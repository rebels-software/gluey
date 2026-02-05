// Copyright (C) 2026 Rebels Software
//
// Licensed under the Apache License, Version 2.0 (the "License")
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http;
using System.Text.Json;
using Gluey.Cli.Api;
using Gluey.Parser;
using Gluey.Runtime;
using Gluey.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var rootCommand = new RootCommand("Gluey - IoT Message Router CLI");

// validate command
var validateCommand = new Command("validate", "Validate a .gflow file syntax");
var validateFileArgument = new Argument<FileInfo>("file", "The .gflow file to validate");
validateCommand.AddArgument(validateFileArgument);

validateCommand.SetHandler(async (InvocationContext context) =>
{
    var file = context.ParseResult.GetValueForArgument(validateFileArgument);
    var exitCode = await ValidateFile(file);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(validateCommand);

// run command
var runCommand = new Command("run", "Run a .gflow workflow as a daemon");
var runFileArgument = new Argument<FileInfo>("file", "The .gflow file to run");
runCommand.AddArgument(runFileArgument);

runCommand.SetHandler(async (InvocationContext context) =>
{
    var file = context.ParseResult.GetValueForArgument(runFileArgument);
    var exitCode = await RunWorkflow(file);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(runCommand);

// daemon command group
var daemonCommand = new Command("daemon", "Manage the Gluey daemon");

// daemon start subcommand
var daemonStartCommand = new Command("start", "Start the Gluey daemon");
var portOption = new Option<int>(
    name: "--port",
    getDefaultValue: () => GlueyDaemonService.DefaultPort,
    description: "The port to listen on");
daemonStartCommand.AddOption(portOption);

daemonStartCommand.SetHandler(async (InvocationContext context) =>
{
    var port = context.ParseResult.GetValueForOption(portOption);
    var exitCode = await StartDaemon(port);
    context.ExitCode = exitCode;
});

daemonCommand.AddCommand(daemonStartCommand);

// daemon stop subcommand
var daemonStopCommand = new Command("stop", "Stop the Gluey daemon");

daemonStopCommand.SetHandler(async (InvocationContext context) =>
{
    var exitCode = await StopDaemon();
    context.ExitCode = exitCode;
});

daemonCommand.AddCommand(daemonStopCommand);

// daemon status subcommand
var daemonStatusCommand = new Command("status", "Check the Gluey daemon status");

daemonStatusCommand.SetHandler(async (InvocationContext context) =>
{
    var exitCode = await DaemonStatus();
    context.ExitCode = exitCode;
});

daemonCommand.AddCommand(daemonStatusCommand);
rootCommand.AddCommand(daemonCommand);

return await rootCommand.InvokeAsync(args);

static async Task<int> ValidateFile(FileInfo file)
{
    if (!file.Exists)
    {
        Console.Error.WriteLine($"Error: File not found: {file.FullName}");
        return 1;
    }

    try
    {
        var source = await File.ReadAllTextAsync(file.FullName);

        // Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        // Parse
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();

        // Validate semantic correctness (plugins, structure)
        var validator = FlowValidator.CreateDefault();
        var validationResult = validator.Validate(flow);

        // Report warnings
        foreach (var warning in validationResult.Warnings)
        {
            Console.Error.WriteLine($"Warning: {warning}");
        }

        // Report errors
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                Console.Error.WriteLine($"Error: {error}");
            }
            return 1;
        }

        Console.WriteLine($"✓ Valid: {file.Name}");
        return 0;
    }
    catch (ParseException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> RunWorkflow(FileInfo file)
{
    if (!file.Exists)
    {
        Console.Error.WriteLine($"Error: File not found: {file.FullName}");
        return 1;
    }

    try
    {
        // Parse the flow file first to get workflow name and version for startup message
        var source = await File.ReadAllTextAsync(file.FullName);
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Gluey.Parser.Parser(tokens);
        var flow = parser.Parse();

        // Print startup message
        Console.WriteLine($"Starting workflow '{flow.Name}' v{flow.Version}...");

        // Build the host with GlueyHostedService
        var builder = Host.CreateApplicationBuilder();

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Register services
        builder.Services.AddSingleton(new PluginRegistry());
        builder.Services.AddSingleton<GlueyHostedService>(sp =>
        {
            var registry = sp.GetRequiredService<PluginRegistry>();
            var logger = sp.GetRequiredService<ILogger<GlueyHostedService>>();
            return new GlueyHostedService(file.FullName, registry, logger);
        });
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GlueyHostedService>());

        var host = builder.Build();

        // Set up cancellation for Ctrl+C and SIGTERM
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutting down...");
            cts.Cancel();
        };

        // Handle SIGTERM (docker stop, systemd, etc.)
        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            Console.WriteLine("Shutting down...");
            cts.Cancel();
        };

        // Run the host and wait for shutdown
        await host.RunAsync(cts.Token);

        return 0;
    }
    catch (ParseException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> StartDaemon(int port)
{
    try
    {
        // Create the web application builder
        var builder = WebApplication.CreateBuilder();

        // Configure Kestrel to listen on specified port
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(port);
        });

        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Register singleton services
        builder.Services.AddSingleton<PluginRegistry>();
        builder.Services.AddSingleton<FileStateStore>();
        builder.Services.AddSingleton<WorkflowManager>(sp =>
        {
            var registry = sp.GetRequiredService<PluginRegistry>();
            var logger = sp.GetRequiredService<ILogger<WorkflowManager>>();
            return new WorkflowManager(registry, logger);
        });
        builder.Services.AddSingleton<GlueyDaemonService>(sp =>
        {
            var workflowManager = sp.GetRequiredService<WorkflowManager>();
            var stateStore = sp.GetRequiredService<FileStateStore>();
            var logger = sp.GetRequiredService<ILogger<GlueyDaemonService>>();
            return new GlueyDaemonService(workflowManager, stateStore, logger, port);
        });
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GlueyDaemonService>());

        // Register WorkflowManager provider for DI in API endpoints
        builder.Services.AddScoped<WorkflowManager>(sp =>
            sp.GetRequiredService<GlueyDaemonService>().WorkflowManager);

        var app = builder.Build();

        // Map API endpoints
        app.MapWorkflowEndpoints();
        app.MapDaemonEndpoints();

        // Set up cancellation for Ctrl+C
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Shutting down...");
            cts.Cancel();
        };

        // Handle SIGTERM (docker stop, systemd, etc.)
        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            Console.WriteLine("Shutting down...");
            cts.Cancel();
        };

        // Print startup message
        Console.WriteLine($"Gluey daemon started on port {port}");

        // Run the application
        await app.RunAsync(cts.Token);

        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> StopDaemon()
{
    try
    {
        // Read daemon config to discover port
        var stateStore = new FileStateStore();
        var daemonConfig = await stateStore.LoadDaemonConfigAsync();

        if (daemonConfig == null)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }

        var (port, _) = daemonConfig.Value;

        // Send shutdown request to daemon
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            var response = await httpClient.PostAsync($"http://localhost:{port}/api/shutdown", null);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Daemon stopped");
                return 0;
            }

            Console.Error.WriteLine($"Error: Daemon returned status {response.StatusCode}");
            return 1;
        }
        catch (HttpRequestException)
        {
            // Connection failed - daemon might already be stopped
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
            // Timeout - daemon might already be stopped
            Console.WriteLine("Daemon is not running");
            return 0;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> DaemonStatus()
{
    try
    {
        // Read daemon config to discover port and pid
        var stateStore = new FileStateStore();
        var daemonConfig = await stateStore.LoadDaemonConfigAsync();

        if (daemonConfig == null)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }

        var (port, pid) = daemonConfig.Value;

        // Try to get health from daemon
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            var response = await httpClient.GetAsync($"http://localhost:{port}/api/health");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var health = JsonSerializer.Deserialize<HealthResponse>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (health != null)
                {
                    Console.WriteLine($"Daemon running on port {port} (pid {pid})");
                    Console.WriteLine($"  Status: {health.Status}");
                    Console.WriteLine($"  Workflows: {health.Workflows}");
                    Console.WriteLine($"  Uptime: {health.Uptime}");
                    return 0;
                }
            }

            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (HttpRequestException)
        {
            // Connection failed - daemon is not running
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
            // Timeout - daemon is not running
            Console.WriteLine("Daemon is not running");
            return 0;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

/// <summary>
/// Response DTO for daemon health check.
/// </summary>
sealed record HealthResponse(string Status, int Workflows, string Uptime);

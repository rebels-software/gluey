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

// load command
var loadCommand = new Command("load", "Load a workflow into the daemon");
var loadFileArgument = new Argument<FileInfo>("file", "The .gflow file to load");
loadCommand.AddArgument(loadFileArgument);

loadCommand.SetHandler(async (InvocationContext context) =>
{
    var file = context.ParseResult.GetValueForArgument(loadFileArgument);
    var exitCode = await LoadWorkflow(file);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(loadCommand);

// unload command
var unloadCommand = new Command("unload", "Unload a workflow from the daemon");
var unloadIdentifierArgument = new Argument<string>("identifier", "The workflow name or ID to unload");
unloadCommand.AddArgument(unloadIdentifierArgument);

unloadCommand.SetHandler(async (InvocationContext context) =>
{
    var identifier = context.ParseResult.GetValueForArgument(unloadIdentifierArgument);
    var exitCode = await UnloadWorkflow(identifier);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(unloadCommand);

// list command
var listCommand = new Command("list", "List all workflows loaded in the daemon");

listCommand.SetHandler(async (InvocationContext context) =>
{
    var exitCode = await ListWorkflows();
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(listCommand);

// start command
var startCommand = new Command("start", "Start a workflow");
var startIdentifierArgument = new Argument<string>("identifier", "The workflow name or ID to start");
startCommand.AddArgument(startIdentifierArgument);

startCommand.SetHandler(async (InvocationContext context) =>
{
    var identifier = context.ParseResult.GetValueForArgument(startIdentifierArgument);
    var exitCode = await StartWorkflow(identifier);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(startCommand);

// stop command (for workflows)
var stopCommand = new Command("stop", "Stop a workflow");
var stopIdentifierArgument = new Argument<string>("identifier", "The workflow name or ID to stop");
stopCommand.AddArgument(stopIdentifierArgument);

stopCommand.SetHandler(async (InvocationContext context) =>
{
    var identifier = context.ParseResult.GetValueForArgument(stopIdentifierArgument);
    var exitCode = await StopWorkflowCommand(identifier);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(stopCommand);

// pause command
var pauseCommand = new Command("pause", "Pause a workflow");
var pauseIdentifierArgument = new Argument<string>("identifier", "The workflow name or ID to pause");
pauseCommand.AddArgument(pauseIdentifierArgument);

pauseCommand.SetHandler(async (InvocationContext context) =>
{
    var identifier = context.ParseResult.GetValueForArgument(pauseIdentifierArgument);
    var exitCode = await PauseWorkflow(identifier);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(pauseCommand);

// reload command
var reloadCommand = new Command("reload", "Hot reload a workflow (re-parses .gflow file)");
var reloadIdentifierArgument = new Argument<string>("identifier", "The workflow name or ID to reload");
reloadCommand.AddArgument(reloadIdentifierArgument);

reloadCommand.SetHandler(async (InvocationContext context) =>
{
    var identifier = context.ParseResult.GetValueForArgument(reloadIdentifierArgument);
    var exitCode = await ReloadWorkflow(identifier);
    context.ExitCode = exitCode;
});

rootCommand.AddCommand(reloadCommand);

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

static async Task<int> LoadWorkflow(FileInfo file)
{
    try
    {
        // Check if file exists
        if (!file.Exists)
        {
            Console.Error.WriteLine($"Error: File not found: {file.FullName}");
            return 1;
        }

        // Read daemon config to discover port
        var stateStore = new FileStateStore();
        var daemonConfig = await stateStore.LoadDaemonConfigAsync();

        if (daemonConfig == null)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }

        var (port, _) = daemonConfig.Value;

        // Send load request to daemon with absolute path
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            var absolutePath = Path.GetFullPath(file.FullName);
            var request = new LoadWorkflowRequest(absolutePath);
            var json = JsonSerializer.Serialize(request, DaemonApi.JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"http://localhost:{port}/api/workflows", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<LoadWorkflowResponse>(responseJson, DaemonApi.JsonOptions);
                if (result != null)
                {
                    // Get the workflow info to retrieve its name
                    var infoResponse = await httpClient.GetAsync($"http://localhost:{port}/api/workflows/{result.Id}");
                    if (infoResponse.IsSuccessStatusCode)
                    {
                        var infoJson = await infoResponse.Content.ReadAsStringAsync();
                        var info = JsonSerializer.Deserialize<WorkflowInfoResponse>(infoJson, DaemonApi.JsonOptions);
                        if (info != null)
                        {
                            Console.WriteLine($"Loaded workflow: {info.Name} ({info.Id})");
                            return 0;
                        }
                    }
                    Console.WriteLine($"Loaded workflow: ({result.Id})");
                }
                return 0;
            }

            // Parse error response
            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(responseJson, DaemonApi.JsonOptions);
            Console.Error.WriteLine(errorResponse?.Error ?? "Unknown error");
            return 1;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
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

static async Task<int> UnloadWorkflow(string identifier)
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

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            // List workflows to find matching one
            var listResponse = await httpClient.GetAsync($"http://localhost:{port}/api/workflows");
            if (!listResponse.IsSuccessStatusCode)
            {
                Console.Error.WriteLine("Failed to list workflows");
                return 1;
            }

            var listJson = await listResponse.Content.ReadAsStringAsync();
            var workflows = JsonSerializer.Deserialize<List<WorkflowInfoResponse>>(listJson, DaemonApi.JsonOptions);

            if (workflows == null || workflows.Count == 0)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Find by ID (GUID) or by name
            WorkflowInfoResponse? matchedWorkflow = null;

            if (Guid.TryParse(identifier, out var guidId))
            {
                matchedWorkflow = workflows.FirstOrDefault(w => w.Id == guidId);
            }

            if (matchedWorkflow == null)
            {
                matchedWorkflow = workflows.FirstOrDefault(w =>
                    string.Equals(w.Name, identifier, StringComparison.OrdinalIgnoreCase));
            }

            if (matchedWorkflow == null)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Unload the workflow
            var deleteResponse = await httpClient.DeleteAsync($"http://localhost:{port}/api/workflows/{matchedWorkflow.Id}");
            if (deleteResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Unloaded workflow: {matchedWorkflow.Name}");
                return 0;
            }

            Console.Error.WriteLine($"Failed to unload workflow: {matchedWorkflow.Name}");
            return 1;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
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

static async Task<int> ListWorkflows()
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

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            var response = await httpClient.GetAsync($"http://localhost:{port}/api/workflows");
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine("Failed to list workflows");
                return 1;
            }

            var json = await response.Content.ReadAsStringAsync();
            var workflows = JsonSerializer.Deserialize<List<WorkflowInfoResponse>>(json, DaemonApi.JsonOptions);

            if (workflows == null || workflows.Count == 0)
            {
                Console.WriteLine("No workflows loaded");
                return 0;
            }

            // Print table header
            Console.WriteLine($"{"ID",-36} {"NAME",-21} {"STATUS",-10}");

            // Print workflows
            foreach (var workflow in workflows)
            {
                Console.WriteLine($"{workflow.Id,-36} {workflow.Name,-21} {workflow.Status,-10}");
            }

            return 0;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
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

static async Task<int> StartWorkflow(string identifier)
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

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            // List workflows to find matching one
            var listResponse = await httpClient.GetAsync($"http://localhost:{port}/api/workflows");
            if (!listResponse.IsSuccessStatusCode)
            {
                Console.Error.WriteLine("Failed to list workflows");
                return 1;
            }

            var listJson = await listResponse.Content.ReadAsStringAsync();
            var workflows = JsonSerializer.Deserialize<List<WorkflowInfoResponse>>(listJson, DaemonApi.JsonOptions);

            if (workflows == null || workflows.Count == 0)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Find by ID (GUID) or by name
            WorkflowInfoResponse? matchedWorkflow = null;

            if (Guid.TryParse(identifier, out var guidId))
            {
                matchedWorkflow = workflows.FirstOrDefault(w => w.Id == guidId);
            }

            if (matchedWorkflow == null)
            {
                matchedWorkflow = workflows.FirstOrDefault(w =>
                    string.Equals(w.Name, identifier, StringComparison.OrdinalIgnoreCase));
            }

            if (matchedWorkflow == null)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Start the workflow
            var response = await httpClient.PostAsync($"http://localhost:{port}/api/workflows/{matchedWorkflow.Id}/start", null);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Started workflow: {matchedWorkflow.Name}");
                return 0;
            }

            // Parse error response
            var responseJson = await response.Content.ReadAsStringAsync();
            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(responseJson, DaemonApi.JsonOptions);
            Console.Error.WriteLine($"Error: {errorResponse?.Error ?? "Unknown error"}");
            return 1;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
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

static async Task<int> StopWorkflowCommand(string identifier)
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

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            // List workflows to find matching one
            var listResponse = await httpClient.GetAsync($"http://localhost:{port}/api/workflows");
            if (!listResponse.IsSuccessStatusCode)
            {
                Console.Error.WriteLine("Failed to list workflows");
                return 1;
            }

            var listJson = await listResponse.Content.ReadAsStringAsync();
            var workflows = JsonSerializer.Deserialize<List<WorkflowInfoResponse>>(listJson, DaemonApi.JsonOptions);

            if (workflows == null || workflows.Count == 0)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Find by ID (GUID) or by name
            WorkflowInfoResponse? matchedWorkflow = null;

            if (Guid.TryParse(identifier, out var guidId))
            {
                matchedWorkflow = workflows.FirstOrDefault(w => w.Id == guidId);
            }

            if (matchedWorkflow == null)
            {
                matchedWorkflow = workflows.FirstOrDefault(w =>
                    string.Equals(w.Name, identifier, StringComparison.OrdinalIgnoreCase));
            }

            if (matchedWorkflow == null)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Stop the workflow
            var response = await httpClient.PostAsync($"http://localhost:{port}/api/workflows/{matchedWorkflow.Id}/stop", null);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Stopped workflow: {matchedWorkflow.Name}");
                return 0;
            }

            // Parse error response
            var responseJson = await response.Content.ReadAsStringAsync();
            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(responseJson, DaemonApi.JsonOptions);
            Console.Error.WriteLine($"Error: {errorResponse?.Error ?? "Unknown error"}");
            return 1;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
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

static async Task<int> PauseWorkflow(string identifier)
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

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            // List workflows to find matching one
            var listResponse = await httpClient.GetAsync($"http://localhost:{port}/api/workflows");
            if (!listResponse.IsSuccessStatusCode)
            {
                Console.Error.WriteLine("Failed to list workflows");
                return 1;
            }

            var listJson = await listResponse.Content.ReadAsStringAsync();
            var workflows = JsonSerializer.Deserialize<List<WorkflowInfoResponse>>(listJson, DaemonApi.JsonOptions);

            if (workflows == null || workflows.Count == 0)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Find by ID (GUID) or by name
            WorkflowInfoResponse? matchedWorkflow = null;

            if (Guid.TryParse(identifier, out var guidId))
            {
                matchedWorkflow = workflows.FirstOrDefault(w => w.Id == guidId);
            }

            if (matchedWorkflow == null)
            {
                matchedWorkflow = workflows.FirstOrDefault(w =>
                    string.Equals(w.Name, identifier, StringComparison.OrdinalIgnoreCase));
            }

            if (matchedWorkflow == null)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Pause the workflow
            var response = await httpClient.PostAsync($"http://localhost:{port}/api/workflows/{matchedWorkflow.Id}/pause", null);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Paused workflow: {matchedWorkflow.Name}");
                return 0;
            }

            // Parse error response
            var responseJson = await response.Content.ReadAsStringAsync();
            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(responseJson, DaemonApi.JsonOptions);
            Console.Error.WriteLine($"Error: {errorResponse?.Error ?? "Unknown error"}");
            return 1;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
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

static async Task<int> ReloadWorkflow(string identifier)
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

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        try
        {
            // List workflows to find matching one
            var listResponse = await httpClient.GetAsync($"http://localhost:{port}/api/workflows");
            if (!listResponse.IsSuccessStatusCode)
            {
                Console.Error.WriteLine("Failed to list workflows");
                return 1;
            }

            var listJson = await listResponse.Content.ReadAsStringAsync();
            var workflows = JsonSerializer.Deserialize<List<WorkflowInfoResponse>>(listJson, DaemonApi.JsonOptions);

            if (workflows == null || workflows.Count == 0)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Find by ID (GUID) or by name
            WorkflowInfoResponse? matchedWorkflow = null;

            if (Guid.TryParse(identifier, out var guidId))
            {
                matchedWorkflow = workflows.FirstOrDefault(w => w.Id == guidId);
            }

            if (matchedWorkflow == null)
            {
                matchedWorkflow = workflows.FirstOrDefault(w =>
                    string.Equals(w.Name, identifier, StringComparison.OrdinalIgnoreCase));
            }

            if (matchedWorkflow == null)
            {
                Console.WriteLine($"Workflow not found: {identifier}");
                return 0;
            }

            // Reload the workflow
            var response = await httpClient.PostAsync($"http://localhost:{port}/api/workflows/{matchedWorkflow.Id}/reload", null);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Reloaded workflow: {matchedWorkflow.Name}");
                return 0;
            }

            // Parse error response
            var responseJson = await response.Content.ReadAsStringAsync();
            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(responseJson, DaemonApi.JsonOptions);
            Console.Error.WriteLine($"Error: {errorResponse?.Error ?? "Unknown error"}");
            return 1;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Daemon is not running");
            return 0;
        }
        catch (TaskCanceledException)
        {
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

/// <summary>
/// Response DTO for workflow load operation.
/// </summary>
sealed record LoadWorkflowResponse(Guid Id);

/// <summary>
/// Request DTO for loading a workflow.
/// </summary>
sealed record LoadWorkflowRequest(string FilePath);

/// <summary>
/// Response DTO for workflow information.
/// </summary>
sealed record WorkflowInfoResponse(
    Guid Id,
    string Name,
    string Version,
    string FilePath,
    string Status,
    DateTimeOffset? StartedAt,
    string? LastError,
    long MessageCount);

/// <summary>
/// Response DTO for error messages.
/// </summary>
sealed record ErrorResponse(string? Error);

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
using Gluey.Parser;
using Gluey.Runtime;
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

        // Configure logging - only warnings and errors in production
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

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

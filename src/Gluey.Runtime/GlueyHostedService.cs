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

using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using Gluey.Parser;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gluey.Runtime;

/// <summary>
/// Hosted service that manages the workflow lifecycle.
/// Parses the flow file, initializes plugins, runs the workflow, and handles graceful shutdown.
/// </summary>
public sealed class GlueyHostedService : IHostedService, IAsyncDisposable
{
    private readonly string _flowFilePath;
    private readonly PluginRegistry _pluginRegistry;
    private readonly ILogger<GlueyHostedService> _logger;

    private Flow? _flow;
    private IInputPlugin? _inputPlugin;
    private List<ITransformPlugin>? _transformPlugins;
    private IOutputPlugin? _outputPlugin;
    private WorkflowRunner? _workflowRunner;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    /// <summary>
    /// Gets the parsed flow after startup, if parsing succeeded.
    /// </summary>
    public Flow? Flow => _flow;

    /// <summary>
    /// Creates a new GlueyHostedService.
    /// </summary>
    /// <param name="flowFilePath">Path to the .gflow file to execute.</param>
    /// <param name="pluginRegistry">Registry for creating plugin instances.</param>
    /// <param name="logger">Logger for startup/shutdown events.</param>
    public GlueyHostedService(
        string flowFilePath,
        PluginRegistry pluginRegistry,
        ILogger<GlueyHostedService> logger)
    {
        _flowFilePath = flowFilePath ?? throw new ArgumentNullException(nameof(flowFilePath));
        _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the workflow: parses the flow file, initializes plugins, and starts the WorkflowRunner.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting workflow from {FlowFile}", _flowFilePath);

        // Parse the flow file
        _flow = ParseFlowFile(_flowFilePath);
        _logger.LogInformation("Parsed flow '{Name}' v{Version}", _flow.Name, _flow.Version);

        // Initialize plugins
        await InitializePluginsAsync(_flow, cancellationToken);
        _logger.LogInformation("Plugins initialized successfully");

        // Create and start the workflow runner
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workflowRunner = new WorkflowRunner(
            _inputPlugin!,
            _transformPlugins!,
            _outputPlugin!);

        _runTask = _workflowRunner.RunAsync(_cts.Token);
        _logger.LogInformation("Workflow '{Name}' started", _flow.Name);
    }

    /// <summary>
    /// Stops the workflow: signals cancellation and waits for drain (5s timeout).
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_flow == null || _cts == null || _runTask == null)
        {
            return;
        }

        _logger.LogInformation("Stopping workflow '{Name}'...", _flow.Name);

        // Signal cancellation to the workflow
        await _cts.CancelAsync();

        // Wait for the workflow to drain with a 5-second timeout
        var drainTimeout = TimeSpan.FromSeconds(5);
        using var timeoutCts = new CancellationTokenSource(drainTimeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            await _runTask.WaitAsync(combinedCts.Token);
            _logger.LogInformation("Workflow '{Name}' drained successfully", _flow.Name);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Workflow '{Name}' drain timed out after {Timeout}s",
                _flow.Name,
                drainTimeout.TotalSeconds);
        }

        _logger.LogInformation("Workflow '{Name}' stopped", _flow.Name);
    }

    /// <summary>
    /// Disposes of all resources (plugins, cancellation sources).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _cts?.Dispose();

        if (_inputPlugin != null)
        {
            await _inputPlugin.DisposeAsync();
        }

        if (_outputPlugin != null)
        {
            await _outputPlugin.DisposeAsync();
        }

        _transformPlugins = null;
        _workflowRunner = null;
    }

    /// <summary>
    /// Parses the .gflow file and returns the Flow AST.
    /// </summary>
    private static Flow ParseFlowFile(string filePath)
    {
        var source = File.ReadAllText(filePath);
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        return parser.Parse();
    }

    /// <summary>
    /// Initializes all plugins from the parsed Flow.
    /// </summary>
    private async Task InitializePluginsAsync(Flow flow, CancellationToken cancellationToken)
    {
        // Create and initialize input plugin
        _inputPlugin = _pluginRegistry.CreateInput(flow.Input.Type);
        var inputConfig = MergeInputConfig(flow.Input);
        await _inputPlugin.InitializeAsync(inputConfig, cancellationToken);

        // Create and initialize transform plugins
        _transformPlugins = new List<ITransformPlugin>();
        foreach (var step in flow.PipelineSteps)
        {
            var transform = _pluginRegistry.CreateTransform(step.Type);
            await transform.InitializeAsync(step.Config, cancellationToken);
            _transformPlugins.Add(transform);
        }

        // For now, use the last pipeline step as output if it's an output type
        // or use console as default. This will be enhanced in US-011/US-012.
        // For MVP, we'll detect output from routes or pipeline end.
        _outputPlugin = DetermineOutputPlugin(flow);
        if (_outputPlugin != null)
        {
            var outputConfig = DetermineOutputConfig(flow);
            await _outputPlugin.InitializeAsync(outputConfig, cancellationToken);
        }
        else
        {
            // Fallback: create a console output for demo purposes
            // This will be replaced when ConsoleOutput is implemented
            throw new InvalidOperationException(
                "No output destination found in workflow. " +
                "Define an output in a route destination or as the final pipeline step.");
        }
    }

    /// <summary>
    /// Merges the input URL into the config dictionary if present.
    /// </summary>
    private static IReadOnlyDictionary<string, System.Text.Json.JsonElement> MergeInputConfig(InputNode input)
    {
        var config = new Dictionary<string, System.Text.Json.JsonElement>(input.Config);

        if (!string.IsNullOrEmpty(input.Url) && !config.ContainsKey("url"))
        {
            config["url"] = System.Text.Json.JsonSerializer.SerializeToElement(input.Url);
        }

        return config;
    }

    /// <summary>
    /// Determines the output plugin from the flow definition.
    /// Looks for route destinations or a terminal output node.
    /// </summary>
    private IOutputPlugin? DetermineOutputPlugin(Flow flow)
    {
        // Check routes for output destinations
        foreach (var route in flow.Routes)
        {
            if (route.DestinationSteps.Count > 0)
            {
                var firstDest = route.DestinationSteps[0];
                // Try to create as output plugin
                try
                {
                    return _pluginRegistry.CreateOutput(firstDest.Type);
                }
                catch (ArgumentException)
                {
                    // Not an output plugin, continue checking
                }
            }
        }

        // Check last pipeline step
        if (flow.PipelineSteps.Count > 0)
        {
            var lastStep = flow.PipelineSteps[^1];
            try
            {
                return _pluginRegistry.CreateOutput(lastStep.Type);
            }
            catch (ArgumentException)
            {
                // Not an output plugin
            }
        }

        return null;
    }

    /// <summary>
    /// Determines the output config from the flow definition.
    /// </summary>
    private static IReadOnlyDictionary<string, System.Text.Json.JsonElement> DetermineOutputConfig(Flow flow)
    {
        // Check routes for output destinations
        foreach (var route in flow.Routes)
        {
            if (route.DestinationSteps.Count > 0)
            {
                return route.DestinationSteps[0].Config;
            }
        }

        // Check last pipeline step
        if (flow.PipelineSteps.Count > 0)
        {
            return flow.PipelineSteps[^1].Config;
        }

        return new Dictionary<string, System.Text.Json.JsonElement>();
    }
}

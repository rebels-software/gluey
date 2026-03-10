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
    private IOutputPlugin? _defaultOutputPlugin;
    private Dictionary<string, RoutePipeline>? _routeOutputs;
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

        if (_routeOutputs != null && _routeOutputs.Count > 0)
        {
            // Routing is enabled - use constructor with route outputs
            _workflowRunner = new WorkflowRunner(
                _inputPlugin!,
                _transformPlugins!,
                _defaultOutputPlugin, // Can be null if all messages must route
                _routeOutputs);
            _logger.LogInformation("Workflow '{Name}' configured with {RouteCount} route outputs", _flow.Name, _routeOutputs.Count);
        }
        else
        {
            // No routing - use simple constructor
            _workflowRunner = new WorkflowRunner(
                _inputPlugin!,
                _transformPlugins!,
                _defaultOutputPlugin!);
        }

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

        if (_defaultOutputPlugin != null)
        {
            await _defaultOutputPlugin.DisposeAsync();
        }

        // Dispose route output plugins
        if (_routeOutputs != null)
        {
            foreach (var routePipeline in _routeOutputs.Values)
            {
                foreach (var output in routePipeline.Outputs)
                {
                    await output.DisposeAsync();
                }
            }
        }

        _transformPlugins = null;
        _routeOutputs = null;
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

        // Determine which pipeline steps are transforms vs output
        // Check if the last pipeline step is an output plugin
        var pipelineSteps = flow.PipelineSteps;
        var transformStepCount = pipelineSteps.Count;
        PipelineStep? outputStep = null;

        if (pipelineSteps.Count > 0)
        {
            var lastStep = pipelineSteps[^1];
            if (_pluginRegistry.IsOutputPlugin(lastStep.Type))
            {
                // Last step is an output plugin - exclude it from transforms
                outputStep = lastStep;
                transformStepCount = pipelineSteps.Count - 1;
            }
        }

        // Create and initialize transform plugins (excluding output step if present)
        _transformPlugins = new List<ITransformPlugin>();
        for (int i = 0; i < transformStepCount; i++)
        {
            var step = pipelineSteps[i];
            var transform = _pluginRegistry.CreateTransform(step.Type);
            await transform.InitializeAsync(step.Config, cancellationToken);
            _transformPlugins.Add(transform);
        }

        // Check if we have routing (routes with destinations defined)
        var routesWithDestinations = flow.Routes.Where(r => r.DestinationSteps.Count > 0).ToList();
        if (routesWithDestinations.Count > 0)
        {
            // Initialize route output pipelines
            _routeOutputs = new Dictionary<string, RoutePipeline>(StringComparer.OrdinalIgnoreCase);

            foreach (var route in routesWithDestinations)
            {
                var routePipeline = await CreateRoutePipelineAsync(route, cancellationToken);
                _routeOutputs[route.Name] = routePipeline;
                _logger.LogDebug("Initialized route '{RouteName}' with {OutputCount} output(s)",
                    route.Name, routePipeline.Outputs.Count);
            }

            // Default output is optional when routing is enabled
            // Only set if there's an output in the main pipeline
            if (outputStep != null)
            {
                _defaultOutputPlugin = _pluginRegistry.CreateOutput(outputStep.Type);
                var outputConfig = MergeOutputConfig(outputStep);
                await _defaultOutputPlugin.InitializeAsync(outputConfig, cancellationToken);
            }
        }
        else
        {
            // No routing - initialize single output
            if (outputStep != null)
            {
                // Use the last pipeline step as output
                _defaultOutputPlugin = _pluginRegistry.CreateOutput(outputStep.Type);
                var outputConfig = MergeOutputConfig(outputStep);
                await _defaultOutputPlugin.InitializeAsync(outputConfig, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException(
                    "No output destination found in workflow. " +
                    "Define an output as the final pipeline step or in route destinations.");
            }
        }
    }

    /// <summary>
    /// Creates a RoutePipeline from a Route definition.
    /// </summary>
    private async Task<RoutePipeline> CreateRoutePipelineAsync(Route route, CancellationToken cancellationToken)
    {
        var transforms = new List<ITransformPlugin>();
        var outputs = new List<IOutputPlugin>();

        foreach (var step in route.DestinationSteps)
        {
            if (_pluginRegistry.IsOutputPlugin(step.Type))
            {
                // This is an output plugin for this route
                var output = _pluginRegistry.CreateOutput(step.Type);
                var outputConfig = MergeOutputConfig(step);
                await output.InitializeAsync(outputConfig, cancellationToken);
                outputs.Add(output);
            }
            else if (_pluginRegistry.IsTransformPlugin(step.Type))
            {
                // This is a transform plugin in the route pipeline
                var transform = _pluginRegistry.CreateTransform(step.Type);
                await transform.InitializeAsync(step.Config, cancellationToken);
                transforms.Add(transform);
            }
        }

        if (outputs.Count == 0)
        {
            throw new InvalidOperationException(
                $"Route '{route.Name}' has no output destination defined. " +
                "Each route must have at least one output plugin.");
        }

        return new RoutePipeline(transforms, outputs);
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
    /// Merges the output "target" into "url" config if present (for route destinations like mqtt("url")).
    /// The parser stores the parenthesized argument as "target", but plugins expect "url".
    /// </summary>
    private static IReadOnlyDictionary<string, System.Text.Json.JsonElement> MergeOutputConfig(PipelineStep step)
    {
        var config = new Dictionary<string, System.Text.Json.JsonElement>(step.Config);

        // Route destinations store parenthesized arg as "target",
        // pipeline steps store it as "field" — check both
        if (!config.ContainsKey("url"))
        {
            System.Text.Json.JsonElement? source = null;
            if (config.TryGetValue("target", out var targetElement) && targetElement.ValueKind == System.Text.Json.JsonValueKind.String)
                source = targetElement;
            else if (config.TryGetValue("field", out var fieldElement) && fieldElement.ValueKind == System.Text.Json.JsonValueKind.String)
                source = fieldElement;

            if (source.HasValue)
                config["url"] = source.Value;
        }

        return config;
    }

}

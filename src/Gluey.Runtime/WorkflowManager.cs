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

using System.Collections.Concurrent;
using System.Text.Json;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using Gluey.Parser;
using Microsoft.Extensions.Logging;

namespace Gluey.Runtime;

/// <summary>
/// Manages multiple workflows with independent lifecycles.
/// Provides methods to load, unload, start, and stop workflows.
/// </summary>
public sealed class WorkflowManager : IAsyncDisposable
{
    private readonly PluginRegistry _pluginRegistry;
    private readonly ILogger<WorkflowManager> _logger;
    private readonly ConcurrentDictionary<Guid, WorkflowInstance> _workflows = new();
    private readonly TimeSpan _drainTimeout = TimeSpan.FromSeconds(5);
    private bool _disposed;

    /// <summary>
    /// Creates a new WorkflowManager.
    /// </summary>
    /// <param name="pluginRegistry">Registry for creating plugin instances.</param>
    /// <param name="logger">Logger for workflow lifecycle events.</param>
    public WorkflowManager(PluginRegistry pluginRegistry, ILogger<WorkflowManager> logger)
    {
        _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads a workflow from a .gflow file. Parses the file and creates WorkflowInfo in Draft status.
    /// </summary>
    /// <param name="filePath">Path to the .gflow file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The workflow ID.</returns>
    public async Task<Guid> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _logger.LogInformation("Loading workflow from {FilePath}", filePath);

        // Parse the flow file
        var flow = ParseFlowFile(filePath);
        _logger.LogDebug("Parsed flow '{Name}' v{Version}", flow.Name, flow.Version);

        // Create WorkflowInfo in Draft status
        var info = WorkflowInfo.Create(flow.Name, flow.Version, filePath);

        // Create workflow instance
        var instance = new WorkflowInstance
        {
            Info = info,
            Flow = flow
        };

        // Add to dictionary
        if (!_workflows.TryAdd(info.Id, instance))
        {
            throw new InvalidOperationException($"Workflow with ID {info.Id} already exists");
        }

        _logger.LogInformation("Loaded workflow '{Name}' v{Version} with ID {Id}",
            flow.Name, flow.Version, info.Id);

        return await Task.FromResult(info.Id);
    }

    /// <summary>
    /// Unloads a workflow. Stops it if running and removes it from the manager.
    /// </summary>
    /// <param name="id">The workflow ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UnloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_workflows.TryGetValue(id, out var instance))
        {
            _logger.LogWarning("Workflow {Id} not found for unload", id);
            return;
        }

        _logger.LogInformation("Unloading workflow '{Name}' ({Id})", instance.Info.Name, id);

        // Stop if running
        if (instance.Info.Status == WorkflowStatus.Active)
        {
            await StopAsync(id, cancellationToken);
        }

        // Remove from dictionary
        if (_workflows.TryRemove(id, out var removed))
        {
            // Dispose resources
            await DisposeInstanceAsync(removed);
            _logger.LogInformation("Unloaded workflow '{Name}' ({Id})", removed.Info.Name, id);
        }
    }

    /// <summary>
    /// Starts a workflow. Initializes plugins and begins processing messages.
    /// </summary>
    /// <param name="id">The workflow ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_workflows.TryGetValue(id, out var instance))
        {
            throw new InvalidOperationException($"Workflow {id} not found");
        }

        if (instance.Info.Status == WorkflowStatus.Active)
        {
            _logger.LogWarning("Workflow '{Name}' ({Id}) is already running", instance.Info.Name, id);
            return;
        }

        _logger.LogInformation("Starting workflow '{Name}' ({Id})", instance.Info.Name, id);

        try
        {
            // Initialize plugins if not already done
            if (instance.Input == null)
            {
                await InitializePluginsAsync(instance, cancellationToken);
                _logger.LogDebug("Plugins initialized for workflow '{Name}'", instance.Info.Name);
            }

            // Create cancellation token source for this workflow
            instance.Cts = new CancellationTokenSource();

            // Create and start the workflow runner
            WorkflowRunner runner;
            if (instance.RouteOutputs != null && instance.RouteOutputs.Count > 0)
            {
                runner = new WorkflowRunner(
                    instance.Input!,
                    instance.Transforms!,
                    instance.Output,
                    instance.RouteOutputs);
            }
            else
            {
                runner = new WorkflowRunner(
                    instance.Input!,
                    instance.Transforms!,
                    instance.Output!);
            }

            // Start the workflow
            instance.RunTask = runner.RunAsync(instance.Cts.Token);

            // Update status to Active
            instance.Info = instance.Info.WithStarted();

            _logger.LogInformation("Workflow '{Name}' ({Id}) started", instance.Info.Name, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start workflow '{Name}' ({Id})", instance.Info.Name, id);
            instance.Info = instance.Info.WithError(ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Stops a workflow. Signals cancellation and waits for drain (5s timeout).
    /// </summary>
    /// <param name="id">The workflow ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_workflows.TryGetValue(id, out var instance))
        {
            throw new InvalidOperationException($"Workflow {id} not found");
        }

        if (instance.Info.Status != WorkflowStatus.Active)
        {
            _logger.LogWarning("Workflow '{Name}' ({Id}) is not running", instance.Info.Name, id);
            return;
        }

        _logger.LogInformation("Stopping workflow '{Name}' ({Id})", instance.Info.Name, id);

        if (instance.Cts != null && instance.RunTask != null)
        {
            // Signal cancellation
            await instance.Cts.CancelAsync();

            // Wait for drain with timeout
            using var timeoutCts = new CancellationTokenSource(_drainTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                await instance.RunTask.WaitAsync(combinedCts.Token);
                _logger.LogDebug("Workflow '{Name}' drained successfully", instance.Info.Name);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Workflow '{Name}' drain timed out after {Timeout}s",
                    instance.Info.Name,
                    _drainTimeout.TotalSeconds);
            }

            // Dispose the CTS
            instance.Cts.Dispose();
            instance.Cts = null;
            instance.RunTask = null;
        }

        // Update status to Stopped
        instance.Info = instance.Info.WithStatus(WorkflowStatus.Stopped);

        _logger.LogInformation("Workflow '{Name}' ({Id}) stopped", instance.Info.Name, id);
    }

    /// <summary>
    /// Pauses a workflow. Signals cancellation and waits for drain (5s timeout).
    /// Workflow remains loaded with plugins initialized and can be resumed with StartAsync.
    /// </summary>
    /// <param name="id">The workflow ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PauseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_workflows.TryGetValue(id, out var instance))
        {
            throw new InvalidOperationException($"Workflow {id} not found");
        }

        if (instance.Info.Status != WorkflowStatus.Active)
        {
            throw new InvalidOperationException(
                $"Cannot pause workflow '{instance.Info.Name}' ({id}) because it is not running. " +
                $"Current status: {instance.Info.Status}");
        }

        _logger.LogInformation("Pausing workflow '{Name}' ({Id})", instance.Info.Name, id);

        if (instance.Cts != null && instance.RunTask != null)
        {
            // Signal cancellation
            await instance.Cts.CancelAsync();

            // Wait for drain with timeout
            using var timeoutCts = new CancellationTokenSource(_drainTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                await instance.RunTask.WaitAsync(combinedCts.Token);
                _logger.LogDebug("Workflow '{Name}' drained successfully", instance.Info.Name);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Workflow '{Name}' drain timed out after {Timeout}s",
                    instance.Info.Name,
                    _drainTimeout.TotalSeconds);
            }

            // Dispose the CTS
            instance.Cts.Dispose();
            instance.Cts = null;
            instance.RunTask = null;
        }

        // Update status to Paused (plugins remain initialized)
        instance.Info = instance.Info.WithStatus(WorkflowStatus.Paused);

        _logger.LogInformation("Workflow '{Name}' ({Id}) paused", instance.Info.Name, id);
    }

    /// <summary>
    /// Reloads a workflow by re-parsing its .gflow file.
    /// If the workflow was running, it will be stopped, reloaded, and restarted.
    /// If stopped or paused, only the Flow definition is updated.
    /// </summary>
    /// <param name="id">The workflow ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_workflows.TryGetValue(id, out var instance))
        {
            throw new InvalidOperationException($"Workflow {id} not found");
        }

        var wasActive = instance.Info.Status == WorkflowStatus.Active;
        var filePath = instance.Info.FilePath;

        _logger.LogInformation("Reloading workflow '{Name}' ({Id}) from {FilePath}",
            instance.Info.Name, id, filePath);

        try
        {
            // Stop the workflow if it's running
            if (wasActive)
            {
                await StopAsync(id, cancellationToken);
            }

            // Dispose current plugins so they get re-initialized on next start
            await DisposePluginsAsync(instance);

            // Re-parse the flow file
            var flow = ParseFlowFile(filePath);
            _logger.LogDebug("Re-parsed flow '{Name}' v{Version}", flow.Name, flow.Version);

            // Update the instance with new flow
            instance.Flow = flow;
            instance.Info = instance.Info with
            {
                Name = flow.Name,
                Version = flow.Version
            };

            _logger.LogInformation("Workflow '{Name}' ({Id}) reloaded successfully",
                instance.Info.Name, id);

            // Restart if it was active before
            if (wasActive)
            {
                await StartAsync(id, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload workflow '{Name}' ({Id})",
                instance.Info.Name, id);
            instance.Info = instance.Info.WithError($"Reload failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Lists all loaded workflows.
    /// </summary>
    /// <returns>All WorkflowInfo entries.</returns>
    public IReadOnlyList<WorkflowInfo> List()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _workflows.Values.Select(i => i.Info).ToList();
    }

    /// <summary>
    /// Gets a single workflow by ID.
    /// </summary>
    /// <param name="id">The workflow ID.</param>
    /// <returns>The WorkflowInfo, or null if not found.</returns>
    public WorkflowInfo? Get(Guid id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _workflows.TryGetValue(id, out var instance) ? instance.Info : null;
    }

    /// <summary>
    /// Disposes of all workflows and resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("Disposing WorkflowManager with {Count} workflows", _workflows.Count);

        // Stop and dispose all workflows
        foreach (var kvp in _workflows)
        {
            var instance = kvp.Value;
            try
            {
                if (instance.Info.Status == WorkflowStatus.Active && instance.Cts != null)
                {
                    await instance.Cts.CancelAsync();
                    if (instance.RunTask != null)
                    {
                        try
                        {
                            await instance.RunTask.WaitAsync(_drainTimeout);
                        }
                        catch (OperationCanceledException) { }
                        catch (TimeoutException) { }
                    }
                }
                await DisposeInstanceAsync(instance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing workflow '{Name}' ({Id})",
                    instance.Info.Name, kvp.Key);
            }
        }

        _workflows.Clear();
        _logger.LogInformation("WorkflowManager disposed");
    }

    /// <summary>
    /// Parses a .gflow file and returns the Flow AST.
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
    /// Initializes all plugins for a workflow instance.
    /// </summary>
    private async Task InitializePluginsAsync(WorkflowInstance instance, CancellationToken cancellationToken)
    {
        var flow = instance.Flow!;

        // Create and initialize input plugin
        instance.Input = _pluginRegistry.CreateInput(flow.Input.Type);
        var inputConfig = MergeInputConfig(flow.Input);
        await instance.Input.InitializeAsync(inputConfig, cancellationToken);

        // Determine which pipeline steps are transforms vs output
        var pipelineSteps = flow.PipelineSteps;
        var transformStepCount = pipelineSteps.Count;
        PipelineStep? outputStep = null;

        if (pipelineSteps.Count > 0)
        {
            var lastStep = pipelineSteps[^1];
            if (_pluginRegistry.IsOutputPlugin(lastStep.Type))
            {
                outputStep = lastStep;
                transformStepCount = pipelineSteps.Count - 1;
            }
        }

        // Create and initialize transform plugins
        instance.Transforms = new List<ITransformPlugin>();
        for (int i = 0; i < transformStepCount; i++)
        {
            var step = pipelineSteps[i];
            var transform = _pluginRegistry.CreateTransform(step.Type);
            await transform.InitializeAsync(step.Config, cancellationToken);
            instance.Transforms.Add(transform);
        }

        // Check if we have routing
        var routesWithDestinations = flow.Routes.Where(r => r.DestinationSteps.Count > 0).ToList();
        if (routesWithDestinations.Count > 0)
        {
            instance.RouteOutputs = new Dictionary<string, RoutePipeline>(StringComparer.OrdinalIgnoreCase);

            foreach (var route in routesWithDestinations)
            {
                var routePipeline = await CreateRoutePipelineAsync(route, cancellationToken);
                instance.RouteOutputs[route.Name] = routePipeline;
            }

            // Default output is optional when routing is enabled
            if (outputStep != null)
            {
                instance.Output = _pluginRegistry.CreateOutput(outputStep.Type);
                await instance.Output.InitializeAsync(outputStep.Config, cancellationToken);
            }
        }
        else
        {
            // No routing - initialize single output
            if (outputStep != null)
            {
                instance.Output = _pluginRegistry.CreateOutput(outputStep.Type);
                await instance.Output.InitializeAsync(outputStep.Config, cancellationToken);
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
        IOutputPlugin? output = null;

        foreach (var step in route.DestinationSteps)
        {
            if (_pluginRegistry.IsOutputPlugin(step.Type))
            {
                output = _pluginRegistry.CreateOutput(step.Type);
                var outputConfig = MergeOutputConfig(step);
                await output.InitializeAsync(outputConfig, cancellationToken);
            }
            else if (_pluginRegistry.IsTransformPlugin(step.Type))
            {
                var transform = _pluginRegistry.CreateTransform(step.Type);
                await transform.InitializeAsync(step.Config, cancellationToken);
                transforms.Add(transform);
            }
        }

        if (output == null)
        {
            throw new InvalidOperationException(
                $"Route '{route.Name}' has no output destination defined. " +
                "Each route must have at least one output plugin.");
        }

        return new RoutePipeline(transforms, output);
    }

    /// <summary>
    /// Merges the input URL into the config dictionary if present.
    /// </summary>
    private static IReadOnlyDictionary<string, JsonElement> MergeInputConfig(InputNode input)
    {
        var config = new Dictionary<string, JsonElement>(input.Config);

        if (!string.IsNullOrEmpty(input.Url) && !config.ContainsKey("url"))
        {
            config["url"] = JsonSerializer.SerializeToElement(input.Url);
        }

        return config;
    }

    /// <summary>
    /// Merges the output "target" into "url" config if present.
    /// </summary>
    private static IReadOnlyDictionary<string, JsonElement> MergeOutputConfig(PipelineStep step)
    {
        var config = new Dictionary<string, JsonElement>(step.Config);

        if (config.TryGetValue("target", out var targetElement) && !config.ContainsKey("url"))
        {
            if (targetElement.ValueKind == JsonValueKind.String)
            {
                config["url"] = targetElement;
            }
        }

        return config;
    }

    /// <summary>
    /// Disposes only the plugins for a workflow instance (for reload).
    /// Does not dispose the CTS or touch the RunTask.
    /// </summary>
    private static async ValueTask DisposePluginsAsync(WorkflowInstance instance)
    {
        if (instance.Input != null)
        {
            await instance.Input.DisposeAsync();
            instance.Input = null;
        }

        if (instance.Transforms != null)
        {
            instance.Transforms.Clear();
            instance.Transforms = null;
        }

        if (instance.Output != null)
        {
            await instance.Output.DisposeAsync();
            instance.Output = null;
        }

        if (instance.RouteOutputs != null)
        {
            foreach (var routePipeline in instance.RouteOutputs.Values)
            {
                await routePipeline.Output.DisposeAsync();
            }
            instance.RouteOutputs.Clear();
            instance.RouteOutputs = null;
        }
    }

    /// <summary>
    /// Disposes resources for a workflow instance.
    /// </summary>
    private static async ValueTask DisposeInstanceAsync(WorkflowInstance instance)
    {
        instance.Cts?.Dispose();

        if (instance.Input != null)
        {
            await instance.Input.DisposeAsync();
        }

        if (instance.Output != null)
        {
            await instance.Output.DisposeAsync();
        }

        if (instance.RouteOutputs != null)
        {
            foreach (var routePipeline in instance.RouteOutputs.Values)
            {
                await routePipeline.Output.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Internal class to track per-workflow runtime state.
    /// </summary>
    private sealed class WorkflowInstance
    {
        public required WorkflowInfo Info { get; set; }
        public Flow? Flow { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        public Task? RunTask { get; set; }
        public IInputPlugin? Input { get; set; }
        public List<ITransformPlugin>? Transforms { get; set; }
        public IOutputPlugin? Output { get; set; }
        public Dictionary<string, RoutePipeline>? RouteOutputs { get; set; }
    }
}

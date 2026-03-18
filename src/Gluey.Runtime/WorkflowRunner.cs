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
using Microsoft.Extensions.Logging;

namespace Gluey.Runtime;

/// <summary>
/// Executes the workflow pipeline: reads from input, applies transforms, writes to output.
/// Supports conditional routing to multiple output pipelines based on route metadata.
/// </summary>
public sealed class WorkflowRunner
{
    /// <summary>
    /// Metadata key used by RouteTransform to store the matched route name.
    /// </summary>
    public const string RouteMetadataKey = "_route";

    /// <summary>
    /// Maps plugin type strings to human-friendly display names for log messages.
    /// </summary>
    private static readonly Dictionary<string, string> TransformDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["json.parse"] = "JSON Parse",
        ["filter"] = "Filter",
        ["transform"] = "Transform",
        ["decode.binary"] = "Binary Decode",
        ["decode.base64"] = "Base64 Decode",
        ["decode.hex"] = "Hex Decode",
        ["route"] = "Route",
        ["validate_schema"] = "Schema Validate",
    };

    /// <summary>
    /// Returns the friendly display name for a plugin type, or the raw type if no mapping exists.
    /// </summary>
    private static string GetDisplayName(string type) =>
        TransformDisplayNames.TryGetValue(type, out var name) ? name : type;

    private readonly IInputPlugin _input;
    private readonly IReadOnlyList<ITransformPlugin> _transforms;
    private readonly IOutputPlugin? _defaultOutput;
    private readonly IReadOnlyDictionary<string, RoutePipeline> _routeOutputs;
    private readonly ILogger? _logger;

    /// <summary>
    /// Creates a new WorkflowRunner with a single output (no routing).
    /// </summary>
    /// <param name="input">The input plugin to read messages from.</param>
    /// <param name="transforms">The transform plugins to apply in sequence.</param>
    /// <param name="output">The output plugin to write messages to.</param>
    /// <param name="logger">Optional logger for pipeline message tracing.</param>
    public WorkflowRunner(
        IInputPlugin input,
        IReadOnlyList<ITransformPlugin> transforms,
        IOutputPlugin output,
        ILogger? logger = null)
        : this(input, transforms, output, new Dictionary<string, RoutePipeline>(), logger)
    {
    }

    /// <summary>
    /// Creates a new WorkflowRunner with multiple route outputs for conditional routing.
    /// </summary>
    /// <param name="input">The input plugin to read messages from.</param>
    /// <param name="transforms">The transform plugins to apply in sequence.</param>
    /// <param name="defaultOutput">The default output plugin (used when no route matches or routing is disabled). Can be null if all messages must route.</param>
    /// <param name="routeOutputs">Dictionary mapping route names to their output pipelines.</param>
    /// <param name="logger">Optional logger for pipeline message tracing.</param>
    public WorkflowRunner(
        IInputPlugin input,
        IReadOnlyList<ITransformPlugin> transforms,
        IOutputPlugin? defaultOutput,
        IReadOnlyDictionary<string, RoutePipeline> routeOutputs,
        ILogger? logger = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
        _defaultOutput = defaultOutput;
        _routeOutputs = routeOutputs ?? throw new ArgumentNullException(nameof(routeOutputs));
        _logger = logger;

        // At least one output must be configured
        if (_defaultOutput == null && _routeOutputs.Count == 0)
        {
            throw new ArgumentException("At least one output (default or route) must be configured.");
        }
    }

    /// <summary>
    /// Runs the workflow pipeline until cancellation is requested.
    /// Reads messages from input, applies transforms in sequence, and writes to output.
    /// If route metadata is present, routes to the appropriate output pipeline.
    /// </summary>
    /// <param name="cancellationToken">Token to signal shutdown.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read messages from input plugin via IAsyncEnumerable
            await foreach (var message in _input.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // Log message received from input
                LogMessageReceived(message);

                // Apply each transform in sequence
                var currentMessage = message;
                var filtered = false;

                foreach (var transform in _transforms)
                {
                    // If transform returns null, message is dropped (filtered)
                    currentMessage = await transform.ProcessAsync(currentMessage, cancellationToken).ConfigureAwait(false);

                    if (currentMessage is null)
                    {
                        // Message was filtered - log and break out of transform loop
                        _logger?.LogInformation("Message filtered by {Type}", GetDisplayName(transform.Type));
                        filtered = true;
                        break;
                    }

                    if (transform.Type == "route" && currentMessage.Metadata.TryGetValue(RouteMetadataKey, out var appliedRoute))
                    {
                        _logger?.LogInformation("Route applied: {Route}", appliedRoute);
                    }
                    else
                    {
                        _logger?.LogInformation("{Type} applied", GetDisplayName(transform.Type));
                    }
                }

                // If message survived all transforms, route to appropriate output
                if (!filtered && currentMessage is not null)
                {
                    await RouteMessageAsync(currentMessage, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested - graceful shutdown
        }
    }

    /// <summary>
    /// Logs a message received event with source and optional topic metadata.
    /// </summary>
    private void LogMessageReceived(Message message)
    {
        if (_logger is null) return;

        var source = message.Metadata.TryGetValue("source", out var s) ? s : "unknown";
        if (message.Metadata.TryGetValue("topic", out var topic))
        {
            _logger.LogInformation("Message received from {Source} (topic: {Topic})", source, topic);
        }
        else
        {
            _logger.LogInformation("Message received from {Source}", source);
        }
    }

    /// <summary>
    /// Routes a message to the appropriate output pipeline based on route metadata.
    /// </summary>
    /// <param name="message">The message to route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task RouteMessageAsync(Message message, CancellationToken cancellationToken)
    {
        // Check for route metadata set by RouteTransform
        if (message.Metadata.TryGetValue(RouteMetadataKey, out var routeName) &&
            _routeOutputs.TryGetValue(routeName, out var routePipeline))
        {
            // Execute the route-specific pipeline
            await ExecuteRoutePipelineAsync(message, routePipeline, cancellationToken).ConfigureAwait(false);
        }
        else if (_defaultOutput != null)
        {
            // No route match or no route metadata - use default output
            await WriteToOutputAsync(_defaultOutput, message, cancellationToken).ConfigureAwait(false);
        }
        // If no default output and no route match, message is dropped
    }

    /// <summary>
    /// Writes a message to a single output plugin with logging.
    /// Catches and logs errors so the workflow continues processing.
    /// </summary>
    private async Task WriteToOutputAsync(IOutputPlugin output, Message message, CancellationToken cancellationToken)
    {
        try
        {
            await output.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            var detail = output.ToString();
            if (detail != null && detail != output.GetType().ToString())
            {
                _logger?.LogInformation("Output {Detail}: written", detail);
            }
            else
            {
                _logger?.LogInformation("Output {Type}: written", output.Type);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Output {Type}: {Error}", output.Type, ex.Message);
        }
    }

    /// <summary>
    /// Executes a route-specific pipeline (transforms + fan-out to outputs).
    /// Errors in one output do not stop others from completing.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="pipeline">The route pipeline to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ExecuteRoutePipelineAsync(
        Message message,
        RoutePipeline pipeline,
        CancellationToken cancellationToken)
    {
        var currentMessage = message;

        // Apply route-specific transforms
        foreach (var transform in pipeline.Transforms)
        {
            currentMessage = await transform.ProcessAsync(currentMessage, cancellationToken).ConfigureAwait(false);

            if (currentMessage is null)
            {
                // Message was filtered by route transform - log and drop it
                _logger?.LogInformation("Message filtered by {Type}", GetDisplayName(transform.Type));
                return;
            }

            _logger?.LogInformation("{Type} applied", GetDisplayName(transform.Type));
        }

        // Fan-out to all route outputs in parallel; errors in one don't stop others
        await FanOutToOutputsAsync(pipeline.Outputs, currentMessage, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a message to multiple outputs in parallel with error isolation.
    /// Each output write is wrapped in a try-catch so a failure in one output
    /// does not prevent the others from completing.
    /// </summary>
    /// <param name="outputs">The output plugins to write to.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task FanOutToOutputsAsync(
        IReadOnlyList<IOutputPlugin> outputs,
        Message message,
        CancellationToken cancellationToken)
    {
        if (outputs.Count == 1)
        {
            // Single output - use WriteToOutputAsync for logging
            await WriteToOutputAsync(outputs[0], message, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Multiple outputs - fan-out in parallel with error isolation and logging
        var tasks = outputs.Select(output => WriteToOutputAsync(output, message, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}

/// <summary>
/// Represents a pipeline for a specific route: optional transforms followed by one or more outputs.
/// Supports fan-out to multiple outputs in parallel.
/// </summary>
public sealed class RoutePipeline
{
    /// <summary>
    /// Gets the transforms to apply before sending to outputs (can be empty).
    /// </summary>
    public IReadOnlyList<ITransformPlugin> Transforms { get; }

    /// <summary>
    /// Gets the output plugins for this route. Fan-out sends to all outputs in parallel.
    /// </summary>
    public IReadOnlyList<IOutputPlugin> Outputs { get; }

    /// <summary>
    /// Creates a new RoutePipeline with only a single output (no transforms).
    /// </summary>
    /// <param name="output">The output plugin.</param>
    public RoutePipeline(IOutputPlugin output)
        : this([], [output ?? throw new ArgumentNullException(nameof(output))])
    {
    }

    /// <summary>
    /// Creates a new RoutePipeline with transforms and a single output.
    /// </summary>
    /// <param name="transforms">The transforms to apply.</param>
    /// <param name="output">The output plugin.</param>
    public RoutePipeline(IReadOnlyList<ITransformPlugin> transforms, IOutputPlugin output)
        : this(transforms, [output ?? throw new ArgumentNullException(nameof(output))])
    {
    }

    /// <summary>
    /// Creates a new RoutePipeline with transforms and multiple outputs for fan-out.
    /// </summary>
    /// <param name="transforms">The transforms to apply.</param>
    /// <param name="outputs">The output plugins to fan-out to in parallel.</param>
    public RoutePipeline(IReadOnlyList<ITransformPlugin> transforms, IReadOnlyList<IOutputPlugin> outputs)
    {
        Transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
        Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));

        if (outputs.Count == 0)
        {
            throw new ArgumentException("At least one output must be provided.", nameof(outputs));
        }
    }
}

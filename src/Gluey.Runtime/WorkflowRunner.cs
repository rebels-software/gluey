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

    private readonly IInputPlugin _input;
    private readonly IReadOnlyList<ITransformPlugin> _transforms;
    private readonly IOutputPlugin? _defaultOutput;
    private readonly IReadOnlyDictionary<string, RoutePipeline> _routeOutputs;

    /// <summary>
    /// Creates a new WorkflowRunner with a single output (no routing).
    /// </summary>
    /// <param name="input">The input plugin to read messages from.</param>
    /// <param name="transforms">The transform plugins to apply in sequence.</param>
    /// <param name="output">The output plugin to write messages to.</param>
    public WorkflowRunner(
        IInputPlugin input,
        IReadOnlyList<ITransformPlugin> transforms,
        IOutputPlugin output)
        : this(input, transforms, output, new Dictionary<string, RoutePipeline>())
    {
    }

    /// <summary>
    /// Creates a new WorkflowRunner with multiple route outputs for conditional routing.
    /// </summary>
    /// <param name="input">The input plugin to read messages from.</param>
    /// <param name="transforms">The transform plugins to apply in sequence.</param>
    /// <param name="defaultOutput">The default output plugin (used when no route matches or routing is disabled). Can be null if all messages must route.</param>
    /// <param name="routeOutputs">Dictionary mapping route names to their output pipelines.</param>
    public WorkflowRunner(
        IInputPlugin input,
        IReadOnlyList<ITransformPlugin> transforms,
        IOutputPlugin? defaultOutput,
        IReadOnlyDictionary<string, RoutePipeline> routeOutputs)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
        _defaultOutput = defaultOutput;
        _routeOutputs = routeOutputs ?? throw new ArgumentNullException(nameof(routeOutputs));

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
                // Apply each transform in sequence
                var currentMessage = message;

                foreach (var transform in _transforms)
                {
                    // If transform returns null, message is dropped (filtered)
                    currentMessage = await transform.ProcessAsync(currentMessage, cancellationToken).ConfigureAwait(false);

                    if (currentMessage is null)
                    {
                        // Message was filtered - break out of transform loop
                        break;
                    }
                }

                // If message survived all transforms, route to appropriate output
                if (currentMessage is not null)
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
            await _defaultOutput.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }
        // If no default output and no route match, message is dropped
    }

    /// <summary>
    /// Executes a route-specific pipeline (transforms + output).
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="pipeline">The route pipeline to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task ExecuteRoutePipelineAsync(
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
                // Message was filtered by route transform - drop it
                return;
            }
        }

        // Send to route output
        await pipeline.Output.WriteAsync(currentMessage, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Represents a pipeline for a specific route: optional transforms followed by an output.
/// </summary>
public sealed class RoutePipeline
{
    /// <summary>
    /// Gets the transforms to apply before sending to output (can be empty).
    /// </summary>
    public IReadOnlyList<ITransformPlugin> Transforms { get; }

    /// <summary>
    /// Gets the output plugin for this route.
    /// </summary>
    public IOutputPlugin Output { get; }

    /// <summary>
    /// Creates a new RoutePipeline with only an output (no transforms).
    /// </summary>
    /// <param name="output">The output plugin.</param>
    public RoutePipeline(IOutputPlugin output)
        : this([], output)
    {
    }

    /// <summary>
    /// Creates a new RoutePipeline with transforms and an output.
    /// </summary>
    /// <param name="transforms">The transforms to apply.</param>
    /// <param name="output">The output plugin.</param>
    public RoutePipeline(IReadOnlyList<ITransformPlugin> transforms, IOutputPlugin output)
    {
        Transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }
}

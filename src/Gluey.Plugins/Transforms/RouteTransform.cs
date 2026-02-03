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

using System.Text.Json;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;
using Gluey.Plugins.Common;

namespace Gluey.Plugins.Transforms;

/// <summary>
/// Transform plugin that routes messages to different outputs based on conditions.
/// Evaluates multiple route conditions in order; first matching route wins.
/// Sets the route destination in message metadata for WorkflowRunner to use.
/// </summary>
/// <remarks>
/// Supports:
/// - Multiple route conditions evaluated in order
/// - First matching route wins
/// - * (asterisk) as catch-all/default route
/// - Sets "_route" metadata with the matching route name
/// - Metadata access: $meta.topic, $meta.key
/// - String methods: .split('/')[1], .substring(0, 5), .indexOf('/')
/// </remarks>
public sealed class RouteTransform : ITransformPlugin
{
    /// <summary>
    /// Metadata key used to store the matched route name.
    /// </summary>
    public const string RouteMetadataKey = "_route";

    private readonly List<RouteCondition> _routes = [];

    public string Type => "route";

    /// <summary>
    /// Initializes the route transform with configuration.
    /// Configuration is a map of route names to condition expressions.
    /// </summary>
    /// <param name="config">Configuration containing route name -> condition mappings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Config format: { "route_name": "condition_expression", ... }
        // Conditions are evaluated in order; first match wins
        // Special condition "*" is the catch-all (default) route
        foreach (var kvp in config)
        {
            var routeName = kvp.Key;
            var condition = kvp.Value.ValueKind == JsonValueKind.String
                ? kvp.Value.GetString() ?? "*"
                : "*";

            _routes.Add(new RouteCondition(routeName, condition));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a message by evaluating route conditions and setting the destination.
    /// </summary>
    /// <param name="message">The input message to route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with "_route" metadata set to the matching route name.</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (_routes.Count == 0)
        {
            // No routes configured - pass through
            return Task.FromResult<Message?>(message);
        }

        // Evaluate conditions in order; first match wins
        foreach (var route in _routes)
        {
            if (EvaluateCondition(route.Condition, message))
            {
                // Set the route destination in metadata
                var routedMessage = message.WithMetadata(RouteMetadataKey, route.Name);
                return Task.FromResult<Message?>(routedMessage);
            }
        }

        // No route matched - pass through without route metadata
        // WorkflowRunner can decide what to do (use default output or drop)
        return Task.FromResult<Message?>(message);
    }

    /// <summary>
    /// Evaluates a condition expression against a message (payload and metadata).
    /// </summary>
    /// <param name="condition">The condition expression to evaluate.</param>
    /// <param name="message">The message containing payload and metadata.</param>
    /// <returns>True if condition matches, false otherwise.</returns>
    private static bool EvaluateCondition(string condition, Message message)
    {
        // Special case: * (catch-all) always matches
        if (condition == "*")
        {
            return true;
        }

        // Empty condition also matches all (catch-all behavior)
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        try
        {
            var evaluator = new ExpressionEvaluator(message.Payload.RootElement, message.Metadata);
            return evaluator.EvaluateBoolean(condition);
        }
        catch
        {
            // On evaluation error, don't match this route
            return false;
        }
    }

    /// <summary>
    /// Represents a single route condition.
    /// </summary>
    private sealed record RouteCondition(string Name, string Condition);
}

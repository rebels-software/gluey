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
/// Evaluates multiple route conditions in order. By default, first matching route wins.
/// When mode is "all", evaluates all conditions and sends to every matching route.
/// Sets the route destination in message metadata for WorkflowRunner to use.
/// </summary>
/// <remarks>
/// Supports:
/// - Multiple route conditions evaluated in order
/// - mode: first (default) - first matching route wins
/// - mode: all - all matching routes receive the message (comma-separated in _route metadata)
/// - * (asterisk) as catch-all/default route
/// - Sets "_route" metadata with the matching route name(s)
/// - Metadata access: $meta.topic, $meta.key
/// - String methods: .split('/')[1], .substring(0, 5), .indexOf('/')
/// </remarks>
public sealed class RouteTransform : ITransformPlugin
{
    /// <summary>
    /// Metadata key used to store the matched route name(s).
    /// When mode is "all", multiple route names are comma-separated.
    /// </summary>
    public const string RouteMetadataKey = "_route";

    /// <summary>
    /// Config key used to store the route evaluation mode ("first" or "all").
    /// Prefixed with underscore to avoid collision with route condition names.
    /// </summary>
    internal const string RouteModeConfigKey = "_route_mode";

    private readonly List<RouteCondition> _routes = [];
    private bool _matchAll;

    public string Type => "route";

    /// <summary>
    /// Initializes the route transform with configuration.
    /// Configuration is a map of route names to condition expressions,
    /// plus an optional "_route_mode" key ("first" or "all").
    /// </summary>
    /// <param name="config">Configuration containing route name -> condition mappings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Check for route mode option
        if (config.TryGetValue(RouteModeConfigKey, out var modeElement) &&
            modeElement.ValueKind == JsonValueKind.String)
        {
            _matchAll = string.Equals(modeElement.GetString(), "all", StringComparison.OrdinalIgnoreCase);
        }

        // Config format: { "route_name": "condition_expression", ... }
        // Conditions are evaluated in order; first match wins (default) or all matches (mode: all)
        // Special condition "*" is the catch-all (default) route
        foreach (var kvp in config)
        {
            // Skip internal config keys
            if (kvp.Key.StartsWith('_'))
                continue;

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
    /// In "first" mode (default), sets "_route" to the first matching route name.
    /// In "all" mode, sets "_route" to a comma-separated list of all matching route names.
    /// </summary>
    /// <param name="message">The input message to route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with "_route" metadata set to the matching route name(s).</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (_routes.Count == 0)
        {
            // No routes configured - pass through
            return Task.FromResult<Message?>(message);
        }

        if (_matchAll)
        {
            return ProcessAllMatchesAsync(message);
        }

        return ProcessFirstMatchAsync(message);
    }

    /// <summary>
    /// First-match mode: evaluates conditions in order; first match wins.
    /// </summary>
    private Task<Message?> ProcessFirstMatchAsync(Message message)
    {
        foreach (var route in _routes)
        {
            if (EvaluateCondition(route.Condition, message))
            {
                var routedMessage = message.WithMetadata(RouteMetadataKey, route.Name);
                return Task.FromResult<Message?>(routedMessage);
            }
        }

        // No route matched - pass through without route metadata
        return Task.FromResult<Message?>(message);
    }

    /// <summary>
    /// All-match mode: evaluates all conditions, collects all matches into a comma-separated list.
    /// </summary>
    private Task<Message?> ProcessAllMatchesAsync(Message message)
    {
        var matchedRoutes = new List<string>();

        foreach (var route in _routes)
        {
            if (EvaluateCondition(route.Condition, message))
            {
                matchedRoutes.Add(route.Name);
            }
        }

        if (matchedRoutes.Count == 0)
        {
            // No route matched - pass through without route metadata
            return Task.FromResult<Message?>(message);
        }

        var routeValue = string.Join(",", matchedRoutes);
        var routedMessage = message.WithMetadata(RouteMetadataKey, routeValue);
        return Task.FromResult<Message?>(routedMessage);
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

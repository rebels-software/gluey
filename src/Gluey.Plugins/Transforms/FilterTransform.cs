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
/// Transform plugin that filters messages based on condition expressions.
/// Returns the message if the condition evaluates to true, null if false.
/// </summary>
/// <remarks>
/// Supports:
/// - Comparisons: > &lt; >= &lt;= == !=
/// - Logical operators: &amp;&amp; ||
/// - Nested field access: device.location.type
/// - Literal values: numbers, strings (quoted), booleans
/// - Metadata access: $meta.topic, $meta.key
/// - String methods: .split('/')[1], .substring(0, 5), .indexOf('/')
/// </remarks>
public sealed class FilterTransform : ITransformPlugin
{
    private string _condition = string.Empty;

    public string Type => "filter";

    /// <summary>
    /// Initializes the filter transform with configuration.
    /// </summary>
    /// <param name="config">Configuration containing 'condition' expression.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        if (config.TryGetValue("condition", out var conditionElement) &&
            conditionElement.ValueKind == JsonValueKind.String)
        {
            _condition = conditionElement.GetString() ?? string.Empty;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a message by evaluating the filter condition.
    /// </summary>
    /// <param name="message">The input message to filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message if condition is true, null if false (filtered out).</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_condition))
        {
            // No condition means pass through
            return Task.FromResult<Message?>(message);
        }

        try
        {
            var evaluator = new ExpressionEvaluator(message.Payload.RootElement, message.Metadata);
            var result = evaluator.EvaluateBoolean(_condition);
            return Task.FromResult<Message?>(result ? message : null);
        }
        catch
        {
            // On evaluation error, filter out the message
            return Task.FromResult<Message?>(null);
        }
    }
}

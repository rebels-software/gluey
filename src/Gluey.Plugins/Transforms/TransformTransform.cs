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
/// Transform plugin that creates a new payload from field mappings.
/// Each mapping is an expression that can reference input fields, perform arithmetic,
/// use ternary conditionals, or call built-in functions.
/// </summary>
/// <remarks>
/// Supports:
/// - Direct field copy: device_id: device_id
/// - Nested field access: lat: device.location.lat
/// - Arithmetic: temp_f: temperature * 9/5 + 32
/// - Ternary: level: temp > 30 ? "high" : "normal"
/// - Built-in functions: now(), uuid(), timestamp()
/// - Metadata access: $meta.topic, $meta.key
/// - String methods: .split('/')[1], .substring(0, 5), .indexOf('/')
/// </remarks>
public sealed class TransformTransform : ITransformPlugin
{
    private Dictionary<string, string> _mappings = new();

    public string Type => "transform";

    /// <summary>
    /// Initializes the transform with field mappings from configuration.
    /// </summary>
    /// <param name="config">Configuration containing field name -> expression mappings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        foreach (var kvp in config)
        {
            if (kvp.Value.ValueKind == JsonValueKind.String)
            {
                _mappings[kvp.Key] = kvp.Value.GetString() ?? string.Empty;
            }
            else
            {
                // For non-string values, convert to string representation
                _mappings[kvp.Key] = kvp.Value.ToString();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a message by creating a new payload from field mappings.
    /// </summary>
    /// <param name="message">The input message to transform.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with transformed payload.</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (_mappings.Count == 0)
        {
            // No mappings means pass through
            return Task.FromResult<Message?>(message);
        }

        try
        {
            var evaluator = new ExpressionEvaluator(message.Payload.RootElement, message.Metadata);
            var result = new Dictionary<string, object?>();

            foreach (var mapping in _mappings)
            {
                var value = evaluator.Evaluate(mapping.Value);
                result[mapping.Key] = value;
            }

            // Create new JsonDocument from result
            var jsonString = JsonSerializer.Serialize(result);
            var newPayload = JsonDocument.Parse(jsonString);

            return Task.FromResult<Message?>(message.WithPayload(newPayload));
        }
        catch (Exception ex)
        {
            // On evaluation error, log and return null to filter out
            Console.Error.WriteLine($"[transform] Expression evaluation failed: {ex.Message}");
            return Task.FromResult<Message?>(null);
        }
    }
}

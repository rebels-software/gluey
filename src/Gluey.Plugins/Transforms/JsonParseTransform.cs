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

using System.Text;
using System.Text.Json;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;

namespace Gluey.Plugins.Transforms;

/// <summary>
/// Transform plugin that parses JSON payloads from raw bytes or base64-encoded data.
/// Supports on_error handling with skip (drop message) or route_to (set metadata flag).
/// </summary>
public sealed class JsonParseTransform : ITransformPlugin
{
    private OnErrorAction _onErrorAction = OnErrorAction.Skip;
    private string? _routeToTarget;

    public string Type => "json.parse";

    /// <summary>
    /// Initializes the JSON parse transform with configuration.
    /// </summary>
    /// <param name="config">Configuration containing optional 'on_error' settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Check for on_error configuration
        if (config.TryGetValue("on_error", out var onErrorElement))
        {
            if (onErrorElement.ValueKind == JsonValueKind.String)
            {
                var action = onErrorElement.GetString();
                if (string.Equals(action, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    _onErrorAction = OnErrorAction.Skip;
                }
            }
            else if (onErrorElement.ValueKind == JsonValueKind.Object)
            {
                // Handle object form: { action: "route_to", target: "name" }
                if (onErrorElement.TryGetProperty("action", out var actionElement) &&
                    actionElement.ValueKind == JsonValueKind.String &&
                    string.Equals(actionElement.GetString(), "route_to", StringComparison.OrdinalIgnoreCase))
                {
                    _onErrorAction = OnErrorAction.RouteTo;
                    if (onErrorElement.TryGetProperty("target", out var targetElement) &&
                        targetElement.ValueKind == JsonValueKind.String)
                    {
                        _routeToTarget = targetElement.GetString();
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Processes a message by parsing its payload as JSON.
    /// If the payload is already a JsonDocument, attempts to parse raw_bytes from metadata.
    /// </summary>
    /// <param name="message">The input message to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with parsed JsonDocument payload, or null if filtered.</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if we have raw_bytes in metadata (from binary input like HTTP octet-stream)
            if (message.Metadata.TryGetValue("raw_bytes", out var rawBytesBase64))
            {
                var bytes = Convert.FromBase64String(rawBytesBase64);
                var payload = JsonDocument.Parse(bytes);
                return Task.FromResult<Message?>(message.WithPayload(payload));
            }

            // Try to get string content from the payload and re-parse it
            // This handles cases where the payload is wrapped (e.g., {"data": "..."})
            var root = message.Payload.RootElement;

            // If the payload has a "data" field containing a string, parse that
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var dataElement) &&
                dataElement.ValueKind == JsonValueKind.String)
            {
                var jsonString = dataElement.GetString();
                if (!string.IsNullOrEmpty(jsonString))
                {
                    var payload = JsonDocument.Parse(jsonString);
                    return Task.FromResult<Message?>(message.WithPayload(payload));
                }
            }

            // If the payload is already a proper JSON object/array, just pass it through
            if (root.ValueKind == JsonValueKind.Object || root.ValueKind == JsonValueKind.Array)
            {
                // Clone the payload to ensure we don't have ownership issues
                var payload = JsonDocument.Parse(root.GetRawText());
                return Task.FromResult<Message?>(message.WithPayload(payload));
            }

            // If it's a raw string at root level, try to parse it as JSON
            if (root.ValueKind == JsonValueKind.String)
            {
                var jsonString = root.GetString();
                if (!string.IsNullOrEmpty(jsonString))
                {
                    var payload = JsonDocument.Parse(jsonString);
                    return Task.FromResult<Message?>(message.WithPayload(payload));
                }
            }

            // Unable to parse - handle error
            return HandleError(message);
        }
        catch (JsonException)
        {
            return HandleError(message);
        }
    }

    /// <summary>
    /// Handles parse errors based on configured on_error action.
    /// </summary>
    private Task<Message?> HandleError(Message message)
    {
        switch (_onErrorAction)
        {
            case OnErrorAction.Skip:
                // Return null to filter/drop the message
                return Task.FromResult<Message?>(null);

            case OnErrorAction.RouteTo:
                // Set metadata flag for routing and return the message
                var routedMessage = message.WithMetadata("_route_error", _routeToTarget ?? "error");
                return Task.FromResult<Message?>(routedMessage);

            default:
                return Task.FromResult<Message?>(null);
        }
    }

    private enum OnErrorAction
    {
        Skip,
        RouteTo
    }
}

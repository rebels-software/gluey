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

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gluey.Core.Abstractions;
using Gluey.Core.Models;

namespace Gluey.Plugins.Transforms;

/// <summary>
/// Transform plugin that decodes binary payloads using a specified format.
/// Supports extracting fields using bytes(offset, length, type) format.
///
/// Supported types:
/// - ascii, string: ASCII/UTF-8 string
/// - uint8: unsigned 8-bit integer
/// - int16_be, int16_le: signed 16-bit integer (big/little endian)
/// - uint16_be, uint16_le: unsigned 16-bit integer (big/little endian)
/// - int32_be, int32_le: signed 32-bit integer (big/little endian)
/// - uint32_be, uint32_le: unsigned 32-bit integer (big/little endian)
/// - float32_be, float32_le: 32-bit IEEE 754 float (big/little endian)
/// </summary>
public sealed class BinaryDecodeTransform : ITransformPlugin
{
    private string? _sourceField;
    private List<FieldDefinition> _fieldDefinitions = [];

    public string Type => "decode.binary";

    /// <summary>
    /// Initializes the binary decode transform with configuration.
    /// </summary>
    /// <param name="config">Configuration containing 'format' array with field definitions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Optional source field - if not specified, reads from raw_bytes metadata or payload
        if (config.TryGetValue("field", out var fieldElement) && fieldElement.ValueKind == JsonValueKind.String)
        {
            _sourceField = fieldElement.GetString();
        }

        // Parse format array
        if (config.TryGetValue("format", out var formatElement) && formatElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in formatElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    // Object format: { name: "device_id", offset: 0, length: 4, type: "string" }
                    var def = ParseFieldDefinitionFromObject(item);
                    if (def != null)
                    {
                        _fieldDefinitions.Add(def);
                    }
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    // String format: "device_id: bytes(0, 4, \"string\")"
                    var def = ParseFieldDefinitionFromString(item.GetString());
                    if (def != null)
                    {
                        _fieldDefinitions.Add(def);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Parses a field definition from object format.
    /// </summary>
    private static FieldDefinition? ParseFieldDefinitionFromObject(JsonElement element)
    {
        string? name = null;
        int offset = 0;
        int length = 0;
        string type = "uint8";

        if (element.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
        {
            name = nameEl.GetString();
        }

        if (element.TryGetProperty("offset", out var offsetEl) && offsetEl.ValueKind == JsonValueKind.Number)
        {
            offset = offsetEl.GetInt32();
        }

        if (element.TryGetProperty("length", out var lengthEl) && lengthEl.ValueKind == JsonValueKind.Number)
        {
            length = lengthEl.GetInt32();
        }

        if (element.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            type = typeEl.GetString() ?? "uint8";
        }

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return new FieldDefinition(name, offset, length, type);
    }

    /// <summary>
    /// Parses a field definition from string format like "device_id: bytes(0, 4, \"string\")".
    /// </summary>
    private static FieldDefinition? ParseFieldDefinitionFromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Parse format: "field_name: bytes(offset, length, \"type\")"
        var colonIndex = value.IndexOf(':');
        if (colonIndex < 0)
        {
            return null;
        }

        var name = value[..colonIndex].Trim();
        var rest = value[(colonIndex + 1)..].Trim();

        // Check for bytes(...) function
        if (!rest.StartsWith("bytes(", StringComparison.OrdinalIgnoreCase) || !rest.EndsWith(')'))
        {
            return null;
        }

        // Extract parameters: offset, length, "type"
        var paramsStr = rest[6..^1]; // Remove "bytes(" and ")"
        var parts = SplitParameters(paramsStr);

        if (parts.Count < 3)
        {
            return null;
        }

        if (!int.TryParse(parts[0].Trim(), out var offset))
        {
            return null;
        }

        if (!int.TryParse(parts[1].Trim(), out var length))
        {
            return null;
        }

        var type = parts[2].Trim().Trim('"', '\'');

        return new FieldDefinition(name, offset, length, type);
    }

    /// <summary>
    /// Splits function parameters, handling quoted strings.
    /// </summary>
    private static List<string> SplitParameters(string input)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        foreach (var c in input)
        {
            if (!inQuotes && (c == '"' || c == '\''))
            {
                inQuotes = true;
                quoteChar = c;
                current.Append(c);
            }
            else if (inQuotes && c == quoteChar)
            {
                inQuotes = false;
                current.Append(c);
            }
            else if (!inQuotes && c == ',')
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>
    /// Processes a message by decoding binary payload according to format definitions.
    /// </summary>
    /// <param name="message">The input message to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with decoded fields as JsonDocument payload.</returns>
    public Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] bytes = GetBinaryData(message);

            if (bytes.Length == 0)
            {
                return Task.FromResult<Message?>(null);
            }

            // Build result object with extracted fields
            var result = new JsonObject();

            foreach (var field in _fieldDefinitions)
            {
                var value = ExtractField(bytes, field);
                if (value != null)
                {
                    result[field.Name] = value;
                }
            }

            // Convert to JsonDocument
            var json = result.ToJsonString();
            var payload = JsonDocument.Parse(json);

            return Task.FromResult<Message?>(message.WithPayload(payload));
        }
        catch
        {
            // On error, drop the message
            return Task.FromResult<Message?>(null);
        }
    }

    /// <summary>
    /// Gets binary data from the message based on configuration.
    /// </summary>
    private byte[] GetBinaryData(Message message)
    {
        // Check for raw_bytes in metadata first (from HTTP binary input)
        if (message.Metadata.TryGetValue("raw_bytes", out var rawBytesBase64))
        {
            return Convert.FromBase64String(rawBytesBase64);
        }

        // Check for specific source field in payload
        if (!string.IsNullOrEmpty(_sourceField))
        {
            var root = message.Payload.RootElement;
            if (root.TryGetProperty(_sourceField, out var fieldElement))
            {
                if (fieldElement.ValueKind == JsonValueKind.String)
                {
                    // Field contains base64-encoded data
                    var base64 = fieldElement.GetString();
                    if (!string.IsNullOrEmpty(base64))
                    {
                        return Convert.FromBase64String(base64);
                    }
                }
            }
        }

        // Try to get data from payload "data" field if it's a string (raw data)
        var payloadRoot = message.Payload.RootElement;
        if (payloadRoot.ValueKind == JsonValueKind.Object &&
            payloadRoot.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.String)
        {
            var data = dataElement.GetString();
            if (!string.IsNullOrEmpty(data))
            {
                // Try base64 first
                try
                {
                    return Convert.FromBase64String(data);
                }
                catch
                {
                    // Fall back to UTF-8 encoding
                    return Encoding.UTF8.GetBytes(data);
                }
            }
        }

        return [];
    }

    /// <summary>
    /// Extracts a field value from the byte array according to the field definition.
    /// </summary>
    private static JsonNode? ExtractField(byte[] bytes, FieldDefinition field)
    {
        if (field.Offset < 0 || field.Offset >= bytes.Length)
        {
            return null;
        }

        var endOffset = field.Offset + field.Length;
        if (endOffset > bytes.Length)
        {
            return null;
        }

        var span = bytes.AsSpan(field.Offset, field.Length);

        return field.Type.ToLowerInvariant() switch
        {
            "ascii" or "string" => ExtractString(span),
            "uint8" => ExtractUInt8(span),
            "int16_be" => ExtractInt16Be(span),
            "int16_le" => ExtractInt16Le(span),
            "uint16_be" => ExtractUInt16Be(span),
            "uint16_le" => ExtractUInt16Le(span),
            "int32_be" => ExtractInt32Be(span),
            "int32_le" => ExtractInt32Le(span),
            "uint32_be" => ExtractUInt32Be(span),
            "uint32_le" => ExtractUInt32Le(span),
            "float32_be" => ExtractFloat32Be(span),
            "float32_le" => ExtractFloat32Le(span),
            _ => null
        };
    }

    private static JsonNode ExtractString(ReadOnlySpan<byte> span)
    {
        // Find null terminator or use full length
        var length = span.IndexOf((byte)0);
        if (length < 0) length = span.Length;
        return JsonValue.Create(Encoding.ASCII.GetString(span[..length]));
    }

    private static JsonNode ExtractUInt8(ReadOnlySpan<byte> span)
    {
        return span.Length >= 1 ? JsonValue.Create((int)span[0]) : JsonValue.Create(0);
    }

    private static JsonNode ExtractInt16Be(ReadOnlySpan<byte> span)
    {
        return span.Length >= 2 ? JsonValue.Create((int)BinaryPrimitives.ReadInt16BigEndian(span)) : JsonValue.Create(0);
    }

    private static JsonNode ExtractInt16Le(ReadOnlySpan<byte> span)
    {
        return span.Length >= 2 ? JsonValue.Create((int)BinaryPrimitives.ReadInt16LittleEndian(span)) : JsonValue.Create(0);
    }

    private static JsonNode ExtractUInt16Be(ReadOnlySpan<byte> span)
    {
        return span.Length >= 2 ? JsonValue.Create((int)BinaryPrimitives.ReadUInt16BigEndian(span)) : JsonValue.Create(0);
    }

    private static JsonNode ExtractUInt16Le(ReadOnlySpan<byte> span)
    {
        return span.Length >= 2 ? JsonValue.Create((int)BinaryPrimitives.ReadUInt16LittleEndian(span)) : JsonValue.Create(0);
    }

    private static JsonNode ExtractInt32Be(ReadOnlySpan<byte> span)
    {
        return span.Length >= 4 ? JsonValue.Create(BinaryPrimitives.ReadInt32BigEndian(span)) : JsonValue.Create(0);
    }

    private static JsonNode ExtractInt32Le(ReadOnlySpan<byte> span)
    {
        return span.Length >= 4 ? JsonValue.Create(BinaryPrimitives.ReadInt32LittleEndian(span)) : JsonValue.Create(0);
    }

    private static JsonNode ExtractUInt32Be(ReadOnlySpan<byte> span)
    {
        return span.Length >= 4 ? JsonValue.Create(BinaryPrimitives.ReadUInt32BigEndian(span)) : JsonValue.Create(0);
    }

    private static JsonNode ExtractUInt32Le(ReadOnlySpan<byte> span)
    {
        return span.Length >= 4 ? JsonValue.Create(BinaryPrimitives.ReadUInt32LittleEndian(span)) : JsonValue.Create(0);
    }

    private static JsonNode ExtractFloat32Be(ReadOnlySpan<byte> span)
    {
        if (span.Length < 4) return JsonValue.Create(0.0);
        var intValue = BinaryPrimitives.ReadInt32BigEndian(span);
        return JsonValue.Create(BitConverter.Int32BitsToSingle(intValue));
    }

    private static JsonNode ExtractFloat32Le(ReadOnlySpan<byte> span)
    {
        if (span.Length < 4) return JsonValue.Create(0.0);
        var intValue = BinaryPrimitives.ReadInt32LittleEndian(span);
        return JsonValue.Create(BitConverter.Int32BitsToSingle(intValue));
    }

    /// <summary>
    /// Represents a field definition for binary decoding.
    /// </summary>
    private sealed record FieldDefinition(string Name, int Offset, int Length, string Type);
}

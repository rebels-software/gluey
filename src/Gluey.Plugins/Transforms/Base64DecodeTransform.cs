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

namespace Gluey.Plugins.Transforms;

/// <summary>
/// Transform plugin that decodes base64 strings to raw bytes.
/// Can optionally apply binary decode format to the decoded bytes.
///
/// Configuration options:
/// - field: The payload field containing the base64 string (optional, defaults to looking at common locations)
/// - format: Binary decode format array to apply after base64 decoding (optional)
/// </summary>
public sealed class Base64DecodeTransform : ITransformPlugin
{
    private string? _sourceField;
    private BinaryDecodeTransform? _binaryDecoder;

    public string Type => "decode.base64";

    /// <summary>
    /// Initializes the base64 decode transform with configuration.
    /// </summary>
    /// <param name="config">Configuration containing optional 'field' and 'format' settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Optional source field - if not specified, tries common locations
        if (config.TryGetValue("field", out var fieldElement) && fieldElement.ValueKind == JsonValueKind.String)
        {
            _sourceField = fieldElement.GetString();
        }

        // Optional binary decode format - if specified, decode bytes using BinaryDecodeTransform
        if (config.TryGetValue("format", out var formatElement) && formatElement.ValueKind == JsonValueKind.Array)
        {
            _binaryDecoder = new BinaryDecodeTransform();
            // Pass the format config to binary decoder
            var binaryConfig = new Dictionary<string, JsonElement>
            {
                ["format"] = formatElement
            };
            await _binaryDecoder.InitializeAsync(binaryConfig, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes a message by decoding base64 string to raw bytes.
    /// </summary>
    /// <param name="message">The input message to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with decoded bytes in metadata or as binary-decoded payload.</returns>
    public async Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        try
        {
            var base64String = GetBase64String(message);

            if (string.IsNullOrEmpty(base64String))
            {
                return null;
            }

            // Decode base64 to raw bytes
            byte[] decodedBytes;
            try
            {
                decodedBytes = Convert.FromBase64String(base64String);
            }
            catch (FormatException)
            {
                // Invalid base64 string
                return null;
            }

            // If binary format is specified, pass through binary decoder
            if (_binaryDecoder != null)
            {
                // Store decoded bytes in metadata for BinaryDecodeTransform to consume
                var metadata = new Dictionary<string, string>(message.Metadata)
                {
                    ["raw_bytes"] = Convert.ToBase64String(decodedBytes)
                };
                var intermediateMessage = message.WithMetadata(metadata);
                return await _binaryDecoder.ProcessAsync(intermediateMessage, cancellationToken).ConfigureAwait(false);
            }

            // Without binary format, store decoded bytes in metadata for downstream transforms
            var newMetadata = new Dictionary<string, string>(message.Metadata)
            {
                ["raw_bytes"] = Convert.ToBase64String(decodedBytes)
            };

            return message.WithMetadata(newMetadata);
        }
        catch
        {
            // On error, drop the message
            return null;
        }
    }

    /// <summary>
    /// Gets the base64 string from the message based on configuration.
    /// </summary>
    private string? GetBase64String(Message message)
    {
        var root = message.Payload.RootElement;

        // Check for specific source field in payload
        if (!string.IsNullOrEmpty(_sourceField))
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(_sourceField, out var fieldElement))
            {
                if (fieldElement.ValueKind == JsonValueKind.String)
                {
                    return fieldElement.GetString();
                }
            }
            return null;
        }

        // Try common field names for base64 data
        string[] commonFields = ["frm_payload", "payload", "data", "base64"];
        foreach (var fieldName in commonFields)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(fieldName, out var element))
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrEmpty(value) && IsLikelyBase64(value))
                    {
                        return value;
                    }
                }
            }
        }

        // If the root is a string, try it as base64
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString();
            if (!string.IsNullOrEmpty(value) && IsLikelyBase64(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a string is likely base64 encoded.
    /// </summary>
    private static bool IsLikelyBase64(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Basic check: base64 only contains these characters
        // Allow for padding with = at the end
        var trimmed = value.TrimEnd('=');
        foreach (var c in trimmed)
        {
            if (!char.IsLetterOrDigit(c) && c != '+' && c != '/')
            {
                return false;
            }
        }

        return true;
    }
}

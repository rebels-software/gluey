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
/// Transform plugin that decodes hex strings to raw bytes.
/// Can optionally apply binary decode format to the decoded bytes.
///
/// Configuration options:
/// - field: The payload field containing the hex string (optional, defaults to looking at common locations)
/// - format: Binary decode format array to apply after hex decoding (optional)
///
/// Example hex strings: "41424344" decodes to bytes [0x41, 0x42, 0x43, 0x44] ("ABCD" in ASCII)
/// </summary>
public sealed class HexDecodeTransform : ITransformPlugin
{
    private string? _sourceField;
    private BinaryDecodeTransform? _binaryDecoder;

    public string Type => "decode.hex";

    /// <summary>
    /// Initializes the hex decode transform with configuration.
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
    /// Processes a message by decoding hex string to raw bytes.
    /// </summary>
    /// <param name="message">The input message to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message with decoded bytes in metadata or as binary-decoded payload.</returns>
    public async Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        try
        {
            var hexString = GetHexString(message);

            if (string.IsNullOrEmpty(hexString))
            {
                return null;
            }

            // Decode hex string to raw bytes
            byte[] decodedBytes;
            try
            {
                decodedBytes = HexStringToBytes(hexString);
            }
            catch (FormatException)
            {
                // Invalid hex string
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
    /// Gets the hex string from the message based on configuration.
    /// </summary>
    private string? GetHexString(Message message)
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

        // Try common field names for hex data
        string[] commonFields = ["hex_payload", "hex", "hex_data", "payload", "data"];
        foreach (var fieldName in commonFields)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(fieldName, out var element))
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrEmpty(value) && IsLikelyHex(value))
                    {
                        return value;
                    }
                }
            }
        }

        // If the root is a string, try it as hex
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString();
            if (!string.IsNullOrEmpty(value) && IsLikelyHex(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a string is likely hex encoded.
    /// Hex strings contain only 0-9, a-f, A-F and have even length.
    /// Optionally allows 0x prefix.
    /// </summary>
    private static bool IsLikelyHex(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var hex = value;

        // Remove optional 0x prefix
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        // Hex strings should have even length (two chars per byte)
        if (hex.Length % 2 != 0)
            return false;

        // Check all characters are valid hex
        foreach (var c in hex)
        {
            if (!IsHexChar(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a character is a valid hexadecimal digit.
    /// </summary>
    private static bool IsHexChar(char c)
    {
        return (c >= '0' && c <= '9') ||
               (c >= 'a' && c <= 'f') ||
               (c >= 'A' && c <= 'F');
    }

    /// <summary>
    /// Converts a hex string to a byte array.
    /// Supports optional 0x prefix and spaces between bytes.
    /// </summary>
    /// <param name="hex">The hex string to convert (e.g., "41424344" or "0x41424344")</param>
    /// <returns>Byte array with decoded values</returns>
    /// <exception cref="FormatException">Thrown when the input is not valid hex</exception>
    private static byte[] HexStringToBytes(string hex)
    {
        // Remove optional 0x prefix
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        // Remove spaces (some hex strings are formatted with spaces: "41 42 43 44")
        hex = hex.Replace(" ", "");

        // Must have even length
        if (hex.Length % 2 != 0)
        {
            throw new FormatException("Hex string must have even length");
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var highNibble = HexCharToInt(hex[i * 2]);
            var lowNibble = HexCharToInt(hex[i * 2 + 1]);

            if (highNibble < 0 || lowNibble < 0)
            {
                throw new FormatException($"Invalid hex character at position {i * 2}");
            }

            bytes[i] = (byte)((highNibble << 4) | lowNibble);
        }

        return bytes;
    }

    /// <summary>
    /// Converts a hex character to its integer value (0-15).
    /// </summary>
    /// <param name="c">The hex character</param>
    /// <returns>Integer value 0-15, or -1 if invalid</returns>
    private static int HexCharToInt(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };
    }
}

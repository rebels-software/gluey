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

namespace Gluey.Plugins.Outputs;

/// <summary>
/// Console output plugin that prints messages to stdout for debugging.
/// Formats the payload as indented JSON with a timestamp prefix.
/// </summary>
public sealed class ConsoleOutput : IOutputPlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string Type => "console";

    /// <summary>
    /// Initializes the console output plugin. No configuration required.
    /// </summary>
    public Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default)
    {
        // Console output requires no initialization
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes the message payload to console as formatted JSON with timestamp prefix.
    /// </summary>
    public Task WriteAsync(Message message, CancellationToken cancellationToken = default)
    {
        // Format timestamp in ISO 8601 format
        var timestamp = message.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");

        // Serialize payload as formatted JSON
        var json = JsonSerializer.Serialize(message.Payload, JsonOptions);

        // Print with timestamp prefix
        Console.WriteLine($"[{timestamp}] {json}");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the console output plugin. No cleanup required.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        // Console output requires no cleanup
        return ValueTask.CompletedTask;
    }
}

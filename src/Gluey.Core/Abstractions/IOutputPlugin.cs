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
using Gluey.Core.Models;

namespace Gluey.Core.Abstractions;

/// <summary>
/// Interface for output plugins that send messages to destinations.
/// Output plugins are the terminal nodes of the workflow pipeline.
/// </summary>
public interface IOutputPlugin : IAsyncDisposable
{
    /// <summary>
    /// Gets the plugin type identifier (e.g., "console", "http", "mqtt", "sql").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Initializes the plugin with the provided configuration.
    /// </summary>
    /// <param name="config">Configuration dictionary from the .gflow file.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a message to the output destination.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task WriteAsync(Message message, CancellationToken cancellationToken = default);
}

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
/// Interface for input plugins that emit messages into the workflow pipeline.
/// Input plugins are async enumerable sources of messages.
/// </summary>
public interface IInputPlugin : IAsyncDisposable
{
    /// <summary>
    /// Gets the plugin type identifier (e.g., "http", "mqtt").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Initializes the plugin with the provided configuration.
    /// </summary>
    /// <param name="config">Configuration dictionary from the .gflow file.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads messages from the input source as an async enumerable stream.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop reading.</param>
    /// <returns>An async enumerable of messages.</returns>
    IAsyncEnumerable<Message> ReadAsync(CancellationToken cancellationToken = default);
}

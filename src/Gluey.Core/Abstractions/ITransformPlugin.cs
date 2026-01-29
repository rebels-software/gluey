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
/// Interface for transform plugins that process messages in the workflow pipeline.
/// Transform plugins can modify, filter, or route messages.
/// </summary>
public interface ITransformPlugin
{
    /// <summary>
    /// Gets the plugin type identifier (e.g., "json.parse", "filter", "transform").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Initializes the plugin with the provided configuration.
    /// </summary>
    /// <param name="config">Configuration dictionary from the .gflow file.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task InitializeAsync(IReadOnlyDictionary<string, JsonElement> config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a message and returns the transformed result.
    /// </summary>
    /// <param name="message">The input message to process.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>
    /// The transformed message, or null if the message should be filtered/dropped.
    /// Returning null signals to the pipeline that this message should not continue.
    /// </returns>
    Task<Message?> ProcessAsync(Message message, CancellationToken cancellationToken = default);
}

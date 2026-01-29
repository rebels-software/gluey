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

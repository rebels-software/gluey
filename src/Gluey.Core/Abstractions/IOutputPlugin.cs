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

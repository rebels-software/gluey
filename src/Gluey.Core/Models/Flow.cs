using System.Text.Json;

namespace Gluey.Core.Models;

/// <summary>
/// Represents a complete workflow flow definition parsed from a .gflow file.
/// </summary>
public sealed class Flow
{
    /// <summary>
    /// The name of the flow (e.g., "hello-world").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The version of the flow (e.g., "1.0").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The input source configuration for this flow.
    /// </summary>
    public required InputNode Input { get; init; }

    /// <summary>
    /// The sequence of pipeline steps (transforms) to apply to messages.
    /// </summary>
    public IReadOnlyList<PipelineStep> PipelineSteps { get; init; } = [];

    /// <summary>
    /// The routing rules for conditional message handling.
    /// </summary>
    public IReadOnlyList<Route> Routes { get; init; } = [];
}

/// <summary>
/// Represents an input source configuration in a flow.
/// </summary>
public sealed class InputNode
{
    /// <summary>
    /// The type of input plugin (e.g., "http", "mqtt", "kafka").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The connection URL or path (e.g., "/webhook" or "mqtt://broker:1883").
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Additional configuration options for the input plugin.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Config { get; init; } =
        new Dictionary<string, JsonElement>();
}

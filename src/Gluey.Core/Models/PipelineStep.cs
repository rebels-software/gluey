using System.Text.Json;

namespace Gluey.Core.Models;

/// <summary>
/// Represents a single step in the message processing pipeline.
/// </summary>
public sealed class PipelineStep
{
    /// <summary>
    /// The type of transform plugin (e.g., "json.parse", "filter", "transform").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Configuration options for this pipeline step.
    /// Keys are config names, values are JSON elements representing the config values.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Config { get; init; } =
        new Dictionary<string, JsonElement>();

    /// <summary>
    /// Error handling configuration for this step.
    /// </summary>
    public OnErrorConfig? OnError { get; init; }
}

/// <summary>
/// Specifies how to handle errors in a pipeline step.
/// </summary>
public sealed class OnErrorConfig
{
    /// <summary>
    /// The error handling action to take.
    /// </summary>
    public required OnErrorAction Action { get; init; }

    /// <summary>
    /// The name of the route to send failed messages to (when Action is RouteTo).
    /// </summary>
    public string? RouteName { get; init; }
}

/// <summary>
/// Defines the possible actions when an error occurs in a pipeline step.
/// </summary>
public enum OnErrorAction
{
    /// <summary>
    /// Skip the message and continue processing (drop the message).
    /// </summary>
    Skip,

    /// <summary>
    /// Route the message to a specific error handling route.
    /// </summary>
    RouteTo
}

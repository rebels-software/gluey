using System.Text.Json;

namespace Gluey.Core.Models;

/// <summary>
/// Represents a message flowing through the workflow pipeline.
/// This is a record type for immutability and value-based equality.
/// </summary>
/// <param name="Payload">The message payload as a JsonDocument.</param>
/// <param name="Metadata">Key-value metadata associated with the message.</param>
/// <param name="Timestamp">The timestamp when the message was created or received.</param>
public sealed record Message(
    JsonDocument Payload,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// Creates a new message with the current timestamp.
    /// </summary>
    public static Message Create(JsonDocument payload, IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new Message(
            payload,
            metadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a new message with updated payload, preserving metadata and timestamp.
    /// </summary>
    public Message WithPayload(JsonDocument newPayload)
    {
        return this with { Payload = newPayload };
    }

    /// <summary>
    /// Creates a new message with additional metadata.
    /// </summary>
    public Message WithMetadata(string key, string value)
    {
        var newMetadata = new Dictionary<string, string>(Metadata)
        {
            [key] = value
        };
        return this with { Metadata = newMetadata };
    }

    /// <summary>
    /// Creates a new message with replaced metadata.
    /// </summary>
    public Message WithMetadata(IReadOnlyDictionary<string, string> newMetadata)
    {
        return this with { Metadata = newMetadata };
    }
}

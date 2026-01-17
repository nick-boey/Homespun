namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Represents a message in a Claude Code session.
/// </summary>
public class ClaudeMessage
{
    /// <summary>
    /// Unique identifier for this message.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The session this message belongs to.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// The role of the message sender (user or assistant).
    /// </summary>
    public required ClaudeMessageRole Role { get; init; }

    /// <summary>
    /// The content blocks that make up this message.
    /// </summary>
    public List<ClaudeMessageContent> Content { get; init; } = [];

    /// <summary>
    /// When this message was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this message is still being streamed.
    /// </summary>
    public bool IsStreaming { get; set; }
}

/// <summary>
/// The role of a message sender.
/// </summary>
public enum ClaudeMessageRole
{
    /// <summary>
    /// Message from the user.
    /// </summary>
    User,

    /// <summary>
    /// Message from Claude.
    /// </summary>
    Assistant
}

using System.Collections.Concurrent;

namespace Homespun.Features.ClaudeCode.Services;

/// <inheritdoc />
public sealed class PendingToolCallRegistry : IPendingToolCallRegistry
{
    private readonly ConcurrentDictionary<string, string> _slots = new();

    /// <inheritdoc />
    public void Register(string sessionId, string toolCallId)
    {
        _slots[sessionId] = toolCallId;
    }

    /// <inheritdoc />
    public string? Dequeue(string sessionId)
        => _slots.TryRemove(sessionId, out var id) ? id : null;
}

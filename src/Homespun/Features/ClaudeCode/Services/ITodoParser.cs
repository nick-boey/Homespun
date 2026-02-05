using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Parses todo items from Claude session messages.
/// </summary>
public interface ITodoParser
{
    /// <summary>
    /// Extracts the current todo list from session messages by parsing TodoWrite tool calls.
    /// Returns the most recent state of todos (from the last TodoWrite call).
    /// </summary>
    /// <param name="messages">The list of messages from the session.</param>
    /// <returns>A read-only list of todo items representing the current state.</returns>
    IReadOnlyList<SessionTodoItem> ParseFromMessages(IReadOnlyList<ClaudeMessage> messages);
}

using System.Text.Json;

namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Interface for parsing todo items from Claude session messages.
/// </summary>
public interface ITodoParser
{
    /// <summary>
    /// Extracts the current todo list from session messages by parsing TodoWrite tool calls.
    /// Returns the most recent state of todos (from the last TodoWrite call).
    /// </summary>
    IReadOnlyList<SessionTodoItem> ParseFromMessages(IReadOnlyList<ClaudeMessage> messages);
}

/// <summary>
/// Parses todo items from Claude session messages by extracting TodoWrite tool calls.
/// </summary>
public class TodoParser : ITodoParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public IReadOnlyList<SessionTodoItem> ParseFromMessages(IReadOnlyList<ClaudeMessage> messages)
    {
        var allTodoWrites = messages
            .SelectMany(m => m.Content)
            .Where(c => c.Type == ClaudeContentType.ToolUse && c.ToolName == "TodoWrite")
            .ToList();

        if (allTodoWrites.Count == 0)
        {
            return [];
        }

        var lastTodoWrite = allTodoWrites[^1];

        if (string.IsNullOrEmpty(lastTodoWrite.ToolInput))
        {
            return [];
        }

        return ParseTodoJson(lastTodoWrite.ToolInput);
    }

    private static List<SessionTodoItem> ParseTodoJson(string json)
    {
        try
        {
            var wrapper = JsonSerializer.Deserialize<TodoWriteWrapper>(json, JsonOptions);
            if (wrapper?.Todos == null || wrapper.Todos.Count == 0)
            {
                return [];
            }

            return wrapper.Todos
                .Select(t => new SessionTodoItem
                {
                    Content = t.Content ?? string.Empty,
                    ActiveForm = t.ActiveForm ?? string.Empty,
                    Status = ParseStatus(t.Status)
                })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static TodoStatus ParseStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "pending" => TodoStatus.Pending,
            "in_progress" => TodoStatus.InProgress,
            "completed" => TodoStatus.Completed,
            _ => TodoStatus.Pending
        };
    }

    private class TodoWriteWrapper
    {
        public List<TodoItemJson>? Todos { get; set; }
    }

    private class TodoItemJson
    {
        public string? Content { get; set; }
        public string? ActiveForm { get; set; }
        public string? Status { get; set; }
    }
}

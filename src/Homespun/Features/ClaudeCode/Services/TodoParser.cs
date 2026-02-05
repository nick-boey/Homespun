using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

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
        // Find all TodoWrite tool uses across all messages
        var allTodoWrites = messages
            .SelectMany(m => m.Content)
            .Where(c => c.Type == ClaudeContentType.ToolUse && c.ToolName == "TodoWrite")
            .ToList();

        if (allTodoWrites.Count == 0)
        {
            return [];
        }

        // Get the last (most recent) TodoWrite call
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

    /// <summary>
    /// Wrapper class for deserializing TodoWrite tool input JSON.
    /// </summary>
    private class TodoWriteWrapper
    {
        public List<TodoItemJson>? Todos { get; set; }
    }

    /// <summary>
    /// Raw todo item from JSON deserialization.
    /// </summary>
    private class TodoItemJson
    {
        public string? Content { get; set; }
        public string? ActiveForm { get; set; }
        public string? Status { get; set; }
    }
}

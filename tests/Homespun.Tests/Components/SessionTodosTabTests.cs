using Bunit;
using Homespun.Client.Features.Chat.Components.SessionInfoPanel;
using System.Text.Json;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionTodosTabTests : BunitTestContext
{
    [SetUp]
    public new void Setup()
    {
        base.Setup();
    }

    private static List<ClaudeMessage> CreateMessagesWithTodos(params (string content, string activeForm, string status)[] todos)
    {
        var todoJson = JsonSerializer.Serialize(new
        {
            todos = todos.Select(t => new { content = t.content, activeForm = t.activeForm, status = t.status }).ToList()
        });

        return
        [
            new ClaudeMessage
            {
                SessionId = "test-session",
                Role = ClaudeMessageRole.Assistant,
                Content =
                [
                    new ClaudeMessageContent
                    {
                        Type = ClaudeContentType.ToolUse,
                        ToolName = "TodoWrite",
                        ToolInput = todoJson
                    }
                ]
            }
        ];
    }

    [Test]
    public void SessionTodosTab_WithTodos_DisplaysList()
    {
        // Arrange
        var messages = CreateMessagesWithTodos(
            ("Task 1", "Doing task 1", "pending"),
            ("Task 2", "Doing task 2", "in_progress"),
            ("Task 3", "Doing task 3", "completed"));

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, messages));

        // Assert
        var todoItems = cut.FindAll(".todo-item");
        Assert.That(todoItems, Has.Count.EqualTo(3));
    }

    [Test]
    public void SessionTodosTab_PendingItem_ShowsCircleIcon()
    {
        // Arrange
        var messages = CreateMessagesWithTodos(("Pending task", "Pending task", "pending"));

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, messages));

        // Assert
        var statusIcon = cut.Find(".status-icon.pending");
        Assert.That(statusIcon.TextContent, Does.Contain("○"));
    }

    [Test]
    public void SessionTodosTab_InProgressItem_ShowsSpinnerIcon()
    {
        // Arrange
        var messages = CreateMessagesWithTodos(("In progress task", "Working on task", "in_progress"));

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, messages));

        // Assert
        var statusIcon = cut.Find(".status-icon.in-progress");
        Assert.That(statusIcon, Is.Not.Null);
    }

    [Test]
    public void SessionTodosTab_CompletedItem_ShowsCheckIcon()
    {
        // Arrange
        var messages = CreateMessagesWithTodos(("Completed task", "Completed task", "completed"));

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, messages));

        // Assert
        var statusIcon = cut.Find(".status-icon.completed");
        Assert.That(statusIcon.TextContent, Does.Contain("✓"));
    }

    [Test]
    public void SessionTodosTab_InProgressItem_ShowsActiveForm()
    {
        // Arrange
        var messages = CreateMessagesWithTodos(("Task content", "Currently working on task", "in_progress"));

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, messages));

        // Assert
        var activeForm = cut.Find(".active-form");
        Assert.That(activeForm.TextContent, Does.Contain("Currently working on task"));
    }

    [Test]
    public void SessionTodosTab_NoTodos_ShowsEmptyState()
    {
        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, new List<ClaudeMessage>()));

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState.TextContent, Does.Contain("No tasks tracked"));
    }

    [Test]
    public void SessionTodosTab_NullMessages_ShowsEmptyState()
    {
        // Act
        var cut = Render<SessionTodosTab>();

        // Assert
        var emptyState = cut.Find(".empty-state");
        Assert.That(emptyState, Is.Not.Null);
    }

    [Test]
    public void SessionTodosTab_DisplaysProgressSummary()
    {
        // Arrange
        var messages = CreateMessagesWithTodos(
            ("Task 1", "Task 1", "completed"),
            ("Task 2", "Task 2", "in_progress"),
            ("Task 3", "Task 3", "pending"));

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, messages));

        // Assert
        var summary = cut.Find(".todos-summary");
        Assert.That(summary.TextContent, Does.Contain("1").And.Contain("3")); // 1 of 3 completed
    }
}

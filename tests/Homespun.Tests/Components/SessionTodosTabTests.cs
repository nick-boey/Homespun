using Bunit;
using Homespun.Features.ClaudeCode.Components.SessionInfoPanel;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Homespun.Tests.Components;

[TestFixture]
public class SessionTodosTabTests : BunitTestContext
{
    private Mock<ITodoParser> _mockTodoParser = null!;

    [SetUp]
    public new void Setup()
    {
        base.Setup();
        _mockTodoParser = new Mock<ITodoParser>();
        Services.AddSingleton(_mockTodoParser.Object);
    }

    [Test]
    public void SessionTodosTab_WithTodos_DisplaysList()
    {
        // Arrange
        var todos = new List<SessionTodoItem>
        {
            new() { Content = "Task 1", ActiveForm = "Doing task 1", Status = TodoStatus.Pending },
            new() { Content = "Task 2", ActiveForm = "Doing task 2", Status = TodoStatus.InProgress },
            new() { Content = "Task 3", ActiveForm = "Doing task 3", Status = TodoStatus.Completed }
        };
        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(todos);

        var messages = new List<ClaudeMessage>();

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
        var todos = new List<SessionTodoItem>
        {
            new() { Content = "Pending task", ActiveForm = "Pending task", Status = TodoStatus.Pending }
        };
        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(todos);

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, new List<ClaudeMessage>()));

        // Assert
        var statusIcon = cut.Find(".status-icon.pending");
        Assert.That(statusIcon.TextContent, Does.Contain("○"));
    }

    [Test]
    public void SessionTodosTab_InProgressItem_ShowsSpinnerIcon()
    {
        // Arrange
        var todos = new List<SessionTodoItem>
        {
            new() { Content = "In progress task", ActiveForm = "Working on task", Status = TodoStatus.InProgress }
        };
        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(todos);

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, new List<ClaudeMessage>()));

        // Assert
        var statusIcon = cut.Find(".status-icon.in-progress");
        Assert.That(statusIcon, Is.Not.Null);
    }

    [Test]
    public void SessionTodosTab_CompletedItem_ShowsCheckIcon()
    {
        // Arrange
        var todos = new List<SessionTodoItem>
        {
            new() { Content = "Completed task", ActiveForm = "Completed task", Status = TodoStatus.Completed }
        };
        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(todos);

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, new List<ClaudeMessage>()));

        // Assert
        var statusIcon = cut.Find(".status-icon.completed");
        Assert.That(statusIcon.TextContent, Does.Contain("✓"));
    }

    [Test]
    public void SessionTodosTab_InProgressItem_ShowsActiveForm()
    {
        // Arrange
        var todos = new List<SessionTodoItem>
        {
            new() { Content = "Task content", ActiveForm = "Currently working on task", Status = TodoStatus.InProgress }
        };
        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(todos);

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, new List<ClaudeMessage>()));

        // Assert
        var activeForm = cut.Find(".active-form");
        Assert.That(activeForm.TextContent, Does.Contain("Currently working on task"));
    }

    [Test]
    public void SessionTodosTab_NoTodos_ShowsEmptyState()
    {
        // Arrange
        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(new List<SessionTodoItem>());

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
        // Arrange
        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(new List<SessionTodoItem>());

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
        var todos = new List<SessionTodoItem>
        {
            new() { Content = "Task 1", ActiveForm = "Task 1", Status = TodoStatus.Completed },
            new() { Content = "Task 2", ActiveForm = "Task 2", Status = TodoStatus.InProgress },
            new() { Content = "Task 3", ActiveForm = "Task 3", Status = TodoStatus.Pending }
        };
        _mockTodoParser.Setup(p => p.ParseFromMessages(It.IsAny<IReadOnlyList<ClaudeMessage>>()))
            .Returns(todos);

        // Act
        var cut = Render<SessionTodosTab>(parameters => parameters
            .Add(p => p.Messages, new List<ClaudeMessage>()));

        // Assert
        var summary = cut.Find(".todos-summary");
        Assert.That(summary.TextContent, Does.Contain("1").And.Contain("3")); // 1 of 3 completed
    }
}

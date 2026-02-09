using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SessionTodoItemTests
{
    #region Model Creation Tests

    [Test]
    public void SessionTodoItem_CreatesWithRequiredProperties()
    {
        // Arrange & Act
        var item = new SessionTodoItem
        {
            Content = "Implement feature X",
            ActiveForm = "Implementing feature X",
            Status = TodoStatus.Pending
        };

        // Assert
        Assert.That(item.Content, Is.EqualTo("Implement feature X"));
        Assert.That(item.ActiveForm, Is.EqualTo("Implementing feature X"));
        Assert.That(item.Status, Is.EqualTo(TodoStatus.Pending));
    }

    [Test]
    public void SessionTodoItem_SupportsAllStatuses()
    {
        // Arrange & Act
        var pending = new SessionTodoItem
        {
            Content = "Task 1",
            ActiveForm = "Task 1",
            Status = TodoStatus.Pending
        };

        var inProgress = new SessionTodoItem
        {
            Content = "Task 2",
            ActiveForm = "Task 2",
            Status = TodoStatus.InProgress
        };

        var completed = new SessionTodoItem
        {
            Content = "Task 3",
            ActiveForm = "Task 3",
            Status = TodoStatus.Completed
        };

        // Assert
        Assert.That(pending.Status, Is.EqualTo(TodoStatus.Pending));
        Assert.That(inProgress.Status, Is.EqualTo(TodoStatus.InProgress));
        Assert.That(completed.Status, Is.EqualTo(TodoStatus.Completed));
    }

    #endregion

    #region TodoStatus Enum Tests

    [Test]
    public void TodoStatus_HasExpectedValues()
    {
        // Assert
        Assert.That(Enum.GetValues<TodoStatus>(), Has.Length.EqualTo(3));
        Assert.That(Enum.IsDefined(TodoStatus.Pending), Is.True);
        Assert.That(Enum.IsDefined(TodoStatus.InProgress), Is.True);
        Assert.That(Enum.IsDefined(TodoStatus.Completed), Is.True);
    }

    [Test]
    public void TodoStatus_FromString_ParsesEnumNames()
    {
        // Note: Enum.Parse works with actual enum names, not JSON snake_case values.
        // Snake_case parsing (e.g., "in_progress" -> InProgress) is handled by TodoParser.

        // Act
        var pending = Enum.Parse<TodoStatus>("pending", ignoreCase: true);
        var inProgress = Enum.Parse<TodoStatus>("InProgress", ignoreCase: true);
        var completed = Enum.Parse<TodoStatus>("completed", ignoreCase: true);

        // Assert
        Assert.That(pending, Is.EqualTo(TodoStatus.Pending));
        Assert.That(inProgress, Is.EqualTo(TodoStatus.InProgress));
        Assert.That(completed, Is.EqualTo(TodoStatus.Completed));
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public void SessionTodoItem_AllowsEmptyStrings()
    {
        // Arrange & Act
        var item = new SessionTodoItem
        {
            Content = "",
            ActiveForm = "",
            Status = TodoStatus.Pending
        };

        // Assert
        Assert.That(item.Content, Is.Empty);
        Assert.That(item.ActiveForm, Is.Empty);
    }

    [Test]
    public void SessionTodoItem_AllowsUnicodeContent()
    {
        // Arrange & Act
        var item = new SessionTodoItem
        {
            Content = "实现功能 🚀",
            ActiveForm = "正在实现功能 🚀",
            Status = TodoStatus.InProgress
        };

        // Assert
        Assert.That(item.Content, Is.EqualTo("实现功能 🚀"));
        Assert.That(item.ActiveForm, Is.EqualTo("正在实现功能 🚀"));
    }

    #endregion
}

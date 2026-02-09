using Homespun.Features.ClaudeCode.Services;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class TodoParserTests
{
    private TodoParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new TodoParser();
    }

    #region Parse From Messages Tests

    [Test]
    public void ParseFromMessages_NoTodoWrites_ReturnsEmptyList()
    {
        // Arrange
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("Read", """{"file_path": "/some/file.cs"}"""),
            CreateMessageWithToolUse("Write", """{"file_path": "/some/file.cs"}""")
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseFromMessages_SingleTodoWrite_ExtractsTodos()
    {
        // Arrange
        var todoJson = """
        {
            "todos": [
                {"content": "Task 1", "activeForm": "Doing task 1", "status": "pending"},
                {"content": "Task 2", "activeForm": "Doing task 2", "status": "in_progress"},
                {"content": "Task 3", "activeForm": "Doing task 3", "status": "completed"}
            ]
        }
        """;
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("TodoWrite", todoJson)
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Content, Is.EqualTo("Task 1"));
        Assert.That(result[0].ActiveForm, Is.EqualTo("Doing task 1"));
        Assert.That(result[0].Status, Is.EqualTo(TodoStatus.Pending));
        Assert.That(result[1].Status, Is.EqualTo(TodoStatus.InProgress));
        Assert.That(result[2].Status, Is.EqualTo(TodoStatus.Completed));
    }

    [Test]
    public void ParseFromMessages_MultipleTodoWrites_ReturnsLatestState()
    {
        // Arrange
        var firstTodoJson = """
        {
            "todos": [
                {"content": "Task 1", "activeForm": "Doing task 1", "status": "pending"}
            ]
        }
        """;
        var secondTodoJson = """
        {
            "todos": [
                {"content": "Task 1", "activeForm": "Doing task 1", "status": "completed"},
                {"content": "Task 2", "activeForm": "Doing task 2", "status": "in_progress"}
            ]
        }
        """;
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("TodoWrite", firstTodoJson),
            CreateMessageWithToolUse("TodoWrite", secondTodoJson)
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Content, Is.EqualTo("Task 1"));
        Assert.That(result[0].Status, Is.EqualTo(TodoStatus.Completed));
        Assert.That(result[1].Content, Is.EqualTo("Task 2"));
        Assert.That(result[1].Status, Is.EqualTo(TodoStatus.InProgress));
    }

    [Test]
    public void ParseFromMessages_InvalidJson_ReturnsEmptyList()
    {
        // Arrange
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("TodoWrite", "{ invalid json }")
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseFromMessages_EmptyTodosArray_ReturnsEmptyList()
    {
        // Arrange
        var todoJson = """{"todos": []}""";
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("TodoWrite", todoJson)
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseFromMessages_MissingTodosProperty_ReturnsEmptyList()
    {
        // Arrange
        var todoJson = """{"other": "data"}""";
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("TodoWrite", todoJson)
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseFromMessages_NullInput_ReturnsEmptyList()
    {
        // Arrange
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("TodoWrite", null)
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseFromMessages_ParsesAllStatuses_Correctly()
    {
        // Arrange
        var todoJson = """
        {
            "todos": [
                {"content": "Pending task", "activeForm": "Active", "status": "pending"},
                {"content": "In progress task", "activeForm": "Active", "status": "in_progress"},
                {"content": "Completed task", "activeForm": "Active", "status": "completed"}
            ]
        }
        """;
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("TodoWrite", todoJson)
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result[0].Status, Is.EqualTo(TodoStatus.Pending));
        Assert.That(result[1].Status, Is.EqualTo(TodoStatus.InProgress));
        Assert.That(result[2].Status, Is.EqualTo(TodoStatus.Completed));
    }

    [Test]
    public void ParseFromMessages_MixedToolCalls_OnlyParsesTodos()
    {
        // Arrange
        var todoJson = """
        {
            "todos": [
                {"content": "Task 1", "activeForm": "Doing task 1", "status": "pending"}
            ]
        }
        """;
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("Read", """{"file": "test.cs"}"""),
            CreateMessageWithToolUse("TodoWrite", todoJson),
            CreateMessageWithToolUse("Write", """{"file": "test.cs"}""")
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Content, Is.EqualTo("Task 1"));
    }

    [Test]
    public void ParseFromMessages_EmptyMessages_ReturnsEmptyList()
    {
        // Arrange
        var messages = new List<ClaudeMessage>();

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseFromMessages_MessageWithMultipleTodoWrites_UsesLastOne()
    {
        // Arrange - single message with multiple content blocks
        var message = new ClaudeMessage
        {
            SessionId = "test-session",
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolName = "TodoWrite",
                    ToolInput = """{"todos": [{"content": "First", "activeForm": "First", "status": "pending"}]}"""
                },
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolName = "TodoWrite",
                    ToolInput = """{"todos": [{"content": "Second", "activeForm": "Second", "status": "completed"}]}"""
                }
            ]
        };

        // Act
        var result = _parser.ParseFromMessages([message]);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Content, Is.EqualTo("Second"));
    }

    [Test]
    public void ParseFromMessages_UnknownStatus_DefaultsToPending()
    {
        // Arrange
        var todoJson = """
        {
            "todos": [
                {"content": "Task", "activeForm": "Active", "status": "unknown_status"}
            ]
        }
        """;
        var messages = new List<ClaudeMessage>
        {
            CreateMessageWithToolUse("TodoWrite", todoJson)
        };

        // Act
        var result = _parser.ParseFromMessages(messages);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Status, Is.EqualTo(TodoStatus.Pending));
    }

    #endregion

    #region Helper Methods

    private static ClaudeMessage CreateMessageWithToolUse(string toolName, string? toolInput)
    {
        return new ClaudeMessage
        {
            SessionId = "test-session",
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolName = toolName,
                    ToolInput = toolInput
                }
            ]
        };
    }

    #endregion
}

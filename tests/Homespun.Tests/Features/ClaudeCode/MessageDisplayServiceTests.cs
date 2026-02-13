using Homespun.Client.Services;
using Homespun.Shared.Models.Sessions;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Tests for MessageDisplayService ordering logic to ensure thinking blocks
/// appear in correct order relative to tool use blocks.
/// </summary>
[TestFixture]
public class MessageDisplayServiceTests
{
    private MessageDisplayService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new MessageDisplayService();
    }

    #region Basic Display Item Tests

    [Test]
    public void GetDisplayItems_EmptyMessages_ReturnsEmptyList()
    {
        // Arrange
        var messages = new List<ClaudeMessage>();

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDisplayItems_SingleUserMessage_ReturnsUserMessage()
    {
        // Arrange
        var messages = new List<ClaudeMessage>
        {
            CreateUserMessage("Hello")
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.TypeOf<ClaudeMessage>());
        var msg = (ClaudeMessage)result[0];
        Assert.That(msg.Role, Is.EqualTo(ClaudeMessageRole.User));
    }

    [Test]
    public void GetDisplayItems_AssistantTextOnly_ReturnsAssistantMessage()
    {
        // Arrange
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateTextContent("Hello back", 0))
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.TypeOf<ClaudeMessage>());
    }

    #endregion

    #region Thinking Block Order Tests

    [Test]
    public void GetDisplayItems_ThinkingThenTools_ThinkingAppearsFirst()
    {
        // Arrange: [Thinking(0), ToolUse(1), ToolUse(2)]
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateThinkingContent("Let me think...", 0),
                CreateToolUseContent("tool1", "Read", "{}", 1),
                CreateToolUseContent("tool2", "Write", "{}", 2))
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert: SyntheticMsg([Thinking]) + ToolGroup([Tool1, Tool2])
        Assert.That(result, Has.Count.EqualTo(2));

        // First item should be the thinking message
        Assert.That(result[0], Is.TypeOf<ClaudeMessage>());
        var thinkingMsg = (ClaudeMessage)result[0];
        Assert.That(thinkingMsg.Content, Has.Count.EqualTo(1));
        Assert.That(thinkingMsg.Content[0].Type, Is.EqualTo(ClaudeContentType.Thinking));

        // Second item should be the tool group
        Assert.That(result[1], Is.TypeOf<ToolExecutionGroup>());
        var toolGroup = (ToolExecutionGroup)result[1];
        Assert.That(toolGroup.Executions, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetDisplayItems_ToolsThenThinking_ThinkingAppearsAfterTools()
    {
        // Arrange: [ToolUse(0), ToolUse(1), Thinking(2)]
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateToolUseContent("tool1", "Read", "{}", 0),
                CreateToolUseContent("tool2", "Write", "{}", 1),
                CreateThinkingContent("Now I understand...", 2))
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert: ToolGroup([Tool1, Tool2]) + SyntheticMsg([Thinking])
        Assert.That(result, Has.Count.EqualTo(2));

        // First item should be the tool group
        Assert.That(result[0], Is.TypeOf<ToolExecutionGroup>());
        var toolGroup = (ToolExecutionGroup)result[0];
        Assert.That(toolGroup.Executions, Has.Count.EqualTo(2));

        // Second item should be the thinking message
        Assert.That(result[1], Is.TypeOf<ClaudeMessage>());
        var thinkingMsg = (ClaudeMessage)result[1];
        Assert.That(thinkingMsg.Content, Has.Count.EqualTo(1));
        Assert.That(thinkingMsg.Content[0].Type, Is.EqualTo(ClaudeContentType.Thinking));
    }

    [Test]
    public void GetDisplayItems_InterspersedContent_PreservesOrder()
    {
        // Arrange: [Thinking(0), ToolUse(1), ToolUse(2), Thinking(3), Text(4)]
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateThinkingContent("First thought", 0),
                CreateToolUseContent("tool1", "Read", "{}", 1),
                CreateToolUseContent("tool2", "Write", "{}", 2),
                CreateThinkingContent("Second thought", 3),
                CreateTextContent("Final answer", 4))
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert: SyntheticMsg([Thinking0]) + ToolGroup([Tool1, Tool2]) + SyntheticMsg([Thinking3, Text4])
        Assert.That(result, Has.Count.EqualTo(3));

        // First: thinking message
        Assert.That(result[0], Is.TypeOf<ClaudeMessage>());
        var msg1 = (ClaudeMessage)result[0];
        Assert.That(msg1.Content, Has.Count.EqualTo(1));
        Assert.That(msg1.Content[0].Type, Is.EqualTo(ClaudeContentType.Thinking));
        Assert.That(msg1.Content[0].Text, Is.EqualTo("First thought"));

        // Second: tool group
        Assert.That(result[1], Is.TypeOf<ToolExecutionGroup>());
        var toolGroup = (ToolExecutionGroup)result[1];
        Assert.That(toolGroup.Executions, Has.Count.EqualTo(2));

        // Third: thinking + text message
        Assert.That(result[2], Is.TypeOf<ClaudeMessage>());
        var msg2 = (ClaudeMessage)result[2];
        Assert.That(msg2.Content, Has.Count.EqualTo(2));
        Assert.That(msg2.Content[0].Type, Is.EqualTo(ClaudeContentType.Thinking));
        Assert.That(msg2.Content[0].Text, Is.EqualTo("Second thought"));
        Assert.That(msg2.Content[1].Type, Is.EqualTo(ClaudeContentType.Text));
        Assert.That(msg2.Content[1].Text, Is.EqualTo("Final answer"));
    }

    [Test]
    public void GetDisplayItems_ThinkingBetweenToolGroups_CreatesSeparateGroups()
    {
        // Arrange: [ToolUse(0), Thinking(1), ToolUse(2)]
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateToolUseContent("tool1", "Read", "{}", 0),
                CreateThinkingContent("Middle thought", 1),
                CreateToolUseContent("tool2", "Write", "{}", 2))
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert: ToolGroup([Tool1]) + SyntheticMsg([Thinking]) + ToolGroup([Tool2])
        Assert.That(result, Has.Count.EqualTo(3));

        // First: tool group with one tool
        Assert.That(result[0], Is.TypeOf<ToolExecutionGroup>());
        var toolGroup1 = (ToolExecutionGroup)result[0];
        Assert.That(toolGroup1.Executions, Has.Count.EqualTo(1));
        Assert.That(toolGroup1.Executions[0].ToolUse.ToolName, Is.EqualTo("Read"));

        // Second: thinking message
        Assert.That(result[1], Is.TypeOf<ClaudeMessage>());
        var thinkingMsg = (ClaudeMessage)result[1];
        Assert.That(thinkingMsg.Content, Has.Count.EqualTo(1));
        Assert.That(thinkingMsg.Content[0].Type, Is.EqualTo(ClaudeContentType.Thinking));

        // Third: tool group with one tool
        Assert.That(result[2], Is.TypeOf<ToolExecutionGroup>());
        var toolGroup2 = (ToolExecutionGroup)result[2];
        Assert.That(toolGroup2.Executions, Has.Count.EqualTo(1));
        Assert.That(toolGroup2.Executions[0].ToolUse.ToolName, Is.EqualTo("Write"));
    }

    #endregion

    #region Legacy/Backward Compatibility Tests

    [Test]
    public void GetDisplayItems_LegacyMessagesWithoutIndex_PreservesInsertionOrder()
    {
        // Arrange: Content blocks with Index = -1 (default/legacy)
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateThinkingContent("First thought", -1),
                CreateToolUseContent("tool1", "Read", "{}", -1),
                CreateThinkingContent("Second thought", -1))
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert: Should process in list order (backward compatible)
        // Thinking -> Tool -> Thinking means: Msg([Thinking1]) + ToolGroup([Tool]) + Msg([Thinking2])
        Assert.That(result, Has.Count.EqualTo(3));

        Assert.That(result[0], Is.TypeOf<ClaudeMessage>());
        Assert.That(result[1], Is.TypeOf<ToolExecutionGroup>());
        Assert.That(result[2], Is.TypeOf<ClaudeMessage>());
    }

    [Test]
    public void GetDisplayItems_MixedIndexAndLegacy_HandlesGracefully()
    {
        // Arrange: Some blocks have index, some don't
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateThinkingContent("Thought", 0),
                CreateToolUseContent("tool1", "Read", "{}", -1), // Legacy
                CreateTextContent("Response", 2))
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert: Should handle mixed gracefully - legacy blocks appear at end of their category
        Assert.That(result, Has.Count.GreaterThanOrEqualTo(2));
    }

    #endregion

    #region Tool Result Matching Tests

    [Test]
    public void GetDisplayItems_ToolResultsMatchByToolUseId()
    {
        // Arrange
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateToolUseContent("tool-id-1", "Read", "{\"path\": \"file.txt\"}", 0)),
            CreateToolResultMessage("tool-id-1", "File contents here", true)
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.TypeOf<ToolExecutionGroup>());
        var group = (ToolExecutionGroup)result[0];
        Assert.That(group.Executions, Has.Count.EqualTo(1));
        Assert.That(group.Executions[0].ToolResult, Is.Not.Null);
        Assert.That(group.Executions[0].ToolResult!.ToolResult, Is.EqualTo("File contents here"));
    }

    [Test]
    public void GetDisplayItems_MultipleToolsWithResults_AllMatched()
    {
        // Arrange
        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(
                CreateToolUseContent("tool-1", "Read", "{}", 0),
                CreateToolUseContent("tool-2", "Write", "{}", 1)),
            CreateToolResultMessage("tool-1", "Result 1", true),
            CreateToolResultMessage("tool-2", "Result 2", true)
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        var group = (ToolExecutionGroup)result[0];
        Assert.That(group.Executions, Has.Count.EqualTo(2));
        Assert.That(group.Executions[0].ToolResult?.ToolResult, Is.EqualTo("Result 1"));
        Assert.That(group.Executions[1].ToolResult?.ToolResult, Is.EqualTo("Result 2"));
    }

    #endregion

    #region Complex Scenarios

    [Test]
    public void GetDisplayItems_FullConversationFlow_PreservesOrder()
    {
        // Arrange: User -> Assistant (thinking + tools) -> Tool results -> Assistant (more thinking + tools + text)
        var messages = new List<ClaudeMessage>
        {
            CreateUserMessage("Please help me"),
            CreateAssistantMessage(
                CreateThinkingContent("Let me analyze this", 0),
                CreateToolUseContent("tool-1", "Read", "{}", 1)),
            CreateToolResultMessage("tool-1", "File contents", true),
            CreateAssistantMessage(
                CreateThinkingContent("I see, now I need to", 0),
                CreateToolUseContent("tool-2", "Write", "{}", 1),
                CreateThinkingContent("Done with that, let me explain", 2),
                CreateTextContent("Here's my answer", 3))
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert: User + Thinking + ToolGroup + Thinking + ToolGroup + Thinking+Text
        Assert.That(result, Has.Count.EqualTo(6));

        // 1. User message
        Assert.That(result[0], Is.TypeOf<ClaudeMessage>());
        Assert.That(((ClaudeMessage)result[0]).Role, Is.EqualTo(ClaudeMessageRole.User));

        // 2. First thinking
        Assert.That(result[1], Is.TypeOf<ClaudeMessage>());
        var thinking1 = (ClaudeMessage)result[1];
        Assert.That(thinking1.Content[0].Text, Is.EqualTo("Let me analyze this"));

        // 3. First tool group
        Assert.That(result[2], Is.TypeOf<ToolExecutionGroup>());

        // 4. Second thinking
        Assert.That(result[3], Is.TypeOf<ClaudeMessage>());
        var thinking2 = (ClaudeMessage)result[3];
        Assert.That(thinking2.Content[0].Text, Is.EqualTo("I see, now I need to"));

        // 5. Second tool group
        Assert.That(result[4], Is.TypeOf<ToolExecutionGroup>());

        // 6. Final thinking + text
        Assert.That(result[5], Is.TypeOf<ClaudeMessage>());
        var finalMsg = (ClaudeMessage)result[5];
        Assert.That(finalMsg.Content, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetDisplayItems_StreamingContent_MaintainsPosition()
    {
        // Arrange: Streaming thinking block (marked as streaming)
        var thinkingContent = CreateThinkingContent("Thinking in progress...", 0);
        thinkingContent.IsStreaming = true;

        var messages = new List<ClaudeMessage>
        {
            CreateAssistantMessage(thinkingContent)
        };

        // Act
        var result = _service.GetDisplayItems(messages);

        // Assert: Streaming content is preserved
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.TypeOf<ClaudeMessage>());
        var msg = (ClaudeMessage)result[0];
        Assert.That(msg.Content[0].IsStreaming, Is.True);
    }

    #endregion

    #region Helper Methods

    private static ClaudeMessage CreateUserMessage(string text)
    {
        return new ClaudeMessage
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = "test-session",
            Role = ClaudeMessageRole.User,
            CreatedAt = DateTime.UtcNow,
            Content = [CreateTextContent(text, 0)]
        };
    }

    private static ClaudeMessage CreateAssistantMessage(params ClaudeMessageContent[] contents)
    {
        var message = new ClaudeMessage
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = "test-session",
            Role = ClaudeMessageRole.Assistant,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var content in contents)
        {
            message.Content.Add(content);
        }
        return message;
    }

    private static ClaudeMessage CreateToolResultMessage(string toolUseId, string result, bool success)
    {
        return new ClaudeMessage
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = "test-session",
            Role = ClaudeMessageRole.User,
            CreatedAt = DateTime.UtcNow,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = toolUseId,
                    ToolResult = result,
                    ToolSuccess = success,
                    Index = 0
                }
            ]
        };
    }

    private static ClaudeMessageContent CreateTextContent(string text, int index)
    {
        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.Text,
            Text = text,
            Index = index
        };
    }

    private static ClaudeMessageContent CreateThinkingContent(string text, int index)
    {
        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.Thinking,
            Text = text,
            Index = index
        };
    }

    private static ClaudeMessageContent CreateToolUseContent(string toolUseId, string toolName, string input, int index)
    {
        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolUseId = toolUseId,
            ToolName = toolName,
            ToolInput = input,
            Index = index
        };
    }

    #endregion
}

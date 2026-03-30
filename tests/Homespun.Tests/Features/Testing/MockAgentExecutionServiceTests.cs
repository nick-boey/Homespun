using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Testing.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.Testing;

/// <summary>
/// Unit tests for MockAgentExecutionService keyword-based response assembly.
/// </summary>
[TestFixture]
public class MockAgentExecutionServiceTests
{
    private MockAgentExecutionService _service = null!;
    private Mock<ILogger<MockAgentExecutionService>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<MockAgentExecutionService>>();
        _service = new MockAgentExecutionService(_loggerMock.Object);
    }

    #region Keyword Parsing Tests

    [Test]
    public void ParseKeywords_WithThinkKeyword_ReturnsThink()
    {
        var keywords = MockAgentExecutionService.ParseKeywords("Please think about this");
        Assert.That(keywords, Does.Contain(MockKeyword.Think));
    }

    [Test]
    public void ParseKeywords_WithToolKeyword_ReturnsTool()
    {
        var keywords = MockAgentExecutionService.ParseKeywords("Use a tool to read the file");
        Assert.That(keywords, Does.Contain(MockKeyword.Tool));
    }

    [Test]
    public void ParseKeywords_WithQuestionKeyword_ReturnsQuestion()
    {
        var keywords = MockAgentExecutionService.ParseKeywords("Ask a question before proceeding");
        Assert.That(keywords, Does.Contain(MockKeyword.Question));
    }

    [Test]
    public void ParseKeywords_WithPlanKeyword_ReturnsPlan()
    {
        var keywords = MockAgentExecutionService.ParseKeywords("Create a plan for this task");
        Assert.That(keywords, Does.Contain(MockKeyword.Plan));
    }

    [Test]
    public void ParseKeywords_WithMultipleKeywords_ReturnsAll()
    {
        var keywords = MockAgentExecutionService.ParseKeywords(
            "Use a tool and then think about the response. Ask a question and create a plan");

        Assert.That(keywords, Has.Count.EqualTo(4));
        Assert.That(keywords, Does.Contain(MockKeyword.Think));
        Assert.That(keywords, Does.Contain(MockKeyword.Tool));
        Assert.That(keywords, Does.Contain(MockKeyword.Question));
        Assert.That(keywords, Does.Contain(MockKeyword.Plan));
    }

    [Test]
    public void ParseKeywords_CaseInsensitive_Works()
    {
        var keywords = MockAgentExecutionService.ParseKeywords("THINK TOOL QUESTION PLAN");

        Assert.That(keywords, Has.Count.EqualTo(4));
        Assert.That(keywords, Does.Contain(MockKeyword.Think));
        Assert.That(keywords, Does.Contain(MockKeyword.Tool));
        Assert.That(keywords, Does.Contain(MockKeyword.Question));
        Assert.That(keywords, Does.Contain(MockKeyword.Plan));
    }

    [Test]
    public void ParseKeywords_NoKeywords_ReturnsEmpty()
    {
        var keywords = MockAgentExecutionService.ParseKeywords("Hello, how are you?");
        Assert.That(keywords, Is.Empty);
    }

    [Test]
    public void ParseKeywords_EmptyString_ReturnsEmpty()
    {
        var keywords = MockAgentExecutionService.ParseKeywords("");
        Assert.That(keywords, Is.Empty);
    }

    #endregion

    #region SendMessageAsync Response Sequence Tests

    [Test]
    public async Task SendMessageAsync_WithThinkKeyword_YieldsThinkingBlockAndText()
    {
        // Arrange - start a session first
        var sessionId = await StartSessionAndGetId();
        var request = new AgentMessageRequest(sessionId, "Please think about this");

        // Act
        var messages = await CollectMessages(request);

        // Assert
        var assistantMessages = messages.OfType<SdkAssistantMessage>().ToList();
        Assert.That(assistantMessages, Has.Count.GreaterThanOrEqualTo(2));

        // First assistant message should have thinking block
        var thinkingMessage = assistantMessages.First();
        Assert.That(thinkingMessage.Message.Content, Has.Some.TypeOf<SdkThinkingBlock>());

        // Second assistant message should have text block
        var textMessage = assistantMessages.Skip(1).First();
        Assert.That(textMessage.Message.Content, Has.Some.TypeOf<SdkTextBlock>());

        // Should end with result
        Assert.That(messages.Last(), Is.TypeOf<SdkResultMessage>());
    }

    [Test]
    public async Task SendMessageAsync_WithToolKeyword_YieldsToolUseAndResults()
    {
        // Arrange
        var sessionId = await StartSessionAndGetId();
        var request = new AgentMessageRequest(sessionId, "Use a tool to read the file");

        // Act
        var messages = await CollectMessages(request);

        // Assert
        var assistantMessages = messages.OfType<SdkAssistantMessage>().ToList();
        var userMessages = messages.OfType<SdkUserMessage>().ToList();

        // Should have tool use blocks
        var toolUseBlocks = assistantMessages
            .SelectMany(m => m.Message.Content)
            .OfType<SdkToolUseBlock>()
            .ToList();
        Assert.That(toolUseBlocks, Has.Count.GreaterThanOrEqualTo(2)); // Read + Write

        // Should have tool result blocks
        var toolResultBlocks = userMessages
            .SelectMany(m => m.Message.Content)
            .OfType<SdkToolResultBlock>()
            .ToList();
        Assert.That(toolResultBlocks, Has.Count.GreaterThanOrEqualTo(2));

        // Should have Read and Write tools
        Assert.That(toolUseBlocks.Select(t => t.Name), Does.Contain("Read"));
        Assert.That(toolUseBlocks.Select(t => t.Name), Does.Contain("Write"));

        // Should end with result
        Assert.That(messages.Last(), Is.TypeOf<SdkResultMessage>());
    }

    [Test]
    public async Task SendMessageAsync_WithQuestionKeyword_YieldsQuestionPendingMessage()
    {
        // Arrange
        var sessionId = await StartSessionAndGetId();
        var request = new AgentMessageRequest(sessionId, "Ask a question before proceeding");

        // Act
        var messages = await CollectMessagesUntilPending(request);

        // Assert - should have question pending message
        var questionPending = messages.OfType<SdkQuestionPendingMessage>().FirstOrDefault();
        Assert.That(questionPending, Is.Not.Null);

        // Should have questions JSON
        Assert.That(questionPending!.QuestionsJson, Does.Contain("questions"));

        // Parse and verify structure
        var questionsDoc = JsonDocument.Parse(questionPending.QuestionsJson);
        var questionsArray = questionsDoc.RootElement.GetProperty("questions");
        Assert.That(questionsArray.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task SendMessageAsync_WithPlanKeyword_YieldsPlanPendingMessage()
    {
        // Arrange
        var sessionId = await StartSessionAndGetId();
        var request = new AgentMessageRequest(sessionId, "Create a plan for this task");

        // Act
        var messages = await CollectMessagesUntilPending(request);

        // Assert - should have plan pending message
        var planPending = messages.OfType<SdkPlanPendingMessage>().FirstOrDefault();
        Assert.That(planPending, Is.Not.Null);

        // Should have plan JSON
        Assert.That(planPending!.PlanJson, Does.Contain("plan"));
    }

    [Test]
    public async Task SendMessageAsync_WithMultipleKeywords_YieldsCorrectSequence()
    {
        // Arrange - think + tool but no question/plan so it completes
        var sessionId = await StartSessionAndGetId();
        var request = new AgentMessageRequest(sessionId, "Use a tool and then think about it");

        // Act
        var messages = await CollectMessages(request);

        // Assert - order should be: think → tool (per spec)
        var assistantMessages = messages.OfType<SdkAssistantMessage>().ToList();

        // Find first thinking block and first tool use block
        int? thinkIndex = null;
        int? toolIndex = null;

        for (int i = 0; i < assistantMessages.Count; i++)
        {
            if (thinkIndex == null && assistantMessages[i].Message.Content.Any(c => c is SdkThinkingBlock))
                thinkIndex = i;
            if (toolIndex == null && assistantMessages[i].Message.Content.Any(c => c is SdkToolUseBlock))
                toolIndex = i;
        }

        Assert.That(thinkIndex, Is.Not.Null, "Should have thinking block");
        Assert.That(toolIndex, Is.Not.Null, "Should have tool use block");
        Assert.That(thinkIndex, Is.LessThan(toolIndex), "Thinking should come before tool use");
    }

    [Test]
    public async Task SendMessageAsync_WithQuestionAndPlan_DoesNotDuplicatePlan()
    {
        // Arrange
        var sessionId = await StartSessionAndGetId();
        var request = new AgentMessageRequest(sessionId, "Ask a question and create a plan");

        // Act - collect messages until question pending
        var messages = await CollectMessagesUntilPending(request);

        // Answer the question
        var answers = new Dictionary<string, string>
        {
            { "question_0", "Red" },
            { "question_1", "Feature1,Feature2" }
        };
        await _service.AnswerQuestionAsync(sessionId, answers);

        // Collect remaining messages until plan pending
        var remainingMessages = await CollectMessagesUntilPending(
            new AgentMessageRequest(sessionId, ""), continueFromChannel: true, sessionId: sessionId);

        // Assert - should have exactly one plan pending message total
        var allMessages = messages.Concat(remainingMessages).ToList();
        var planPendingMessages = allMessages.OfType<SdkPlanPendingMessage>().ToList();
        Assert.That(planPendingMessages, Has.Count.EqualTo(1), "Should have exactly one plan pending message");
    }

    #endregion

    #region AnswerQuestionAsync Tests

    [Test]
    public async Task AnswerQuestionAsync_WithValidSession_ClearsPendingQuestion()
    {
        // Arrange
        var sessionId = await StartSessionAndGetId();
        var request = new AgentMessageRequest(sessionId, "Ask a question");

        // Trigger question
        var messages = await CollectMessagesUntilPending(request);
        Assert.That(messages.OfType<SdkQuestionPendingMessage>().Any(), Is.True);

        // Act
        var result = await _service.AnswerQuestionAsync(sessionId, new Dictionary<string, string>
        {
            { "question_0", "Red" }
        });

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task AnswerQuestionAsync_NonExistentSession_ReturnsFalse()
    {
        // Act
        var result = await _service.AnswerQuestionAsync("non-existent-session",
            new Dictionary<string, string> { { "q", "a" } });

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task AnswerQuestionAsync_NoQuestionPending_ReturnsFalse()
    {
        // Arrange - start session but don't trigger question
        var sessionId = await StartSessionAndGetId();

        // Act
        var result = await _service.AnswerQuestionAsync(sessionId,
            new Dictionary<string, string> { { "q", "a" } });

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region ApprovePlanAsync Tests

    [Test]
    public async Task ApprovePlanAsync_WithValidSession_ClearsPendingPlan()
    {
        // Arrange
        var sessionId = await StartSessionAndGetId();
        var request = new AgentMessageRequest(sessionId, "Create a plan");

        // Trigger plan
        var messages = await CollectMessagesUntilPending(request);
        Assert.That(messages.OfType<SdkPlanPendingMessage>().Any(), Is.True);

        // Act
        var result = await _service.ApprovePlanAsync(sessionId, approved: true, keepContext: true);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ApprovePlanAsync_NonExistentSession_ReturnsFalse()
    {
        // Act
        var result = await _service.ApprovePlanAsync("non-existent-session", true, true);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ApprovePlanAsync_NoPlanPending_ReturnsFalse()
    {
        // Arrange - start session but don't trigger plan
        var sessionId = await StartSessionAndGetId();

        // Act
        var result = await _service.ApprovePlanAsync(sessionId, true, true);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region StartSessionAsync Keyword-Based Response Tests

    [Test]
    public async Task StartSessionAsync_WithThinkKeyword_YieldsThinkingBlockAndText()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Please think about this problem");

        // Act
        var messages = await CollectStartMessages(request);

        // Assert
        var assistantMessages = messages.OfType<SdkAssistantMessage>().ToList();
        Assert.That(assistantMessages, Has.Count.GreaterThanOrEqualTo(2));

        // Should have thinking block
        var thinkingMessage = assistantMessages.First();
        Assert.That(thinkingMessage.Message.Content, Has.Some.TypeOf<SdkThinkingBlock>());

        // Should have text block after thinking
        var textMessage = assistantMessages.Skip(1).First();
        Assert.That(textMessage.Message.Content, Has.Some.TypeOf<SdkTextBlock>());

        // Should end with result
        Assert.That(messages.Last(), Is.TypeOf<SdkResultMessage>());
    }

    [Test]
    public async Task StartSessionAsync_WithToolKeyword_YieldsToolUseAndResults()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Use a tool to read the file");

        // Act
        var messages = await CollectStartMessages(request);

        // Assert
        var assistantMessages = messages.OfType<SdkAssistantMessage>().ToList();
        var userMessages = messages.OfType<SdkUserMessage>().ToList();

        var toolUseBlocks = assistantMessages
            .SelectMany(m => m.Message.Content)
            .OfType<SdkToolUseBlock>()
            .ToList();
        Assert.That(toolUseBlocks, Has.Count.GreaterThanOrEqualTo(2)); // Read + Write

        var toolResultBlocks = userMessages
            .SelectMany(m => m.Message.Content)
            .OfType<SdkToolResultBlock>()
            .ToList();
        Assert.That(toolResultBlocks, Has.Count.GreaterThanOrEqualTo(2));

        Assert.That(messages.Last(), Is.TypeOf<SdkResultMessage>());
    }

    [Test]
    public async Task StartSessionAsync_WithQuestionKeyword_YieldsQuestionPendingMessage()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Ask a question before proceeding");

        // Act
        var messages = await CollectStartMessagesUntilPending(request);

        // Assert
        var questionPending = messages.OfType<SdkQuestionPendingMessage>().FirstOrDefault();
        Assert.That(questionPending, Is.Not.Null);
        Assert.That(questionPending!.QuestionsJson, Does.Contain("questions"));
    }

    [Test]
    public async Task StartSessionAsync_WithPlanKeyword_YieldsPlanPendingMessage()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Create a plan for this task");

        // Act
        var messages = await CollectStartMessagesUntilPending(request);

        // Assert
        var planPending = messages.OfType<SdkPlanPendingMessage>().FirstOrDefault();
        Assert.That(planPending, Is.Not.Null);
        Assert.That(planPending!.PlanJson, Does.Contain("plan"));
    }

    [Test]
    public async Task StartSessionAsync_WithNoKeywords_YieldsDefaultResponse()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Hello, how are you?");

        // Act
        var messages = await CollectStartMessages(request);

        // Assert - should still produce session_started, assistant message, and result
        Assert.That(messages.OfType<SdkSystemMessage>().Any(m => m.Subtype == "session_started"), Is.True);
        Assert.That(messages.OfType<SdkAssistantMessage>().Any(), Is.True);
        Assert.That(messages.Last(), Is.TypeOf<SdkResultMessage>());
    }

    [Test]
    public async Task StartSessionAsync_WithMultipleKeywords_YieldsCorrectOrder()
    {
        // Arrange - think + tool keywords
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Use a tool and then think about the response");

        // Act
        var messages = await CollectStartMessages(request);

        // Assert - think should come before tool
        var assistantMessages = messages.OfType<SdkAssistantMessage>().ToList();

        int? thinkIndex = null;
        int? toolIndex = null;
        for (int i = 0; i < assistantMessages.Count; i++)
        {
            if (thinkIndex == null && assistantMessages[i].Message.Content.Any(c => c is SdkThinkingBlock))
                thinkIndex = i;
            if (toolIndex == null && assistantMessages[i].Message.Content.Any(c => c is SdkToolUseBlock))
                toolIndex = i;
        }

        Assert.That(thinkIndex, Is.Not.Null, "Should have thinking block");
        Assert.That(toolIndex, Is.Not.Null, "Should have tool use block");
        Assert.That(thinkIndex, Is.LessThan(toolIndex), "Thinking should come before tool use");
    }

    [Test]
    public async Task StartSessionAsync_AlwaysYieldsSessionStartedFirst()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Please think about this and use a tool");

        // Act
        var messages = await CollectStartMessages(request);

        // Assert - first message should always be session_started
        Assert.That(messages.First(), Is.TypeOf<SdkSystemMessage>());
        var sysMsg = (SdkSystemMessage)messages.First();
        Assert.That(sysMsg.Subtype, Is.EqualTo("session_started"));
    }

    #endregion

    #region GetSessionStatusAsync Tests

    [Test]
    public async Task GetSessionStatusAsync_WithActiveSession_ReturnsStatus()
    {
        // Arrange
        var sessionId = await StartSessionAndGetId();

        // Act
        var status = await _service.GetSessionStatusAsync(sessionId);

        // Assert
        Assert.That(status, Is.Not.Null);
        Assert.That(status!.SessionId, Is.EqualTo(sessionId));
    }

    [Test]
    public async Task GetSessionStatusAsync_NonExistentSession_ReturnsNull()
    {
        // Act
        var status = await _service.GetSessionStatusAsync("non-existent");

        // Assert
        Assert.That(status, Is.Null);
    }

    #endregion

    #region Helper Methods

    private async Task<string> StartSessionAndGetId()
    {
        var request = new AgentStartRequest(
            WorkingDirectory: "/test/path",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "Initial prompt");

        string? sessionId = null;
        await foreach (var msg in _service.StartSessionAsync(request))
        {
            if (msg is SdkSystemMessage systemMsg && systemMsg.Subtype == "session_started")
            {
                sessionId = systemMsg.SessionId;
            }
        }

        Assert.That(sessionId, Is.Not.Null, "Should have received session ID");
        return sessionId!;
    }

    private async Task<List<SdkMessage>> CollectMessages(AgentMessageRequest request)
    {
        var messages = new List<SdkMessage>();
        await foreach (var msg in _service.SendMessageAsync(request))
        {
            messages.Add(msg);
        }
        return messages;
    }

    private async Task<List<SdkMessage>> CollectMessagesUntilPending(
        AgentMessageRequest request,
        bool continueFromChannel = false,
        string? sessionId = null)
    {
        var messages = new List<SdkMessage>();

        if (continueFromChannel && sessionId != null)
        {
            // Continue reading from existing channel after answer/approve
            await foreach (var msg in _service.ContinueReadingMessages(sessionId))
            {
                messages.Add(msg);
                if (msg is SdkQuestionPendingMessage or SdkPlanPendingMessage)
                    break;
            }
        }
        else
        {
            await foreach (var msg in _service.SendMessageAsync(request))
            {
                messages.Add(msg);
                if (msg is SdkQuestionPendingMessage or SdkPlanPendingMessage)
                    break;
            }
        }

        return messages;
    }

    private async Task<List<SdkMessage>> CollectStartMessages(AgentStartRequest request)
    {
        var messages = new List<SdkMessage>();
        await foreach (var msg in _service.StartSessionAsync(request))
        {
            messages.Add(msg);
        }
        return messages;
    }

    private async Task<List<SdkMessage>> CollectStartMessagesUntilPending(AgentStartRequest request)
    {
        var messages = new List<SdkMessage>();
        await foreach (var msg in _service.StartSessionAsync(request))
        {
            messages.Add(msg);
            if (msg is SdkQuestionPendingMessage or SdkPlanPendingMessage)
                break;
        }
        return messages;
    }

    #endregion
}

/// <summary>
/// Tests for question JSON structure validation.
/// </summary>
[TestFixture]
public class MockQuestionStructureTests
{
    private MockAgentExecutionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var loggerMock = new Mock<ILogger<MockAgentExecutionService>>();
        _service = new MockAgentExecutionService(loggerMock.Object);
    }

    [Test]
    public async Task QuestionPending_HasCorrectJsonStructure()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "test");

        string? sessionId = null;
        await foreach (var msg in _service.StartSessionAsync(request))
        {
            if (msg is SdkSystemMessage sys) sessionId = sys.SessionId;
        }

        // Act
        SdkQuestionPendingMessage? questionMsg = null;
        await foreach (var msg in _service.SendMessageAsync(new AgentMessageRequest(sessionId!, "Ask a question")))
        {
            if (msg is SdkQuestionPendingMessage qm)
            {
                questionMsg = qm;
                break;
            }
        }

        // Assert
        Assert.That(questionMsg, Is.Not.Null);

        var doc = JsonDocument.Parse(questionMsg!.QuestionsJson);
        var questions = doc.RootElement.GetProperty("questions");

        foreach (var q in questions.EnumerateArray())
        {
            // Each question should have required fields
            Assert.That(q.TryGetProperty("question", out _), Is.True, "Should have 'question' field");
            Assert.That(q.TryGetProperty("header", out _), Is.True, "Should have 'header' field");
            Assert.That(q.TryGetProperty("options", out _), Is.True, "Should have 'options' field");
            Assert.That(q.TryGetProperty("multiSelect", out _), Is.True, "Should have 'multiSelect' field");

            // Options should have label and description
            var options = q.GetProperty("options");
            foreach (var opt in options.EnumerateArray())
            {
                Assert.That(opt.TryGetProperty("label", out _), Is.True, "Option should have 'label'");
                Assert.That(opt.TryGetProperty("description", out _), Is.True, "Option should have 'description'");
            }
        }
    }

    [Test]
    public async Task QuestionPending_HasSingleSelectAndMultiSelectQuestions()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "test");

        string? sessionId = null;
        await foreach (var msg in _service.StartSessionAsync(request))
        {
            if (msg is SdkSystemMessage sys) sessionId = sys.SessionId;
        }

        // Act
        SdkQuestionPendingMessage? questionMsg = null;
        await foreach (var msg in _service.SendMessageAsync(new AgentMessageRequest(sessionId!, "Ask a question")))
        {
            if (msg is SdkQuestionPendingMessage qm)
            {
                questionMsg = qm;
                break;
            }
        }

        // Assert
        Assert.That(questionMsg, Is.Not.Null);

        var doc = JsonDocument.Parse(questionMsg!.QuestionsJson);
        var questions = doc.RootElement.GetProperty("questions");
        var questionList = questions.EnumerateArray().ToList();

        Assert.That(questionList, Has.Count.GreaterThanOrEqualTo(2));

        // Should have at least one single-select and one multi-select
        var hasSingleSelect = questionList.Any(q => !q.GetProperty("multiSelect").GetBoolean());
        var hasMultiSelect = questionList.Any(q => q.GetProperty("multiSelect").GetBoolean());

        Assert.That(hasSingleSelect, Is.True, "Should have single-select question");
        Assert.That(hasMultiSelect, Is.True, "Should have multi-select question");
    }
}

/// <summary>
/// Tests for plan JSON structure validation.
/// </summary>
[TestFixture]
public class MockPlanStructureTests
{
    private MockAgentExecutionService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var loggerMock = new Mock<ILogger<MockAgentExecutionService>>();
        _service = new MockAgentExecutionService(loggerMock.Object);
    }

    [Test]
    public async Task PlanPending_HasCorrectJsonStructure()
    {
        // Arrange
        var request = new AgentStartRequest(
            WorkingDirectory: "/test",
            Mode: SessionMode.Build,
            Model: "sonnet",
            Prompt: "test");

        string? sessionId = null;
        await foreach (var msg in _service.StartSessionAsync(request))
        {
            if (msg is SdkSystemMessage sys) sessionId = sys.SessionId;
        }

        // Act
        SdkPlanPendingMessage? planMsg = null;
        await foreach (var msg in _service.SendMessageAsync(new AgentMessageRequest(sessionId!, "Create a plan")))
        {
            if (msg is SdkPlanPendingMessage pm)
            {
                planMsg = pm;
                break;
            }
        }

        // Assert
        Assert.That(planMsg, Is.Not.Null);

        var doc = JsonDocument.Parse(planMsg!.PlanJson);
        Assert.That(doc.RootElement.TryGetProperty("plan", out var plan), Is.True);
        Assert.That(plan.GetString(), Does.Contain("##")); // Should have markdown headers
    }
}

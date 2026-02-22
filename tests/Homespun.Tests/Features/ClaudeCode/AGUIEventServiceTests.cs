using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Tests for AGUIEventService which translates A2A protocol events to AG-UI events.
/// </summary>
[TestFixture]
public class AGUIEventServiceTests
{
    private AGUIEventService _service = null!;
    private Mock<ILogger<AGUIEventService>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerMock = new Mock<ILogger<AGUIEventService>>();
        _service = new AGUIEventService(_loggerMock.Object);
    }

    #region TranslateStatusUpdate Tests

    [Test]
    public void TranslateStatusUpdate_WorkingState_ReturnsRunStartedEvent()
    {
        // Arrange
        var statusUpdate = CreateStatusUpdate(TaskState.Working);
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunStartedEvent>());
        var runStarted = (RunStartedEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(runStarted.ThreadId, Is.EqualTo(sessionId));
            Assert.That(runStarted.RunId, Is.EqualTo(runId));
            Assert.That(runStarted.Type, Is.EqualTo("RUN_STARTED"));
        });
    }

    [Test]
    public void TranslateStatusUpdate_CompletedState_ReturnsRunFinishedEvent()
    {
        // Arrange
        var statusUpdate = CreateStatusUpdate(TaskState.Completed);
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunFinishedEvent>());
        var runFinished = (RunFinishedEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(runFinished.ThreadId, Is.EqualTo(sessionId));
            Assert.That(runFinished.RunId, Is.EqualTo(runId));
            Assert.That(runFinished.Type, Is.EqualTo("RUN_FINISHED"));
        });
    }

    [Test]
    public void TranslateStatusUpdate_CompletedState_WithMessage_IncludesResultText()
    {
        // Arrange
        var statusUpdate = CreateStatusUpdate(TaskState.Completed, "Task completed successfully");
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunFinishedEvent>());
        var runFinished = (RunFinishedEvent)events[0];
        Assert.That(runFinished.Result, Is.EqualTo("Task completed successfully"));
    }

    [Test]
    public void TranslateStatusUpdate_FailedState_ReturnsRunErrorEvent()
    {
        // Arrange
        var statusUpdate = CreateStatusUpdate(TaskState.Failed, "Something went wrong");
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunErrorEvent>());
        var runError = (RunErrorEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(runError.Message, Is.EqualTo("Something went wrong"));
            Assert.That(runError.Type, Is.EqualTo("RUN_ERROR"));
        });
    }

    [Test]
    public void TranslateStatusUpdate_FailedState_WithoutMessage_ReturnsGenericError()
    {
        // Arrange
        var statusUpdate = CreateStatusUpdate(TaskState.Failed);
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunErrorEvent>());
        var runError = (RunErrorEvent)events[0];
        Assert.That(runError.Message, Is.EqualTo("Task failed"));
    }

    [Test]
    public void TranslateStatusUpdate_CanceledState_ReturnsRunErrorWithCanceledCode()
    {
        // Arrange
        var statusUpdate = CreateStatusUpdate(TaskState.Canceled);
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunErrorEvent>());
        var runError = (RunErrorEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(runError.Message, Is.EqualTo("Task was canceled"));
            Assert.That(runError.Code, Is.EqualTo("canceled"));
        });
    }

    [Test]
    public void TranslateStatusUpdate_InputRequired_Question_ReturnsQuestionPendingEvent()
    {
        // Arrange
        var questionsJson = """
        [
            {
                "question": "What framework?",
                "header": "Framework",
                "options": [
                    { "label": "React", "description": "React framework" },
                    { "label": "Vue", "description": "Vue framework" }
                ],
                "multiSelect": false
            }
        ]
        """;

        var metadata = new Dictionary<string, JsonElement>
        {
            ["inputType"] = JsonSerializer.Deserialize<JsonElement>("\"question\""),
            ["questions"] = JsonSerializer.Deserialize<JsonElement>(questionsJson)
        };

        var statusUpdate = CreateStatusUpdate(TaskState.InputRequired, metadata: metadata);
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<CustomEvent>());
        var customEvent = (CustomEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(customEvent.Name, Is.EqualTo(AGUICustomEventName.QuestionPending));
            Assert.That(customEvent.Type, Is.EqualTo("CUSTOM"));
        });
    }

    [Test]
    public void TranslateStatusUpdate_InputRequired_PlanApproval_ReturnsPlanPendingEvent()
    {
        // Arrange
        var metadata = new Dictionary<string, JsonElement>
        {
            ["inputType"] = JsonSerializer.Deserialize<JsonElement>("\"plan-approval\""),
            ["plan"] = JsonSerializer.Deserialize<JsonElement>("\"## Implementation Plan\\n\\n1. Step one\\n2. Step two\""),
            ["planFilePath"] = JsonSerializer.Deserialize<JsonElement>("\"/path/to/plan.md\"")
        };

        var statusUpdate = CreateStatusUpdate(TaskState.InputRequired, metadata: metadata);
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<CustomEvent>());
        var customEvent = (CustomEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(customEvent.Name, Is.EqualTo(AGUICustomEventName.PlanPending));
            Assert.That(customEvent.Value, Is.InstanceOf<AGUIPlanPendingData>());
        });

        var planData = (AGUIPlanPendingData)customEvent.Value;
        Assert.Multiple(() =>
        {
            Assert.That(planData.PlanContent, Does.Contain("Implementation Plan"));
            Assert.That(planData.PlanFilePath, Is.EqualTo("/path/to/plan.md"));
        });
    }

    [Test]
    public void TranslateStatusUpdate_SubmittedState_ReturnsNoEvents()
    {
        // Arrange
        var statusUpdate = CreateStatusUpdate(TaskState.Submitted);
        var sessionId = "session-123";
        var runId = "run-456";

        // Act
        var events = _service.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert - Submitted is not a state we translate to events
        Assert.That(events, Is.Empty);
    }

    #endregion

    #region TranslateMessage Tests

    [Test]
    public void TranslateMessage_AgentRole_ReturnsTextMessageStartContentEnd()
    {
        // Arrange
        var message = CreateAgentMessage("Hello, world!");
        var sessionId = "session-123";

        // Act
        var events = _service.TranslateMessage(message, sessionId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(events[0], Is.InstanceOf<TextMessageStartEvent>());
            Assert.That(events[1], Is.InstanceOf<TextMessageContentEvent>());
            Assert.That(events[2], Is.InstanceOf<TextMessageEndEvent>());
        });

        var startEvent = (TextMessageStartEvent)events[0];
        var contentEvent = (TextMessageContentEvent)events[1];
        var endEvent = (TextMessageEndEvent)events[2];

        Assert.Multiple(() =>
        {
            Assert.That(startEvent.Role, Is.EqualTo("assistant"));
            Assert.That(contentEvent.Delta, Is.EqualTo("Hello, world!"));
            Assert.That(startEvent.MessageId, Is.EqualTo(contentEvent.MessageId));
            Assert.That(startEvent.MessageId, Is.EqualTo(endEvent.MessageId));
        });
    }

    [Test]
    public void TranslateMessage_AgentRole_WithMultipleTextParts_EmitsMultipleContent()
    {
        // Arrange
        var message = new AgentMessage
        {
            MessageId = "msg-123",
            Role = MessageRole.Agent,
            Parts = new List<Part>
            {
                new TextPart { Text = "First part. " },
                new TextPart { Text = "Second part." }
            },
            ContextId = "context-456"
        };
        var sessionId = "session-123";

        // Act
        var events = _service.TranslateMessage(message, sessionId).ToList();

        // Assert - Start, Content, Content, End
        Assert.That(events, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(events[0], Is.InstanceOf<TextMessageStartEvent>());
            Assert.That(events[1], Is.InstanceOf<TextMessageContentEvent>());
            Assert.That(events[2], Is.InstanceOf<TextMessageContentEvent>());
            Assert.That(events[3], Is.InstanceOf<TextMessageEndEvent>());
        });

        var content1 = (TextMessageContentEvent)events[1];
        var content2 = (TextMessageContentEvent)events[2];
        Assert.Multiple(() =>
        {
            Assert.That(content1.Delta, Is.EqualTo("First part. "));
            Assert.That(content2.Delta, Is.EqualTo("Second part."));
        });
    }

    [Test]
    public void TranslateMessage_AgentRole_WithToolUse_ReturnsToolCallEvents()
    {
        // Arrange
        var toolUseData = new Dictionary<string, object>
        {
            ["toolName"] = "Read",
            ["toolUseId"] = "tool-123",
            ["input"] = new Dictionary<string, object> { ["file_path"] = "/test.txt" }
        };
        var toolUseDataPart = CreateDataPart("tool_use", toolUseData);

        var message = new AgentMessage
        {
            MessageId = "msg-123",
            Role = MessageRole.Agent,
            Parts = new List<Part> { toolUseDataPart },
            ContextId = "context-456"
        };
        var sessionId = "session-123";

        // Act
        var events = _service.TranslateMessage(message, sessionId).ToList();

        // Assert - Start, ToolCallStart, ToolCallArgs, ToolCallEnd, End
        Assert.That(events.Count, Is.GreaterThanOrEqualTo(4));

        var toolCallStart = events.OfType<ToolCallStartEvent>().FirstOrDefault();
        var toolCallEnd = events.OfType<ToolCallEndEvent>().FirstOrDefault();

        Assert.That(toolCallStart, Is.Not.Null);
        Assert.That(toolCallEnd, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(toolCallStart!.ToolCallName, Is.EqualTo("Read"));
            Assert.That(toolCallStart.ToolCallId, Is.EqualTo("tool-123"));
        });
    }

    [Test]
    public void TranslateMessage_AgentRole_WithThinking_EmitsThinkingAsText()
    {
        // Arrange
        var thinkingData = new Dictionary<string, object>
        {
            ["thinking"] = "Let me analyze this..."
        };
        var thinkingDataPart = CreateDataPart("thinking", thinkingData);

        var message = new AgentMessage
        {
            MessageId = "msg-123",
            Role = MessageRole.Agent,
            Parts = new List<Part> { thinkingDataPart },
            ContextId = "context-456"
        };
        var sessionId = "session-123";

        // Act
        var events = _service.TranslateMessage(message, sessionId).ToList();

        // Assert - Should emit thinking content as text
        var contentEvents = events.OfType<TextMessageContentEvent>().ToList();
        Assert.That(contentEvents, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(contentEvents.Any(e => e.Delta.Contains("analyze")), Is.True);
    }

    [Test]
    public void TranslateMessage_UserRole_WithToolResult_ReturnsToolResultEvent()
    {
        // Arrange
        var toolResultData = new Dictionary<string, object>
        {
            ["toolUseId"] = "tool-123",
            ["content"] = "File contents here"
        };
        var toolResultDataPart = CreateDataPart("tool_result", toolResultData);

        var message = new AgentMessage
        {
            MessageId = "msg-123",
            Role = MessageRole.User,
            Parts = new List<Part> { toolResultDataPart },
            ContextId = "context-456"
        };
        var sessionId = "session-123";

        // Act
        var events = _service.TranslateMessage(message, sessionId).ToList();

        // Assert
        var toolResultEvents = events.OfType<ToolCallResultEvent>().ToList();
        Assert.That(toolResultEvents, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(toolResultEvents[0].ToolCallId, Is.EqualTo("tool-123"));
            Assert.That(toolResultEvents[0].Content, Does.Contain("File contents"));
        });
    }

    [Test]
    public void TranslateMessage_EmptyParts_ReturnsStartAndEndOnly()
    {
        // Arrange
        var message = new AgentMessage
        {
            MessageId = "msg-123",
            Role = MessageRole.Agent,
            Parts = new List<Part>(),
            ContextId = "context-456"
        };
        var sessionId = "session-123";

        // Act
        var events = _service.TranslateMessage(message, sessionId).ToList();

        // Assert - Start and End events only
        Assert.That(events, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(events[0], Is.InstanceOf<TextMessageStartEvent>());
            Assert.That(events[1], Is.InstanceOf<TextMessageEndEvent>());
        });
    }

    #endregion

    #region TranslateTask Tests

    [Test]
    public void TranslateTask_ReturnsRunStartedEvent()
    {
        // Arrange
        var task = new AgentTask
        {
            Id = "task-123",
            ContextId = "context-456",
            Status = new AgentTaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = DateTimeOffset.UtcNow
            }
        };
        var sessionId = "session-123";

        // Act
        var events = _service.TranslateTask(task, sessionId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunStartedEvent>());
        var runStarted = (RunStartedEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(runStarted.ThreadId, Is.EqualTo(sessionId));
            Assert.That(runStarted.RunId, Is.EqualTo("task-123"));
        });
    }

    #endregion

    #region Factory Method Tests

    [Test]
    public void CreateRunStarted_ReturnsCorrectEvent()
    {
        // Act
        var evt = _service.CreateRunStarted("session-123", "run-456");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.ThreadId, Is.EqualTo("session-123"));
            Assert.That(evt.RunId, Is.EqualTo("run-456"));
            Assert.That(evt.Type, Is.EqualTo("RUN_STARTED"));
        });
    }

    [Test]
    public void CreateRunFinished_ReturnsCorrectEvent()
    {
        // Act
        var evt = _service.CreateRunFinished("session-123", "run-456", "Success!");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.ThreadId, Is.EqualTo("session-123"));
            Assert.That(evt.RunId, Is.EqualTo("run-456"));
            Assert.That(evt.Result, Is.EqualTo("Success!"));
            Assert.That(evt.Type, Is.EqualTo("RUN_FINISHED"));
        });
    }

    [Test]
    public void CreateRunError_ReturnsCorrectEvent()
    {
        // Act
        var evt = _service.CreateRunError("An error occurred", "error_code");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.Message, Is.EqualTo("An error occurred"));
            Assert.That(evt.Code, Is.EqualTo("error_code"));
            Assert.That(evt.Type, Is.EqualTo("RUN_ERROR"));
        });
    }

    [Test]
    public void CreateQuestionPending_ReturnsCustomEventWithData()
    {
        // Arrange
        var question = new PendingQuestion
        {
            Id = "q-123",
            ToolUseId = "tool-123",
            Questions = new List<UserQuestion>
            {
                new()
                {
                    Question = "What framework?",
                    Header = "Framework",
                    Options = new List<QuestionOption>
                    {
                        new() { Label = "React", Description = "React framework" }
                    },
                    MultiSelect = false
                }
            }
        };

        // Act
        var evt = _service.CreateQuestionPending(question);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.Name, Is.EqualTo(AGUICustomEventName.QuestionPending));
            Assert.That(evt.Value, Is.EqualTo(question));
            Assert.That(evt.Type, Is.EqualTo("CUSTOM"));
        });
    }

    [Test]
    public void CreatePlanPending_ReturnsCustomEventWithData()
    {
        // Arrange
        var planContent = "## My Plan\n\n1. Step one\n2. Step two";
        var planFilePath = "/path/to/plan.md";

        // Act
        var evt = _service.CreatePlanPending(planContent, planFilePath);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(evt.Name, Is.EqualTo(AGUICustomEventName.PlanPending));
            Assert.That(evt.Type, Is.EqualTo("CUSTOM"));
        });

        var planData = evt.Value as AGUIPlanPendingData;
        Assert.That(planData, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(planData!.PlanContent, Is.EqualTo(planContent));
            Assert.That(planData.PlanFilePath, Is.EqualTo(planFilePath));
        });
    }

    #endregion

    #region Helper Methods

    private static TaskStatusUpdateEvent CreateStatusUpdate(
        TaskState state,
        string? messageText = null,
        Dictionary<string, JsonElement>? metadata = null)
    {
        AgentMessage? message = null;
        if (messageText != null)
        {
            message = new AgentMessage
            {
                MessageId = "msg-" + Guid.NewGuid().ToString("N")[..8],
                Role = MessageRole.Agent,
                Parts = new List<Part> { new TextPart { Text = messageText } },
                ContextId = "context-456"
            };
        }

        return new TaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new AgentTaskStatus
            {
                State = state,
                Timestamp = DateTimeOffset.UtcNow,
                Message = message
            },
            Final = state is TaskState.Completed or TaskState.Failed or TaskState.Canceled,
            Metadata = metadata
        };
    }

    private static AgentMessage CreateAgentMessage(string text)
    {
        return new AgentMessage
        {
            MessageId = "msg-" + Guid.NewGuid().ToString("N")[..8],
            Role = MessageRole.Agent,
            Parts = new List<Part> { new TextPart { Text = text } },
            ContextId = "context-456"
        };
    }

    private static DataPart CreateDataPart(string kind, Dictionary<string, object> data)
    {
        var jsonString = JsonSerializer.Serialize(data);
        var dataDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonString)
            ?? new Dictionary<string, JsonElement>();

        var metadataDict = new Dictionary<string, JsonElement>
        {
            ["kind"] = JsonSerializer.Deserialize<JsonElement>($"\"{kind}\"")
        };

        return new DataPart
        {
            Data = dataDict,
            Metadata = metadataDict
        };
    }

    #endregion
}

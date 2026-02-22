using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Homespun.Api.Tests;

/// <summary>
/// Integration tests verifying the full A2A → AG-UI translation flow.
/// These tests verify that A2A events are correctly parsed and translated
/// to AG-UI events that can be broadcast to clients.
/// </summary>
[TestFixture]
public class AGUIEventFlowTests
{
    private AGUIEventService _aguiEventService = null!;
    private ILogger<AGUIEventService> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole())
            .CreateLogger<AGUIEventService>();
        _aguiEventService = new AGUIEventService(_logger);
    }

    #region A2A Task → AG-UI Flow Tests

    [Test]
    public void ProcessA2AEvent_TaskSubmitted_EmitsRunStarted()
    {
        // Arrange
        var taskJson = """
        {
            "kind": "task",
            "id": "task-123",
            "contextId": "context-456",
            "status": {
                "state": "submitted",
                "timestamp": "2024-01-01T00:00:00Z"
            }
        }
        """;

        // Act - Parse and translate
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Task, taskJson);
        Assert.That(parsed, Is.InstanceOf<ParsedAgentTask>());

        var task = ((ParsedAgentTask)parsed!).Task;
        var sessionId = "session-1";
        var events = _aguiEventService.TranslateTask(task, sessionId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunStartedEvent>());
        var runStarted = (RunStartedEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(runStarted.ThreadId, Is.EqualTo(sessionId));
            Assert.That(runStarted.RunId, Is.EqualTo("task-123"));
            Assert.That(runStarted.Type, Is.EqualTo("RUN_STARTED"));
        });
    }

    #endregion

    #region A2A Message → AG-UI Flow Tests

    [Test]
    public void ProcessA2AEvent_Message_AgentRole_EmitsTextMessageEvents()
    {
        // Arrange
        var messageJson = """
        {
            "kind": "message",
            "messageId": "msg-123",
            "role": "agent",
            "parts": [
                { "kind": "text", "text": "Hello, world!" }
            ],
            "contextId": "context-456",
            "taskId": "task-123"
        }
        """;

        // Act - Parse and translate
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Message, messageJson);
        Assert.That(parsed, Is.InstanceOf<ParsedAgentMessage>());

        var message = ((ParsedAgentMessage)parsed!).Message;
        var sessionId = "session-1";
        var events = _aguiEventService.TranslateMessage(message, sessionId).ToList();

        // Assert - Should emit Start, Content, End
        Assert.That(events, Has.Count.EqualTo(3));
        Assert.That(events[0], Is.InstanceOf<TextMessageStartEvent>());
        Assert.That(events[1], Is.InstanceOf<TextMessageContentEvent>());
        Assert.That(events[2], Is.InstanceOf<TextMessageEndEvent>());

        var startEvent = (TextMessageStartEvent)events[0];
        var contentEvent = (TextMessageContentEvent)events[1];
        var endEvent = (TextMessageEndEvent)events[2];

        Assert.Multiple(() =>
        {
            Assert.That(startEvent.MessageId, Is.EqualTo("msg-123"));
            Assert.That(startEvent.Role, Is.EqualTo("assistant"));
            Assert.That(contentEvent.Delta, Is.EqualTo("Hello, world!"));
            Assert.That(endEvent.MessageId, Is.EqualTo("msg-123"));
        });
    }

    [Test]
    public void ProcessA2AEvent_Message_WithToolUse_EmitsToolCallEvents()
    {
        // Arrange
        var messageJson = """
        {
            "kind": "message",
            "messageId": "msg-789",
            "role": "agent",
            "parts": [
                {
                    "kind": "data",
                    "data": { "toolName": "Read", "toolUseId": "tool-1", "input": { "file_path": "/test.txt" } },
                    "metadata": { "kind": "tool_use" }
                }
            ],
            "contextId": "context-456"
        }
        """;

        // Act - Parse and translate
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Message, messageJson);
        Assert.That(parsed, Is.InstanceOf<ParsedAgentMessage>());

        var message = ((ParsedAgentMessage)parsed!).Message;
        var sessionId = "session-1";
        var events = _aguiEventService.TranslateMessage(message, sessionId).ToList();

        // Assert - Should include tool call events
        var toolCallStart = events.OfType<ToolCallStartEvent>().FirstOrDefault();
        var toolCallEnd = events.OfType<ToolCallEndEvent>().FirstOrDefault();

        Assert.That(toolCallStart, Is.Not.Null);
        Assert.That(toolCallEnd, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(toolCallStart!.ToolCallName, Is.EqualTo("Read"));
            Assert.That(toolCallStart.ToolCallId, Is.EqualTo("tool-1"));
        });
    }

    #endregion

    #region A2A StatusUpdate → AG-UI Flow Tests

    [Test]
    public void ProcessA2AEvent_StatusUpdate_Completed_EmitsRunFinished()
    {
        // Arrange
        var statusUpdateJson = """
        {
            "kind": "status-update",
            "taskId": "task-123",
            "contextId": "context-456",
            "status": {
                "state": "completed",
                "timestamp": "2024-01-01T00:00:00Z",
                "message": {
                    "kind": "message",
                    "messageId": "result-msg",
                    "role": "agent",
                    "parts": [{ "kind": "text", "text": "Task completed successfully" }],
                    "contextId": "context-456"
                }
            },
            "final": true
        }
        """;

        // Act - Parse and translate
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, statusUpdateJson);
        Assert.That(parsed, Is.InstanceOf<ParsedTaskStatusUpdateEvent>());

        var statusUpdate = ((ParsedTaskStatusUpdateEvent)parsed!).StatusUpdate;
        var sessionId = "session-1";
        var runId = "run-1";
        var events = _aguiEventService.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<RunFinishedEvent>());
        var runFinished = (RunFinishedEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(runFinished.ThreadId, Is.EqualTo(sessionId));
            Assert.That(runFinished.RunId, Is.EqualTo(runId));
            Assert.That(runFinished.Result, Is.EqualTo("Task completed successfully"));
        });
    }

    [Test]
    public void ProcessA2AEvent_StatusUpdate_InputRequired_Question_EmitsQuestionPending()
    {
        // Arrange
        var statusUpdateJson = """
        {
            "kind": "status-update",
            "taskId": "task-123",
            "contextId": "context-456",
            "status": {
                "state": "input-required",
                "timestamp": "2024-01-01T00:00:00Z"
            },
            "final": false,
            "metadata": {
                "inputType": "question",
                "questions": [
                    {
                        "question": "Which database do you want to use?",
                        "header": "Database",
                        "options": [
                            { "label": "PostgreSQL", "description": "Open source relational database" },
                            { "label": "MongoDB", "description": "Document database" }
                        ],
                        "multiSelect": false
                    }
                ],
                "toolUseId": "tool-ask-1"
            }
        }
        """;

        // Act - Parse and translate
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, statusUpdateJson);
        Assert.That(parsed, Is.InstanceOf<ParsedTaskStatusUpdateEvent>());

        var statusUpdate = ((ParsedTaskStatusUpdateEvent)parsed!).StatusUpdate;
        var sessionId = "session-1";
        var runId = "run-1";
        var events = _aguiEventService.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<CustomEvent>());
        var customEvent = (CustomEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(customEvent.Name, Is.EqualTo(AGUICustomEventName.QuestionPending));
            Assert.That(customEvent.Type, Is.EqualTo("CUSTOM"));
        });

        // Verify question data
        Assert.That(customEvent.Value, Is.InstanceOf<PendingQuestion>());
        var pendingQuestion = (PendingQuestion)customEvent.Value;
        Assert.Multiple(() =>
        {
            Assert.That(pendingQuestion.Questions, Has.Count.EqualTo(1));
            Assert.That(pendingQuestion.Questions[0].Question, Is.EqualTo("Which database do you want to use?"));
            Assert.That(pendingQuestion.Questions[0].Header, Is.EqualTo("Database"));
            Assert.That(pendingQuestion.Questions[0].Options, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void ProcessA2AEvent_StatusUpdate_InputRequired_PlanApproval_EmitsPlanPending()
    {
        // Arrange
        var statusUpdateJson = """
        {
            "kind": "status-update",
            "taskId": "task-123",
            "contextId": "context-456",
            "status": {
                "state": "input-required",
                "timestamp": "2024-01-01T00:00:00Z"
            },
            "final": false,
            "metadata": {
                "inputType": "plan-approval",
                "plan": "# Implementation Plan\n\n1. Create database schema\n2. Add API endpoints\n3. Write tests",
                "planFilePath": "/workdir/.claude-plan.md"
            }
        }
        """;

        // Act - Parse and translate
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, statusUpdateJson);
        Assert.That(parsed, Is.InstanceOf<ParsedTaskStatusUpdateEvent>());

        var statusUpdate = ((ParsedTaskStatusUpdateEvent)parsed!).StatusUpdate;
        var sessionId = "session-1";
        var runId = "run-1";
        var events = _aguiEventService.TranslateStatusUpdate(statusUpdate, sessionId, runId).ToList();

        // Assert
        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.InstanceOf<CustomEvent>());
        var customEvent = (CustomEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(customEvent.Name, Is.EqualTo(AGUICustomEventName.PlanPending));
            Assert.That(customEvent.Type, Is.EqualTo("CUSTOM"));
        });

        // Verify plan data
        Assert.That(customEvent.Value, Is.InstanceOf<AGUIPlanPendingData>());
        var planData = (AGUIPlanPendingData)customEvent.Value;
        Assert.Multiple(() =>
        {
            Assert.That(planData.PlanContent, Does.Contain("Implementation Plan"));
            Assert.That(planData.PlanContent, Does.Contain("Create database schema"));
            Assert.That(planData.PlanFilePath, Is.EqualTo("/workdir/.claude-plan.md"));
        });
    }

    #endregion

    #region End-to-End Flow Tests

    [Test]
    public void FullSessionFlow_TaskStart_Message_Complete()
    {
        // This test simulates a complete session flow:
        // 1. Task submitted
        // 2. Agent message
        // 3. Task completed

        var sessionId = "session-e2e-1";
        var runId = "run-e2e-1";
        var allEvents = new List<AGUIBaseEvent>();

        // Step 1: Task submitted → RunStarted
        var taskJson = """
        {
            "kind": "task",
            "id": "task-e2e",
            "contextId": "context-e2e",
            "status": { "state": "submitted", "timestamp": "2024-01-01T00:00:00Z" }
        }
        """;
        var taskParsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Task, taskJson);
        var task = ((ParsedAgentTask)taskParsed!).Task;
        allEvents.AddRange(_aguiEventService.TranslateTask(task, sessionId));

        // Step 2: Working status
        var workingJson = """
        {
            "kind": "status-update",
            "taskId": "task-e2e",
            "contextId": "context-e2e",
            "status": { "state": "working", "timestamp": "2024-01-01T00:00:01Z" },
            "final": false
        }
        """;
        var workingParsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, workingJson);
        var working = ((ParsedTaskStatusUpdateEvent)workingParsed!).StatusUpdate;
        allEvents.AddRange(_aguiEventService.TranslateStatusUpdate(working, sessionId, runId));

        // Step 3: Agent message
        var messageJson = """
        {
            "kind": "message",
            "messageId": "msg-e2e",
            "role": "agent",
            "parts": [{ "kind": "text", "text": "I've completed the task." }],
            "contextId": "context-e2e"
        }
        """;
        var msgParsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Message, messageJson);
        var message = ((ParsedAgentMessage)msgParsed!).Message;
        allEvents.AddRange(_aguiEventService.TranslateMessage(message, sessionId));

        // Step 4: Task completed
        var completedJson = """
        {
            "kind": "status-update",
            "taskId": "task-e2e",
            "contextId": "context-e2e",
            "status": {
                "state": "completed",
                "timestamp": "2024-01-01T00:00:02Z",
                "message": {
                    "kind": "message",
                    "messageId": "result",
                    "role": "agent",
                    "parts": [{ "kind": "text", "text": "Done" }],
                    "contextId": "context-e2e"
                }
            },
            "final": true
        }
        """;
        var completedParsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, completedJson);
        var completed = ((ParsedTaskStatusUpdateEvent)completedParsed!).StatusUpdate;
        allEvents.AddRange(_aguiEventService.TranslateStatusUpdate(completed, sessionId, runId));

        // Assert - verify complete event sequence
        Assert.That(allEvents, Has.Count.GreaterThanOrEqualTo(6));

        // Should have RunStarted at beginning
        var runStartedEvents = allEvents.OfType<RunStartedEvent>().ToList();
        Assert.That(runStartedEvents, Has.Count.GreaterThanOrEqualTo(1));

        // Should have text message events
        var textStartEvents = allEvents.OfType<TextMessageStartEvent>().ToList();
        var textContentEvents = allEvents.OfType<TextMessageContentEvent>().ToList();
        var textEndEvents = allEvents.OfType<TextMessageEndEvent>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(textStartEvents, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(textContentEvents, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(textEndEvents, Has.Count.GreaterThanOrEqualTo(1));
        });

        // Should have RunFinished at end
        var runFinishedEvents = allEvents.OfType<RunFinishedEvent>().ToList();
        Assert.That(runFinishedEvents, Has.Count.EqualTo(1));
        Assert.That(runFinishedEvents[0].Result, Is.EqualTo("Done"));
    }

    #endregion
}

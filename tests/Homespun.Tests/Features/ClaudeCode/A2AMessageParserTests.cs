using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class A2AMessageParserTests
{
    [Test]
    public void ParseSseEvent_TaskEvent_ReturnsAgentTask()
    {
        var json = """
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

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Task, json);

        Assert.That(result, Is.InstanceOf<ParsedAgentTask>());
        var parsed = (ParsedAgentTask)result!;
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Task.Id, Is.EqualTo("task-123"));
            Assert.That(parsed.Task.ContextId, Is.EqualTo("context-456"));
            Assert.That(parsed.Task.Status.State, Is.EqualTo(TaskState.Submitted));
        });
    }

    [Test]
    public void ParseSseEvent_MessageEvent_AgentRole_ReturnsAgentMessage()
    {
        var json = """
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

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Message, json);

        Assert.That(result, Is.InstanceOf<ParsedAgentMessage>());
        var parsed = (ParsedAgentMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Message.MessageId, Is.EqualTo("msg-123"));
            Assert.That(parsed.Message.Role, Is.EqualTo(MessageRole.Agent));
            Assert.That(parsed.Message.Parts, Has.Count.EqualTo(1));
            Assert.That(parsed.Message.Parts![0], Is.InstanceOf<TextPart>());
            Assert.That(((TextPart)parsed.Message.Parts[0]).Text, Is.EqualTo("Hello, world!"));
        });
    }

    [Test]
    public void ParseSseEvent_MessageEvent_UserRole_ReturnsAgentMessage()
    {
        var json = """
        {
            "kind": "message",
            "messageId": "msg-456",
            "role": "user",
            "parts": [
                { "kind": "text", "text": "Hello from user" }
            ],
            "contextId": "context-456"
        }
        """;

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Message, json);

        Assert.That(result, Is.InstanceOf<ParsedAgentMessage>());
        var parsed = (ParsedAgentMessage)result!;
        Assert.That(parsed.Message.Role, Is.EqualTo(MessageRole.User));
    }

    [Test]
    public void ParseSseEvent_MessageEvent_WithDataPart_ReturnsAgentMessage()
    {
        var json = """
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

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Message, json);

        Assert.That(result, Is.InstanceOf<ParsedAgentMessage>());
        var parsed = (ParsedAgentMessage)result!;
        Assert.That(parsed.Message.Parts, Has.Count.EqualTo(1));
        Assert.That(parsed.Message.Parts![0], Is.InstanceOf<DataPart>());
    }

    [Test]
    public void ParseSseEvent_StatusUpdateEvent_Working_ReturnsTaskStatusUpdateEvent()
    {
        var json = """
        {
            "kind": "status-update",
            "taskId": "task-123",
            "contextId": "context-456",
            "status": {
                "state": "working",
                "timestamp": "2024-01-01T00:00:00Z"
            },
            "final": false
        }
        """;

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, json);

        Assert.That(result, Is.InstanceOf<ParsedTaskStatusUpdateEvent>());
        var parsed = (ParsedTaskStatusUpdateEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(parsed.StatusUpdate.TaskId, Is.EqualTo("task-123"));
            Assert.That(parsed.StatusUpdate.Status.State, Is.EqualTo(TaskState.Working));
            Assert.That(parsed.StatusUpdate.Final, Is.False);
        });
    }

    [Test]
    public void ParseSseEvent_StatusUpdateEvent_InputRequired_ReturnsTaskStatusUpdateEvent()
    {
        var json = """
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
                "inputType": "question"
            }
        }
        """;

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, json);

        Assert.That(result, Is.InstanceOf<ParsedTaskStatusUpdateEvent>());
        var parsed = (ParsedTaskStatusUpdateEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(parsed.StatusUpdate.Status.State, Is.EqualTo(TaskState.InputRequired));
            Assert.That(parsed.StatusUpdate.Final, Is.False);
        });
    }

    [Test]
    public void ParseSseEvent_StatusUpdateEvent_Completed_ReturnsTaskStatusUpdateEvent()
    {
        var json = """
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
                    "parts": [{ "kind": "text", "text": "Task completed" }],
                    "contextId": "context-456"
                }
            },
            "final": true
        }
        """;

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, json);

        Assert.That(result, Is.InstanceOf<ParsedTaskStatusUpdateEvent>());
        var parsed = (ParsedTaskStatusUpdateEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(parsed.StatusUpdate.Status.State, Is.EqualTo(TaskState.Completed));
            Assert.That(parsed.StatusUpdate.Final, Is.True);
            Assert.That(parsed.StatusUpdate.Status.Message, Is.Not.Null);
        });
    }

    [Test]
    public void ParseSseEvent_StatusUpdateEvent_Failed_ReturnsTaskStatusUpdateEvent()
    {
        var json = """
        {
            "kind": "status-update",
            "taskId": "task-123",
            "contextId": "context-456",
            "status": {
                "state": "failed",
                "timestamp": "2024-01-01T00:00:00Z",
                "message": {
                    "kind": "message",
                    "messageId": "error-msg",
                    "role": "agent",
                    "parts": [{ "kind": "text", "text": "Error occurred" }],
                    "contextId": "context-456"
                }
            },
            "final": true
        }
        """;

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, json);

        Assert.That(result, Is.InstanceOf<ParsedTaskStatusUpdateEvent>());
        var parsed = (ParsedTaskStatusUpdateEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(parsed.StatusUpdate.Status.State, Is.EqualTo(TaskState.Failed));
            Assert.That(parsed.StatusUpdate.Final, Is.True);
        });
    }

    [Test]
    public void ParseSseEvent_InvalidJson_ReturnsNull()
    {
        var json = "not valid json";

        var result = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Task, json);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ParseSseEvent_UnknownKind_ReturnsNull()
    {
        var json = """
        {
            "kind": "unknown-event-type",
            "id": "test-123"
        }
        """;

        var result = A2AMessageParser.ParseSseEvent("unknown-event-type", json);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void IsA2AEventKind_TaskKind_ReturnsTrue()
    {
        Assert.That(A2AMessageParser.IsA2AEventKind(HomespunA2AEventKind.Task), Is.True);
    }

    [Test]
    public void IsA2AEventKind_MessageKind_ReturnsTrue()
    {
        Assert.That(A2AMessageParser.IsA2AEventKind(HomespunA2AEventKind.Message), Is.True);
    }

    [Test]
    public void IsA2AEventKind_StatusUpdateKind_ReturnsTrue()
    {
        Assert.That(A2AMessageParser.IsA2AEventKind(HomespunA2AEventKind.StatusUpdate), Is.True);
    }

    [Test]
    public void IsA2AEventKind_UnknownKind_ReturnsFalse()
    {
        Assert.That(A2AMessageParser.IsA2AEventKind("unknown"), Is.False);
    }

    [Test]
    public void ConvertToSdkMessage_Task_Submitted_ReturnsSystemMessage()
    {
        var task = new AgentTask
        {
            Id = "task-123",
            ContextId = "context-456",
            Status = new AgentTaskStatus
            {
                State = TaskState.Submitted,
                Timestamp = DateTimeOffset.Parse("2024-01-01T00:00:00Z")
            }
        };
        var parsed = new ParsedAgentTask(task);

        var result = A2AMessageParser.ConvertToSdkMessage(parsed, "session-1");

        Assert.That(result, Is.InstanceOf<SdkSystemMessage>());
        var sysMsg = (SdkSystemMessage)result!;
        Assert.That(sysMsg.Subtype, Is.EqualTo("session_started"));
    }

    [Test]
    public void ConvertToSdkMessage_Message_AgentRole_ReturnsAssistantMessage()
    {
        var message = new AgentMessage
        {
            MessageId = "msg-123",
            Role = MessageRole.Agent,
            Parts = new List<Part> { new TextPart { Text = "Hello" } },
            ContextId = "context-456"
        };
        var parsed = new ParsedAgentMessage(message);

        var result = A2AMessageParser.ConvertToSdkMessage(parsed, "session-1");

        Assert.That(result, Is.InstanceOf<SdkAssistantMessage>());
        var assistantMsg = (SdkAssistantMessage)result!;
        Assert.That(assistantMsg.Message.Role, Is.EqualTo("assistant"));
        Assert.That(assistantMsg.Message.Content, Has.Count.EqualTo(1));
    }

    [Test]
    public void ConvertToSdkMessage_Message_UserRole_ReturnsUserMessage()
    {
        var message = new AgentMessage
        {
            MessageId = "msg-123",
            Role = MessageRole.User,
            Parts = new List<Part> { new TextPart { Text = "Hello from user" } },
            ContextId = "context-456"
        };
        var parsed = new ParsedAgentMessage(message);

        var result = A2AMessageParser.ConvertToSdkMessage(parsed, "session-1");

        Assert.That(result, Is.InstanceOf<SdkUserMessage>());
        var userMsg = (SdkUserMessage)result!;
        Assert.That(userMsg.Message.Role, Is.EqualTo("user"));
    }

    [Test]
    public void ConvertToSdkMessage_StatusUpdate_Completed_ReturnsResultMessage()
    {
        var statusUpdate = new TaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new AgentTaskStatus
            {
                State = TaskState.Completed,
                Timestamp = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                Message = new AgentMessage
                {
                    MessageId = "result-msg",
                    Role = MessageRole.Agent,
                    Parts = new List<Part> { new TextPart { Text = "Success" } },
                    ContextId = "context-456"
                }
            },
            Final = true
        };
        var parsed = new ParsedTaskStatusUpdateEvent(statusUpdate);

        var result = A2AMessageParser.ConvertToSdkMessage(parsed, "session-1");

        Assert.That(result, Is.InstanceOf<SdkResultMessage>());
        var resultMsg = (SdkResultMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(resultMsg.Subtype, Is.EqualTo("success"));
            Assert.That(resultMsg.IsError, Is.False);
            Assert.That(resultMsg.Result, Is.EqualTo("Success"));
        });
    }

    [Test]
    public void ConvertToSdkMessage_StatusUpdate_Failed_ReturnsResultMessageWithError()
    {
        var statusUpdate = new TaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new AgentTaskStatus
            {
                State = TaskState.Failed,
                Timestamp = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                Message = new AgentMessage
                {
                    MessageId = "error-msg",
                    Role = MessageRole.Agent,
                    Parts = new List<Part> { new TextPart { Text = "Something went wrong" } },
                    ContextId = "context-456"
                }
            },
            Final = true
        };
        var parsed = new ParsedTaskStatusUpdateEvent(statusUpdate);

        var result = A2AMessageParser.ConvertToSdkMessage(parsed, "session-1");

        Assert.That(result, Is.InstanceOf<SdkResultMessage>());
        var resultMsg = (SdkResultMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(resultMsg.Subtype, Is.EqualTo("error_during_execution"));
            Assert.That(resultMsg.IsError, Is.True);
        });
    }

    [Test]
    public void ConvertToSdkMessage_StatusUpdate_Working_ReturnsNull()
    {
        var statusUpdate = new TaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new AgentTaskStatus
            {
                State = TaskState.Working,
                Timestamp = DateTimeOffset.Parse("2024-01-01T00:00:00Z")
            },
            Final = false
        };
        var parsed = new ParsedTaskStatusUpdateEvent(statusUpdate);

        var result = A2AMessageParser.ConvertToSdkMessage(parsed, "session-1");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ConvertToSdkMessage_StatusUpdate_InputRequired_Question_WithQuestionsArrayInMetadata_ReturnsQuestionPendingMessage()
    {
        // This test replicates the bug where questions array in metadata
        // causes InvalidOperationException when consumer tries to access .questions property
        var questionsArrayJson = """
        [
            {
                "question": "What framework?",
                "header": "Framework",
                "options": [{ "label": "React", "description": "React framework" }],
                "multiSelect": false
            }
        ]
        """;

        var metadata = new Dictionary<string, JsonElement>
        {
            ["inputType"] = JsonSerializer.Deserialize<JsonElement>("\"question\""),
            ["questions"] = JsonSerializer.Deserialize<JsonElement>(questionsArrayJson)
        };

        var statusUpdate = new TaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new AgentTaskStatus
            {
                State = TaskState.InputRequired,
                Timestamp = DateTimeOffset.UtcNow
            },
            Final = false,
            Metadata = metadata
        };

        var result = A2AMessageParser.ConvertToSdkMessage(new ParsedTaskStatusUpdateEvent(statusUpdate), "session-1");

        Assert.That(result, Is.InstanceOf<SdkQuestionPendingMessage>());
        var questionMsg = (SdkQuestionPendingMessage)result!;

        // The QuestionsJson should be wrapped in an object with "questions" property
        using var doc = JsonDocument.Parse(questionMsg.QuestionsJson);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(doc.RootElement.TryGetProperty("questions", out var questionsProperty), Is.True);
        Assert.That(questionsProperty.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public void ConvertToSdkMessage_StatusUpdate_InputRequired_Question_WithQuestionsObjectInMetadata_ReturnsQuestionPendingMessage()
    {
        // Test case where metadata already contains the questions in object format
        var questionsObjectJson = """
        {
            "questions": [
                {
                    "question": "What color?",
                    "header": "Color",
                    "options": [{ "label": "Red", "description": "The color red" }],
                    "multiSelect": false
                }
            ]
        }
        """;

        var metadata = new Dictionary<string, JsonElement>
        {
            ["inputType"] = JsonSerializer.Deserialize<JsonElement>("\"question\""),
            ["questions"] = JsonSerializer.Deserialize<JsonElement>(questionsObjectJson)
        };

        var statusUpdate = new TaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new AgentTaskStatus
            {
                State = TaskState.InputRequired,
                Timestamp = DateTimeOffset.UtcNow
            },
            Final = false,
            Metadata = metadata
        };

        var result = A2AMessageParser.ConvertToSdkMessage(new ParsedTaskStatusUpdateEvent(statusUpdate), "session-1");

        Assert.That(result, Is.InstanceOf<SdkQuestionPendingMessage>());
        var questionMsg = (SdkQuestionPendingMessage)result!;

        // Should pass through object format unchanged
        using var doc = JsonDocument.Parse(questionMsg.QuestionsJson);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(doc.RootElement.TryGetProperty("questions", out _), Is.True);
    }
}

using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class A2AMessageParserTests
{
    [Test]
    public void ParseSseEvent_TaskEvent_ReturnsA2ATask()
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

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.Task, json);

        Assert.That(result, Is.InstanceOf<A2ATask>());
        var task = (A2ATask)result!;
        Assert.Multiple(() =>
        {
            Assert.That(task.Kind, Is.EqualTo("task"));
            Assert.That(task.Id, Is.EqualTo("task-123"));
            Assert.That(task.ContextId, Is.EqualTo("context-456"));
            Assert.That(task.Status.State, Is.EqualTo("submitted"));
        });
    }

    [Test]
    public void ParseSseEvent_MessageEvent_AgentRole_ReturnsA2AMessage()
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

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.Message, json);

        Assert.That(result, Is.InstanceOf<A2AMessage>());
        var msg = (A2AMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.Kind, Is.EqualTo("message"));
            Assert.That(msg.MessageId, Is.EqualTo("msg-123"));
            Assert.That(msg.Role, Is.EqualTo("agent"));
            Assert.That(msg.Parts, Has.Count.EqualTo(1));
            Assert.That(msg.Parts[0], Is.InstanceOf<A2ATextPart>());
            Assert.That(((A2ATextPart)msg.Parts[0]).Text, Is.EqualTo("Hello, world!"));
        });
    }

    [Test]
    public void ParseSseEvent_MessageEvent_UserRole_ReturnsA2AMessage()
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

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.Message, json);

        Assert.That(result, Is.InstanceOf<A2AMessage>());
        var msg = (A2AMessage)result!;
        Assert.That(msg.Role, Is.EqualTo("user"));
    }

    [Test]
    public void ParseSseEvent_MessageEvent_WithDataPart_ReturnsA2AMessage()
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

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.Message, json);

        Assert.That(result, Is.InstanceOf<A2AMessage>());
        var msg = (A2AMessage)result!;
        Assert.That(msg.Parts, Has.Count.EqualTo(1));
        Assert.That(msg.Parts[0], Is.InstanceOf<A2ADataPart>());
    }

    [Test]
    public void ParseSseEvent_StatusUpdateEvent_Working_ReturnsA2ATaskStatusUpdateEvent()
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

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.StatusUpdate, json);

        Assert.That(result, Is.InstanceOf<A2ATaskStatusUpdateEvent>());
        var statusUpdate = (A2ATaskStatusUpdateEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(statusUpdate.Kind, Is.EqualTo("status-update"));
            Assert.That(statusUpdate.TaskId, Is.EqualTo("task-123"));
            Assert.That(statusUpdate.Status.State, Is.EqualTo("working"));
            Assert.That(statusUpdate.Final, Is.False);
        });
    }

    [Test]
    public void ParseSseEvent_StatusUpdateEvent_InputRequired_ReturnsA2ATaskStatusUpdateEvent()
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

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.StatusUpdate, json);

        Assert.That(result, Is.InstanceOf<A2ATaskStatusUpdateEvent>());
        var statusUpdate = (A2ATaskStatusUpdateEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(statusUpdate.Status.State, Is.EqualTo("input-required"));
            Assert.That(statusUpdate.Final, Is.False);
        });
    }

    [Test]
    public void ParseSseEvent_StatusUpdateEvent_Completed_ReturnsA2ATaskStatusUpdateEvent()
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

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.StatusUpdate, json);

        Assert.That(result, Is.InstanceOf<A2ATaskStatusUpdateEvent>());
        var statusUpdate = (A2ATaskStatusUpdateEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(statusUpdate.Status.State, Is.EqualTo("completed"));
            Assert.That(statusUpdate.Final, Is.True);
            Assert.That(statusUpdate.Status.Message, Is.Not.Null);
        });
    }

    [Test]
    public void ParseSseEvent_StatusUpdateEvent_Failed_ReturnsA2ATaskStatusUpdateEvent()
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

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.StatusUpdate, json);

        Assert.That(result, Is.InstanceOf<A2ATaskStatusUpdateEvent>());
        var statusUpdate = (A2ATaskStatusUpdateEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(statusUpdate.Status.State, Is.EqualTo("failed"));
            Assert.That(statusUpdate.Final, Is.True);
        });
    }

    [Test]
    public void ParseSseEvent_InvalidJson_ReturnsNull()
    {
        var json = "not valid json";

        var result = A2AMessageParser.ParseSseEvent(A2AEventKind.Task, json);

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
        Assert.That(A2AMessageParser.IsA2AEventKind(A2AEventKind.Task), Is.True);
    }

    [Test]
    public void IsA2AEventKind_MessageKind_ReturnsTrue()
    {
        Assert.That(A2AMessageParser.IsA2AEventKind(A2AEventKind.Message), Is.True);
    }

    [Test]
    public void IsA2AEventKind_StatusUpdateKind_ReturnsTrue()
    {
        Assert.That(A2AMessageParser.IsA2AEventKind(A2AEventKind.StatusUpdate), Is.True);
    }

    [Test]
    public void IsA2AEventKind_UnknownKind_ReturnsFalse()
    {
        Assert.That(A2AMessageParser.IsA2AEventKind("unknown"), Is.False);
    }

    [Test]
    public void ConvertToSdkMessage_Task_Submitted_ReturnsSystemMessage()
    {
        var task = new A2ATask
        {
            Id = "task-123",
            ContextId = "context-456",
            Status = new A2ATaskStatus
            {
                State = A2ATaskState.Submitted,
                Timestamp = "2024-01-01T00:00:00Z"
            }
        };

        var result = A2AMessageParser.ConvertToSdkMessage(task, "session-1");

        Assert.That(result, Is.InstanceOf<SdkSystemMessage>());
        var sysMsg = (SdkSystemMessage)result!;
        Assert.That(sysMsg.Subtype, Is.EqualTo("session_started"));
    }

    [Test]
    public void ConvertToSdkMessage_Message_AgentRole_ReturnsAssistantMessage()
    {
        var message = new A2AMessage
        {
            MessageId = "msg-123",
            Role = "agent",
            Parts = new List<A2APart> { new A2ATextPart { Text = "Hello" } },
            ContextId = "context-456"
        };

        var result = A2AMessageParser.ConvertToSdkMessage(message, "session-1");

        Assert.That(result, Is.InstanceOf<SdkAssistantMessage>());
        var assistantMsg = (SdkAssistantMessage)result!;
        Assert.That(assistantMsg.Message.Role, Is.EqualTo("assistant"));
        Assert.That(assistantMsg.Message.Content, Has.Count.EqualTo(1));
    }

    [Test]
    public void ConvertToSdkMessage_Message_UserRole_ReturnsUserMessage()
    {
        var message = new A2AMessage
        {
            MessageId = "msg-123",
            Role = "user",
            Parts = new List<A2APart> { new A2ATextPart { Text = "Hello from user" } },
            ContextId = "context-456"
        };

        var result = A2AMessageParser.ConvertToSdkMessage(message, "session-1");

        Assert.That(result, Is.InstanceOf<SdkUserMessage>());
        var userMsg = (SdkUserMessage)result!;
        Assert.That(userMsg.Message.Role, Is.EqualTo("user"));
    }

    [Test]
    public void ConvertToSdkMessage_StatusUpdate_Completed_ReturnsResultMessage()
    {
        var statusUpdate = new A2ATaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new A2ATaskStatus
            {
                State = A2ATaskState.Completed,
                Timestamp = "2024-01-01T00:00:00Z",
                Message = new A2AMessage
                {
                    MessageId = "result-msg",
                    Role = "agent",
                    Parts = new List<A2APart> { new A2ATextPart { Text = "Success" } },
                    ContextId = "context-456"
                }
            },
            Final = true
        };

        var result = A2AMessageParser.ConvertToSdkMessage(statusUpdate, "session-1");

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
        var statusUpdate = new A2ATaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new A2ATaskStatus
            {
                State = A2ATaskState.Failed,
                Timestamp = "2024-01-01T00:00:00Z",
                Message = new A2AMessage
                {
                    MessageId = "error-msg",
                    Role = "agent",
                    Parts = new List<A2APart> { new A2ATextPart { Text = "Something went wrong" } },
                    ContextId = "context-456"
                }
            },
            Final = true
        };

        var result = A2AMessageParser.ConvertToSdkMessage(statusUpdate, "session-1");

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
        var statusUpdate = new A2ATaskStatusUpdateEvent
        {
            TaskId = "task-123",
            ContextId = "context-456",
            Status = new A2ATaskStatus
            {
                State = A2ATaskState.Working,
                Timestamp = "2024-01-01T00:00:00Z"
            },
            Final = false
        };

        var result = A2AMessageParser.ConvertToSdkMessage(statusUpdate, "session-1");

        Assert.That(result, Is.Null);
    }
}

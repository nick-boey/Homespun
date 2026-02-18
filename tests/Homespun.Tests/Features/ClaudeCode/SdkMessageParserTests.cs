using System.Text.Json;
using Homespun.Features.ClaudeCode.Services;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SdkMessageParserTests
{
    private JsonSerializerOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _options = SdkMessageParser.CreateJsonOptions();
    }

    [Test]
    public void Deserialize_AssistantMessage_ReturnsCorrectType()
    {
        var json = """
        {
            "type": "assistant",
            "session_id": "sess-123",
            "uuid": "uuid-1",
            "message": {
                "role": "assistant",
                "content": [
                    { "type": "text", "text": "Hello world" }
                ]
            },
            "parent_tool_use_id": null
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkAssistantMessage>());
        var msg = (SdkAssistantMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("sess-123"));
            Assert.That(msg.Uuid, Is.EqualTo("uuid-1"));
            Assert.That(msg.Message.Role, Is.EqualTo("assistant"));
            Assert.That(msg.Message.Content, Has.Count.EqualTo(1));
            Assert.That(msg.Message.Content[0], Is.InstanceOf<SdkTextBlock>());
            Assert.That(((SdkTextBlock)msg.Message.Content[0]).Text, Is.EqualTo("Hello world"));
        });
    }

    [Test]
    public void Deserialize_AssistantMessage_WithMultipleContentBlocks()
    {
        var json = """
        {
            "type": "assistant",
            "session_id": "sess-123",
            "uuid": "uuid-1",
            "message": {
                "role": "assistant",
                "content": [
                    { "type": "thinking", "thinking": "Let me think about this..." },
                    { "type": "text", "text": "Here is my answer" },
                    { "type": "tool_use", "id": "tool-1", "name": "Read", "input": {"file_path": "/test.txt"} }
                ]
            }
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkAssistantMessage>());
        var msg = (SdkAssistantMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.Message.Content, Has.Count.EqualTo(3));
            Assert.That(msg.Message.Content[0], Is.InstanceOf<SdkThinkingBlock>());
            Assert.That(msg.Message.Content[1], Is.InstanceOf<SdkTextBlock>());
            Assert.That(msg.Message.Content[2], Is.InstanceOf<SdkToolUseBlock>());

            var toolUse = (SdkToolUseBlock)msg.Message.Content[2];
            Assert.That(toolUse.Id, Is.EqualTo("tool-1"));
            Assert.That(toolUse.Name, Is.EqualTo("Read"));
        });
    }

    [Test]
    public void Deserialize_UserMessage_WithToolResults()
    {
        var json = """
        {
            "type": "user",
            "session_id": "sess-123",
            "uuid": "uuid-2",
            "message": {
                "role": "user",
                "content": [
                    { "type": "tool_result", "tool_use_id": "tool-1", "content": "file contents here", "is_error": false }
                ]
            },
            "parent_tool_use_id": "parent-1"
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkUserMessage>());
        var msg = (SdkUserMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("sess-123"));
            Assert.That(msg.ParentToolUseId, Is.EqualTo("parent-1"));
            Assert.That(msg.Message.Content, Has.Count.EqualTo(1));
            Assert.That(msg.Message.Content[0], Is.InstanceOf<SdkToolResultBlock>());

            var toolResult = (SdkToolResultBlock)msg.Message.Content[0];
            Assert.That(toolResult.ToolUseId, Is.EqualTo("tool-1"));
            Assert.That(toolResult.IsError, Is.False);
        });
    }

    [Test]
    public void Deserialize_ResultMessage_ReturnsCorrectType()
    {
        var json = """
        {
            "type": "result",
            "session_id": "sess-123",
            "uuid": "uuid-3",
            "subtype": "success",
            "duration_ms": 5000,
            "duration_api_ms": 4500,
            "is_error": false,
            "num_turns": 3,
            "total_cost_usd": 0.0123,
            "result": "Task completed successfully"
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkResultMessage>());
        var msg = (SdkResultMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("sess-123"));
            Assert.That(msg.DurationMs, Is.EqualTo(5000));
            Assert.That(msg.DurationApiMs, Is.EqualTo(4500));
            Assert.That(msg.IsError, Is.False);
            Assert.That(msg.NumTurns, Is.EqualTo(3));
            Assert.That(msg.TotalCostUsd, Is.EqualTo(0.0123m));
            Assert.That(msg.Result, Is.EqualTo("Task completed successfully"));
        });
    }

    [Test]
    public void Deserialize_SystemMessage_ReturnsCorrectType()
    {
        var json = """
        {
            "type": "system",
            "session_id": "sess-123",
            "uuid": "uuid-4",
            "subtype": "init",
            "model": "claude-sonnet-4-20250514",
            "tools": ["Read", "Write", "Bash"]
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkSystemMessage>());
        var msg = (SdkSystemMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("sess-123"));
            Assert.That(msg.Subtype, Is.EqualTo("init"));
            Assert.That(msg.Model, Is.EqualTo("claude-sonnet-4-20250514"));
            Assert.That(msg.Tools, Is.EqualTo(new[] { "Read", "Write", "Bash" }));
        });
    }

    [Test]
    public void Deserialize_StreamEvent_ReturnsCorrectType()
    {
        var json = """
        {
            "type": "stream_event",
            "session_id": "sess-123",
            "uuid": "uuid-5",
            "event": {
                "type": "content_block_start",
                "index": 0,
                "content_block": { "type": "text", "text": "" }
            },
            "parent_tool_use_id": null
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkStreamEvent>());
        var msg = (SdkStreamEvent)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("sess-123"));
            Assert.That(msg.Event, Is.Not.Null);
            Assert.That(msg.Event!.Value.GetProperty("type").GetString(), Is.EqualTo("content_block_start"));
        });
    }

    [Test]
    public void Deserialize_ResultMessage_WithMissingOptionalFields()
    {
        var json = """
        {
            "type": "result",
            "session_id": "sess-123",
            "duration_ms": 0,
            "duration_api_ms": 0,
            "is_error": false,
            "num_turns": 0,
            "total_cost_usd": 0
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkResultMessage>());
        var msg = (SdkResultMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.Uuid, Is.Null);
            Assert.That(msg.Subtype, Is.Null);
            Assert.That(msg.Result, Is.Null);
        });
    }

    [Test]
    public void Deserialize_UnknownType_ReturnsNull()
    {
        var json = """
        {
            "type": "unknown_type",
            "session_id": "sess-123"
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Deserialize_ContentBlock_Text()
    {
        var json = """{ "type": "text", "text": "Hello" }""";

        var result = JsonSerializer.Deserialize<SdkContentBlock>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkTextBlock>());
        Assert.That(((SdkTextBlock)result!).Text, Is.EqualTo("Hello"));
    }

    [Test]
    public void Deserialize_ContentBlock_Thinking()
    {
        var json = """{ "type": "thinking", "thinking": "Let me think..." }""";

        var result = JsonSerializer.Deserialize<SdkContentBlock>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkThinkingBlock>());
        Assert.That(((SdkThinkingBlock)result!).Thinking, Is.EqualTo("Let me think..."));
    }

    [Test]
    public void Deserialize_ContentBlock_ToolUse()
    {
        var json = """{ "type": "tool_use", "id": "tool-1", "name": "Read", "input": {"file_path": "/test"} }""";

        var result = JsonSerializer.Deserialize<SdkContentBlock>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkToolUseBlock>());
        var block = (SdkToolUseBlock)result!;
        Assert.Multiple(() =>
        {
            Assert.That(block.Id, Is.EqualTo("tool-1"));
            Assert.That(block.Name, Is.EqualTo("Read"));
            Assert.That(block.Input.GetProperty("file_path").GetString(), Is.EqualTo("/test"));
        });
    }

    [Test]
    public void Deserialize_ContentBlock_ToolResult()
    {
        var json = """{ "type": "tool_result", "tool_use_id": "tool-1", "content": "result text", "is_error": true }""";

        var result = JsonSerializer.Deserialize<SdkContentBlock>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkToolResultBlock>());
        var block = (SdkToolResultBlock)result!;
        Assert.Multiple(() =>
        {
            Assert.That(block.ToolUseId, Is.EqualTo("tool-1"));
            Assert.That(block.IsError, Is.True);
        });
    }

    [Test]
    public void Deserialize_ContentBlock_UnknownType_ReturnsNull()
    {
        var json = """{ "type": "unknown", "data": "whatever" }""";

        var result = JsonSerializer.Deserialize<SdkContentBlock>(json, _options);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Deserialize_AssistantMessage_WithNullParentToolUseId()
    {
        var json = """
        {
            "type": "assistant",
            "session_id": "sess-123",
            "message": {
                "role": "assistant",
                "content": []
            }
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkAssistantMessage>());
        var msg = (SdkAssistantMessage)result!;
        Assert.That(msg.ParentToolUseId, Is.Null);
    }

    [Test]
    public void Deserialize_StreamEvent_WithNullEvent()
    {
        var json = """
        {
            "type": "stream_event",
            "session_id": "sess-123"
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkStreamEvent>());
        var msg = (SdkStreamEvent)result!;
        Assert.That(msg.Event, Is.Null);
    }

    [Test]
    public void Deserialize_ResultMessage_WithErrorsArray()
    {
        var json = """
        {
            "type": "result",
            "session_id": "sess-123",
            "subtype": "error_max_turns",
            "duration_ms": 5000,
            "duration_api_ms": 4500,
            "is_error": true,
            "num_turns": 10,
            "total_cost_usd": 0.05,
            "result": "Maximum turns reached",
            "errors": ["Turn limit exceeded", "Please continue the conversation to proceed"]
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkResultMessage>());
        var msg = (SdkResultMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.SessionId, Is.EqualTo("sess-123"));
            Assert.That(msg.Subtype, Is.EqualTo("error_max_turns"));
            Assert.That(msg.IsError, Is.True);
            Assert.That(msg.Errors, Is.Not.Null);
            Assert.That(msg.Errors, Has.Count.EqualTo(2));
            Assert.That(msg.Errors![0], Is.EqualTo("Turn limit exceeded"));
            Assert.That(msg.Errors![1], Is.EqualTo("Please continue the conversation to proceed"));
        });
    }

    [Test]
    public void Deserialize_ResultMessage_ErrorDuringExecution()
    {
        var json = """
        {
            "type": "result",
            "session_id": "sess-456",
            "subtype": "error_during_execution",
            "duration_ms": 2000,
            "duration_api_ms": 1500,
            "is_error": true,
            "num_turns": 3,
            "total_cost_usd": 0.01,
            "result": "Tool execution failed",
            "errors": ["Command timed out after 60 seconds"]
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkResultMessage>());
        var msg = (SdkResultMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.Subtype, Is.EqualTo("error_during_execution"));
            Assert.That(msg.IsError, Is.True);
            Assert.That(msg.Result, Is.EqualTo("Tool execution failed"));
            Assert.That(msg.Errors, Has.Count.EqualTo(1));
            Assert.That(msg.Errors![0], Is.EqualTo("Command timed out after 60 seconds"));
        });
    }

    [Test]
    public void Deserialize_ResultMessage_WithEmptyErrorsArray()
    {
        var json = """
        {
            "type": "result",
            "session_id": "sess-789",
            "subtype": "success",
            "duration_ms": 1000,
            "duration_api_ms": 900,
            "is_error": false,
            "num_turns": 1,
            "total_cost_usd": 0.001,
            "errors": []
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkResultMessage>());
        var msg = (SdkResultMessage)result!;
        Assert.Multiple(() =>
        {
            Assert.That(msg.IsError, Is.False);
            Assert.That(msg.Errors, Is.Not.Null);
            Assert.That(msg.Errors, Is.Empty);
        });
    }

    [Test]
    public void Deserialize_ResultMessage_WithoutErrorsField()
    {
        var json = """
        {
            "type": "result",
            "session_id": "sess-abc",
            "subtype": "success",
            "duration_ms": 500,
            "duration_api_ms": 400,
            "is_error": false,
            "num_turns": 1,
            "total_cost_usd": 0.002
        }
        """;

        var result = JsonSerializer.Deserialize<SdkMessage>(json, _options);

        Assert.That(result, Is.InstanceOf<SdkResultMessage>());
        var msg = (SdkResultMessage)result!;
        Assert.That(msg.Errors, Is.Null);
    }
}

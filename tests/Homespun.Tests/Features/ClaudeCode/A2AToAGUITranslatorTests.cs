using System.Text.Json;
using A2A;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// TDD tests for <see cref="A2AToAGUITranslator"/> (tasks 3.1–3.5 of a2a-native-messaging).
/// One canonical A2A fixture per variant; the property test at the end asserts no fixture
/// (or random unknown variant) can cause the translator to throw.
/// </summary>
[TestFixture]
public class A2AToAGUITranslatorTests
{
    private A2AToAGUITranslator _translator = null!;
    private TranslationContext _ctx = null!;

    private const string SessionId = "session-1";
    private const string RunId = "run-xyz";

    [SetUp]
    public void SetUp()
    {
        _translator = new A2AToAGUITranslator();
        _ctx = new TranslationContext(SessionId, RunId);
    }

    // ---------------- Task (submitted) ----------------

    [Test]
    public void Translate_TaskSubmitted_EmitsRunStarted()
    {
        var parsed = ParseTask("""
        {
          "kind": "task",
          "id": "task-1",
          "contextId": "ctx-1",
          "status": { "state": "submitted" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.TypeOf<RunStartedEvent>());
        var run = (RunStartedEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(run.ThreadId, Is.EqualTo(SessionId));
            Assert.That(run.RunId, Is.EqualTo(RunId));
        });
    }

    // ---------------- Message user-text ----------------

    [Test]
    public void Translate_UserTextMessage_EmitsCustomUserMessage()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-u",
          "role": "user",
          "parts": [ { "kind": "text", "text": "hello there" } ],
          "metadata": { "sdkMessageType": "user" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        var custom = AssertCustom(events[0], AGUICustomEventName.UserMessage);
        var text = GetProperty(custom.Value, "text");
        Assert.That(text, Is.EqualTo("hello there"));
    }

    // ---------------- Message agent-text ----------------

    [Test]
    public void Translate_AgentTextMessage_EmitsStartContentEnd()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-a",
          "role": "agent",
          "parts": [ { "kind": "text", "text": "Hello from Claude" } ],
          "metadata": { "sdkMessageType": "assistant" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(3));
        Assert.That(events[0], Is.TypeOf<TextMessageStartEvent>());
        Assert.That(events[1], Is.TypeOf<TextMessageContentEvent>());
        Assert.That(events[2], Is.TypeOf<TextMessageEndEvent>());

        var content = (TextMessageContentEvent)events[1];
        Assert.That(content.Delta, Is.EqualTo("Hello from Claude"));

        // All three events share the same per-block messageId.
        var ids = events.Select(e => e switch
        {
            TextMessageStartEvent s => s.MessageId,
            TextMessageContentEvent c => c.MessageId,
            TextMessageEndEvent en => en.MessageId,
            _ => null,
        }).Distinct().ToList();
        Assert.That(ids, Has.Count.EqualTo(1), "start/content/end must share one id");
    }

    // ---------------- Message agent-thinking ----------------

    [Test]
    public void Translate_AgentThinkingMessage_EmitsCustomThinking()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-t",
          "role": "agent",
          "parts": [
            {
              "kind": "data",
              "data": { "thinking": "let me reason about this" },
              "metadata": { "kind": "thinking", "isThinking": true }
            }
          ],
          "metadata": { "sdkMessageType": "assistant" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        var custom = AssertCustom(events[0], AGUICustomEventName.Thinking);
        Assert.That(GetProperty(custom.Value, "text"), Is.EqualTo("let me reason about this"));
        Assert.That(GetProperty(custom.Value, "parentMessageId"), Is.EqualTo("msg-t"));
    }

    // ---------------- Message agent-tool_use ----------------

    [Test]
    public void Translate_AgentToolUseMessage_EmitsToolCallStartArgsEnd()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-tu",
          "role": "agent",
          "parts": [
            {
              "kind": "data",
              "data": {
                "toolUseId": "tool-call-42",
                "toolName": "Bash",
                "input": { "command": "ls -la" }
              },
              "metadata": { "kind": "tool_use" }
            }
          ],
          "metadata": { "sdkMessageType": "assistant" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(3));
        Assert.That(events[0], Is.TypeOf<ToolCallStartEvent>());
        Assert.That(events[1], Is.TypeOf<ToolCallArgsEvent>());
        Assert.That(events[2], Is.TypeOf<ToolCallEndEvent>());

        var start = (ToolCallStartEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(start.ToolCallId, Is.EqualTo("tool-call-42"));
            Assert.That(start.ToolCallName, Is.EqualTo("Bash"));
            Assert.That(start.ParentMessageId, Is.EqualTo("msg-tu"));
        });

        var args = (ToolCallArgsEvent)events[1];
        Assert.That(args.ToolCallId, Is.EqualTo("tool-call-42"));
        Assert.That(args.Delta, Does.Contain("command"));
        Assert.That(args.Delta, Does.Contain("ls -la"));
    }

    // ---------------- Message user tool_result ----------------

    [Test]
    public void Translate_UserToolResultMessage_EmitsToolCallResult()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-tr",
          "role": "user",
          "parts": [
            {
              "kind": "data",
              "data": {
                "toolUseId": "tool-call-42",
                "content": "total 0\ndrwxr-xr-x ."
              },
              "metadata": { "kind": "tool_result" }
            }
          ],
          "metadata": { "sdkMessageType": "user" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.TypeOf<ToolCallResultEvent>());
        var result = (ToolCallResultEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.ToolCallId, Is.EqualTo("tool-call-42"));
            Assert.That(result.Content, Does.Contain("total 0"));
        });
    }

    // ---------------- Message system (init) ----------------

    [Test]
    public void Translate_SystemInitMessage_EmitsCustomSystemInit()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-sys",
          "role": "agent",
          "parts": [
            {
              "kind": "data",
              "data": {
                "subtype": "init",
                "model": "claude-opus-4-6",
                "tools": ["Bash", "Read"],
                "permissionMode": "bypass"
              },
              "metadata": { "kind": "system" }
            }
          ],
          "metadata": { "sdkMessageType": "system" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        var custom = AssertCustom(events[0], AGUICustomEventName.SystemInit);
        Assert.Multiple(() =>
        {
            Assert.That(GetProperty(custom.Value, "model"), Is.EqualTo("claude-opus-4-6"));
            Assert.That(GetProperty(custom.Value, "permissionMode"), Is.EqualTo("bypass"));
        });
    }

    // ---------------- Message system (hook_started) ----------------

    [Test]
    public void Translate_SystemHookStartedMessage_EmitsCustomHookStarted()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-hs",
          "role": "agent",
          "parts": [
            {
              "kind": "data",
              "data": {
                "subtype": "hook_started",
                "hookId": "hook-1",
                "hookName": "SessionStart",
                "hookEvent": "SessionStart"
              },
              "metadata": { "kind": "system" }
            }
          ],
          "metadata": { "sdkMessageType": "system" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        var custom = AssertCustom(events[0], AGUICustomEventName.HookStarted);
        Assert.Multiple(() =>
        {
            Assert.That(GetProperty(custom.Value, "hookId"), Is.EqualTo("hook-1"));
            Assert.That(GetProperty(custom.Value, "hookName"), Is.EqualTo("SessionStart"));
            Assert.That(GetProperty(custom.Value, "hookEvent"), Is.EqualTo("SessionStart"));
        });
    }

    // ---------------- Message system (hook_response) ----------------

    [Test]
    public void Translate_SystemHookResponseMessage_EmitsCustomHookResponse()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-hr",
          "role": "agent",
          "parts": [
            {
              "kind": "data",
              "data": {
                "subtype": "hook_response",
                "hookId": "hook-1",
                "hookName": "SessionStart",
                "output": "session ready",
                "exitCode": 0,
                "outcome": "success"
              },
              "metadata": { "kind": "system" }
            }
          ],
          "metadata": { "sdkMessageType": "system" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        var custom = AssertCustom(events[0], AGUICustomEventName.HookResponse);
        Assert.Multiple(() =>
        {
            Assert.That(GetProperty(custom.Value, "hookId"), Is.EqualTo("hook-1"));
            Assert.That(GetProperty(custom.Value, "output"), Is.EqualTo("session ready"));
            Assert.That(GetProperty(custom.Value, "outcome"), Is.EqualTo("success"));
        });
    }

    // ---------------- StatusUpdate working ----------------

    [Test]
    public void Translate_StatusUpdateWorking_EmitsNothing()
    {
        var parsed = ParseStatusUpdate("""
        {
          "kind": "status-update",
          "taskId": "task-1",
          "contextId": "ctx-1",
          "status": { "state": "working" },
          "final": false
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Is.Empty,
            "working state is implied by RunStarted and must not fan out");
    }

    // ---------------- StatusUpdate input-required question ----------------

    [Test]
    public void Translate_StatusUpdateInputRequiredQuestion_EmitsCustomQuestionPending()
    {
        var parsed = ParseStatusUpdate("""
        {
          "kind": "status-update",
          "taskId": "task-1",
          "contextId": "ctx-1",
          "status": {
            "state": "input-required",
            "message": {
              "kind": "message",
              "messageId": "status-msg",
              "role": "agent",
              "parts": [
                {
                  "kind": "data",
                  "data": {
                    "questions": [
                      {
                        "question": "Continue?",
                        "header": "Confirm",
                        "options": [
                          { "label": "Yes", "description": "" },
                          { "label": "No",  "description": "" }
                        ],
                        "multiSelect": false
                      }
                    ]
                  },
                  "metadata": { "kind": "questions" }
                }
              ]
            }
          },
          "final": false,
          "metadata": { "inputType": "question" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        var custom = AssertCustom(events[0], AGUICustomEventName.QuestionPending);
        Assert.That(custom.Value, Is.TypeOf<PendingQuestion>(),
            "question.pending payload must be a PendingQuestion record for easy client consumption");
    }

    // ---------------- StatusUpdate input-required plan ----------------

    [Test]
    public void Translate_StatusUpdateInputRequiredPlan_EmitsCustomPlanPending()
    {
        var parsed = ParseStatusUpdate("""
        {
          "kind": "status-update",
          "taskId": "task-1",
          "contextId": "ctx-1",
          "status": {
            "state": "input-required",
            "message": {
              "kind": "message",
              "messageId": "status-msg",
              "role": "agent",
              "parts": [
                {
                  "kind": "data",
                  "data": { "plan": "1. Do thing\n2. Do other thing" },
                  "metadata": { "kind": "plan" }
                }
              ]
            }
          },
          "final": false,
          "metadata": { "inputType": "plan-approval" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        var custom = AssertCustom(events[0], AGUICustomEventName.PlanPending);
        Assert.That(custom.Value, Is.TypeOf<AGUIPlanPendingData>());
        var planData = (AGUIPlanPendingData)custom.Value;
        Assert.That(planData.PlanContent, Does.Contain("Do thing"));
    }

    // ---------------- StatusUpdate status_resumed ----------------

    [Test]
    public void Translate_StatusUpdateStatusResumed_EmitsCustomStatusResumed()
    {
        var parsed = ParseStatusUpdate("""
        {
          "kind": "status-update",
          "taskId": "task-1",
          "contextId": "ctx-1",
          "status": { "state": "working" },
          "final": false,
          "metadata": { "controlType": "status_resumed" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        AssertCustom(events[0], AGUICustomEventName.StatusResumed);
    }

    // ---------------- StatusUpdate workflow_complete ----------------

    [Test]
    public void Translate_StatusUpdateWorkflowComplete_EmitsCustomWorkflowComplete()
    {
        var parsed = ParseStatusUpdate("""
        {
          "kind": "status-update",
          "taskId": "task-1",
          "contextId": "ctx-1",
          "status": {
            "state": "completed",
            "message": {
              "kind": "message",
              "messageId": "status-msg",
              "role": "agent",
              "parts": [
                { "kind": "text", "text": "All done" },
                {
                  "kind": "data",
                  "data": { "status": "success", "outputs": { "k": "v" } },
                  "metadata": { "kind": "workflow_complete" }
                }
              ],
              "metadata": { "sdkMessageType": "workflow_complete" }
            }
          },
          "final": true
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        var custom = AssertCustom(events[0], AGUICustomEventName.WorkflowComplete);
        Assert.That(GetProperty(custom.Value, "status"), Is.EqualTo("success"));
    }

    // ---------------- StatusUpdate completed ----------------

    [Test]
    public void Translate_StatusUpdateCompleted_EmitsRunFinished()
    {
        var parsed = ParseStatusUpdate("""
        {
          "kind": "status-update",
          "taskId": "task-1",
          "contextId": "ctx-1",
          "status": {
            "state": "completed",
            "message": {
              "kind": "message",
              "messageId": "status-msg",
              "role": "agent",
              "parts": [ { "kind": "text", "text": "Done" } ]
            }
          },
          "final": true
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.TypeOf<RunFinishedEvent>());
        var run = (RunFinishedEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(run.ThreadId, Is.EqualTo(SessionId));
            Assert.That(run.RunId, Is.EqualTo(RunId));
        });
    }

    // ---------------- StatusUpdate failed ----------------

    [Test]
    public void Translate_StatusUpdateFailed_EmitsRunError()
    {
        var parsed = ParseStatusUpdate("""
        {
          "kind": "status-update",
          "taskId": "task-1",
          "contextId": "ctx-1",
          "status": {
            "state": "failed",
            "message": {
              "kind": "message",
              "messageId": "status-msg",
              "role": "agent",
              "parts": [
                { "kind": "text", "text": "Something broke" },
                {
                  "kind": "data",
                  "data": { "code": "oops" },
                  "metadata": { "kind": "error" }
                }
              ]
            }
          },
          "final": true
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0], Is.TypeOf<RunErrorEvent>());
        var err = (RunErrorEvent)events[0];
        Assert.Multiple(() =>
        {
            Assert.That(err.Message, Is.EqualTo("Something broke"));
            Assert.That(err.Code, Is.EqualTo("oops"));
        });
    }

    // ---------------- Multi-block message ----------------

    [Test]
    public void Translate_AgentMessageWithMixedBlocks_FansOutInOrder()
    {
        // thinking → text → tool_use
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-mix",
          "role": "agent",
          "parts": [
            {
              "kind": "data",
              "data": { "thinking": "reasoning..." },
              "metadata": { "kind": "thinking" }
            },
            { "kind": "text", "text": "Here's my plan" },
            {
              "kind": "data",
              "data": { "toolUseId": "t1", "toolName": "Read", "input": { "path": "/etc/hosts" } },
              "metadata": { "kind": "tool_use" }
            }
          ],
          "metadata": { "sdkMessageType": "assistant" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        // thinking (1) + text start/content/end (3) + tool_use start/args/end (3) = 7
        Assert.That(events, Has.Count.EqualTo(7));
        Assert.That(events[0], Is.TypeOf<CustomEvent>());
        Assert.That(events[1], Is.TypeOf<TextMessageStartEvent>());
        Assert.That(events[2], Is.TypeOf<TextMessageContentEvent>());
        Assert.That(events[3], Is.TypeOf<TextMessageEndEvent>());
        Assert.That(events[4], Is.TypeOf<ToolCallStartEvent>());
        Assert.That(events[5], Is.TypeOf<ToolCallArgsEvent>());
        Assert.That(events[6], Is.TypeOf<ToolCallEndEvent>());
    }

    // ---------------- 3.5 property test ----------------

    [Test]
    public void Translate_AnyA2AEvent_NeverThrows()
    {
        // Fuzz corners: empty strings, missing metadata, weird states.
        var cases = new (string Kind, string Json)[]
        {
            ("task", """{ "kind":"task", "id":"", "contextId":"", "status":{"state":"unknown"} }"""),
            ("message", """{ "kind":"message", "role":"agent", "parts":[], "messageId":"x" }"""),
            ("message", """{ "kind":"message", "role":"agent", "parts":[{"kind":"data","data":{},"metadata":{"kind":"mystery"}}], "messageId":"y" }"""),
            ("status-update", """{ "kind":"status-update", "taskId":"t", "contextId":"c", "status":{"state":"auth-required"}, "final":false }"""),
            ("status-update", """{ "kind":"status-update", "taskId":"t", "contextId":"c", "status":{"state":"canceled"}, "final":true }"""),
        };

        foreach (var (kind, json) in cases)
        {
            var parsed = A2AMessageParser.ParseSseEvent(kind, json);
            Assert.That(parsed, Is.Not.Null, $"fixture {kind} must parse");

            Assert.DoesNotThrow(() =>
            {
                var events = _translator.Translate(parsed!, _ctx).ToList();
                // All emitted events are valid AG-UI types (no nulls).
                Assert.That(events, Has.All.Not.Null);
            }, $"Translate must not throw for fixture [{kind}] {json}");
        }
    }

    [Test]
    public void Translate_UnknownMessageDataKind_EmitsRawCustom()
    {
        var parsed = ParseMessage("""
        {
          "kind": "message",
          "messageId": "msg-x",
          "role": "agent",
          "parts": [
            {
              "kind": "data",
              "data": { "wat": true },
              "metadata": { "kind": "mystery_block" }
            }
          ],
          "metadata": { "sdkMessageType": "assistant" }
        }
        """);

        var events = _translator.Translate(parsed, _ctx).ToList();

        Assert.That(events, Has.Count.EqualTo(1));
        AssertCustom(events[0], AGUICustomEventName.Raw);
    }

    // ---------------- Helpers ----------------

    private static ParsedA2AEvent ParseTask(string json)
    {
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Task, json);
        Assert.That(parsed, Is.Not.Null, $"Failed to parse task fixture: {json}");
        return parsed!;
    }

    private static ParsedA2AEvent ParseMessage(string json)
    {
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.Message, json);
        Assert.That(parsed, Is.Not.Null, $"Failed to parse message fixture: {json}");
        return parsed!;
    }

    private static ParsedA2AEvent ParseStatusUpdate(string json)
    {
        var parsed = A2AMessageParser.ParseSseEvent(HomespunA2AEventKind.StatusUpdate, json);
        Assert.That(parsed, Is.Not.Null, $"Failed to parse status-update fixture: {json}");
        return parsed!;
    }

    private static CustomEvent AssertCustom(AGUIBaseEvent evt, string expectedName)
    {
        Assert.That(evt, Is.TypeOf<CustomEvent>(), $"expected Custom event, got {evt.GetType().Name}");
        var custom = (CustomEvent)evt;
        Assert.That(custom.Name, Is.EqualTo(expectedName),
            $"custom event name mismatch: got '{custom.Name}', expected '{expectedName}'");
        return custom;
    }

    /// <summary>
    /// Reads a property from an anonymous-object payload by round-tripping through JSON so
    /// the assertion works regardless of how the translator constructs the Custom value.
    /// </summary>
    private static object? GetProperty(object obj, string name)
    {
        var json = JsonSerializer.Serialize(obj);
        var element = JsonDocument.Parse(json).RootElement;
        if (!element.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.TryGetInt64(out var l) ? l : prop.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => prop.GetRawText(),
        };
    }
}

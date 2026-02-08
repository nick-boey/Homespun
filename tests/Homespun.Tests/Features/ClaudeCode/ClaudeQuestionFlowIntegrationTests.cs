using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Integration tests that start a real Claude CLI process to verify the
/// MCP-based ask_user tool flow end-to-end.
/// Uses ClaudeSdkClient directly for full protocol visibility.
///
/// These require the Claude CLI to be installed and authenticated.
///
/// Run with: dotnet test tests/Homespun.Tests --filter "TestCategory=Integration" -- NUnit.DefaultTestAssemblySettings.ExplicitPolicy=Run
/// </summary>
[TestFixture]
[Explicit("Requires Claude CLI installed and authenticated")]
[Category("Integration")]
public class ClaudeQuestionFlowIntegrationTests
{
    [Test]
    [CancelAfter(120_000)]
    public async Task McpAskUser_ReceivesQuestionsViaHandler()
    {
        // Arrange - track questions received by the MCP handler
        var receivedQuestions = new List<List<AskUserQuestionInput>>();
        var questionTcs = new TaskCompletionSource<bool>();

        var askUserFunction = AskUserQuestionFunction.Create(async (questions, ct) =>
        {
            receivedQuestions.Add(questions);
            TestContext.Out.WriteLine($"[MCP ask_user] Received {questions.Count} question(s):");
            foreach (var q in questions)
            {
                TestContext.Out.WriteLine($"  Q: {q.Question} (header={q.Header}, options={q.Options?.Count ?? 0})");
            }

            // Auto-answer with first option for each question
            var answers = new Dictionary<string, string>();
            foreach (var q in questions)
            {
                var answer = q.Options?.FirstOrDefault()?.Label ?? "Test answer";
                answers[q.Question] = answer;
                TestContext.Out.WriteLine($"  -> Answering: {answer}");
            }

            // Signal that we received at least one question
            questionTcs.TrySetResult(true);

            return answers;
        });

        var stderrOutput = new List<string>();
        var converter = new AIFunctionMcpConverter([askUserFunction], "homespun");

        var options = new ClaudeAgentOptions
        {
            PermissionMode = PermissionMode.Default,
            PermissionPromptToolName = "stdio",
            Model = "sonnet",
            Cwd = Path.GetTempPath(),
            IncludePartialMessages = true,
            DisallowedTools = ["AskUserQuestion", "Bash", "Write", "Edit", "NotebookEdit"],
            McpServers = new Dictionary<string, object>
            {
                ["homespun"] = converter.CreateMcpServerConfig()
            },
            Stderr = line => stderrOutput.Add(line)
        };

        await using var client = new ClaudeSdkClient(options);
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        // Connect and send prompt
        await client.ConnectAsync(null, cancellationToken);

        var prompt = "I am testing a UI for Claude Code. " +
                     "Could you please ask me a few example questions using the mcp__homespun__ask_user tool " +
                     "and put together a plan based on my responses? " +
                     "It does not have to be about anything in particular, " +
                     "so don't try to read any files - just make something up.";

        await client.QueryAsync(prompt, cancellationToken: cancellationToken);

        // Act - process messages
        var sessionStarted = false;
        var receivedResult = false;
        var controlRequestCount = 0;
        var toolUseNames = new List<string>();

        TestContext.Out.WriteLine("\n=== MCP ask_user Integration Test ===\n");

        await foreach (var msg in client.ReceiveMessagesAsync(cancellationToken))
        {
            switch (msg)
            {
                case ControlRequest controlRequest:
                {
                    controlRequestCount++;
                    var requestId = controlRequest.RequestId;
                    var toolName = GetToolName(controlRequest);
                    TestContext.Out.WriteLine($"[ControlRequest] tool_name={toolName}, requestId={requestId}");

                    if (string.IsNullOrEmpty(requestId))
                        continue;

                    // Auto-approve all control requests
                    var input = GetInput(controlRequest);
                    var updatedInput = new Dictionary<string, object>();
                    if (input != null)
                    {
                        foreach (var kvp in input)
                            updatedInput[kvp.Key] = kvp.Value;
                    }

                    await client.SendControlResponseAsync(requestId, "allow", updatedInput, cancellationToken: cancellationToken);
                    TestContext.Out.WriteLine($"  -> Auto-approved {toolName}");
                    break;
                }

                case AssistantMessage assistantMsg:
                {
                    sessionStarted = true;
                    foreach (var block in assistantMsg.Content)
                    {
                        if (block is ToolUseBlock toolUse)
                        {
                            toolUseNames.Add(toolUse.Name);
                            TestContext.Out.WriteLine($"[ToolUse] name={toolUse.Name}, id={toolUse.Id}");
                        }
                        else if (block is TextBlock text)
                        {
                            var preview = text.Text.Length > 200 ? text.Text[..200] + "..." : text.Text;
                            TestContext.Out.WriteLine($"[Text] {preview}");
                        }
                    }
                    break;
                }

                case ResultMessage resultMsg:
                    receivedResult = true;
                    sessionStarted = true;
                    TestContext.Out.WriteLine($"[Result] cost=${resultMsg.TotalCostUsd:F4}, duration={resultMsg.DurationMs}ms, turns={resultMsg.NumTurns}");
                    break;

                case StreamEvent:
                    sessionStarted = true;
                    break;
            }

            if (msg is ResultMessage)
                break;
        }

        // Check stderr for errors
        var zodErrors = stderrOutput.Where(l => l.Contains("Zod", StringComparison.OrdinalIgnoreCase)).ToList();

        // Print summary
        TestContext.Out.WriteLine("\n--- Summary ---");
        TestContext.Out.WriteLine($"  Session started: {sessionStarted}");
        TestContext.Out.WriteLine($"  Control requests: {controlRequestCount}");
        TestContext.Out.WriteLine($"  ToolUse names: [{string.Join(", ", toolUseNames)}]");
        TestContext.Out.WriteLine($"  MCP ask_user invocations (via handler): {receivedQuestions.Count}");
        TestContext.Out.WriteLine($"  Result received: {receivedResult}");
        TestContext.Out.WriteLine($"  Stderr lines: {stderrOutput.Count}");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(sessionStarted, Is.True, "Should receive messages from the session");
            Assert.That(receivedResult, Is.True, "Should receive a result event");
            Assert.That(zodErrors, Is.Empty, "Should not have any Zod validation errors");
            Assert.That(receivedQuestions, Is.Not.Empty,
                "MCP ask_user handler should receive at least one question set");
        });

        // Verify question structure
        foreach (var questionSet in receivedQuestions)
        {
            foreach (var q in questionSet)
            {
                Assert.That(q.Question, Is.Not.Null.And.Not.Empty, "Each question should have text");
            }
        }
    }

    private static string? GetToolName(ControlRequest controlRequest)
    {
        if (controlRequest.Data == null) return null;
        if (controlRequest.Data.TryGetValue("tool_name", out var obj) && obj is JsonElement el)
            return el.GetString();
        return null;
    }

    private static Dictionary<string, JsonElement>? GetInput(ControlRequest controlRequest)
    {
        if (controlRequest.Data == null) return null;
        if (controlRequest.Data.TryGetValue("input", out var obj) && obj is JsonElement el)
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(el.GetRawText());
        return null;
    }
}

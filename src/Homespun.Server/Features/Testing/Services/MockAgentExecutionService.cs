using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Keywords that trigger specific mock response patterns.
/// </summary>
public enum MockKeyword
{
    Think,
    Tool,
    Question,
    Plan
}

/// <summary>
/// Mock implementation of IAgentExecutionService for testing.
/// Provides keyword-based response assembly with realistic mock responses.
/// </summary>
public class MockAgentExecutionService : IAgentExecutionService
{
    private readonly ILogger<MockAgentExecutionService> _logger;
    private readonly ConcurrentDictionary<string, MockSession> _sessions = new();
    private int _toolUseCounter;

    private class MockSession
    {
        public required string SessionId { get; init; }
        public required string WorkingDirectory { get; init; }
        public required SessionMode Mode { get; init; }
        public required string Model { get; init; }
        public required DateTime CreatedAt { get; init; }
        public string? ConversationId { get; set; }
        public DateTime LastActivityAt { get; set; }

        // Channel for streaming messages back to consumer
        public Channel<SdkMessage>? MessageChannel { get; set; }

        // Continuation callbacks for question/plan flow
        public Func<Dictionary<string, string>, Task>? QuestionContinuation { get; set; }
        public Func<bool, bool, string?, Task>? PlanContinuation { get; set; }
    }

    public MockAgentExecutionService(ILogger<MockAgentExecutionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses keywords from a message to determine which response patterns to include.
    /// </summary>
    public static HashSet<MockKeyword> ParseKeywords(string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        var keywords = new HashSet<MockKeyword>();

        if (lower.Contains("think")) keywords.Add(MockKeyword.Think);
        if (lower.Contains("tool")) keywords.Add(MockKeyword.Tool);
        if (lower.Contains("question")) keywords.Add(MockKeyword.Question);
        if (lower.Contains("plan")) keywords.Add(MockKeyword.Plan);

        return keywords;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        _logger.LogInformation("[Mock] Starting session {SessionId} in directory {WorkingDirectory}",
            sessionId, request.WorkingDirectory);

        var session = new MockSession
        {
            SessionId = sessionId,
            WorkingDirectory = request.WorkingDirectory,
            Mode = request.Mode,
            Model = request.Model,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        _sessions[sessionId] = session;

        // Yield synthetic session_started message
        yield return new SdkSystemMessage(sessionId, null, "session_started", request.Model, null);

        // Simulate brief processing delay
        await Task.Delay(100, cancellationToken);

        // Yield a synthetic assistant response
        var responseContent = new List<SdkContentBlock>
        {
            new SdkTextBlock("[Mock] Session started successfully.")
        };
        var apiMessage = new SdkApiMessage("assistant", responseContent);
        yield return new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), apiMessage, null);

        // Yield synthetic result
        yield return new SdkResultMessage(
            SessionId: sessionId,
            Uuid: Guid.NewGuid().ToString(),
            Subtype: "success",
            DurationMs: 100,
            DurationApiMs: 50,
            IsError: false,
            NumTurns: 1,
            TotalCostUsd: 0m,
            Result: null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> SendMessageAsync(
        AgentMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] SendMessage to session {SessionId}: {Message}",
            request.SessionId, request.Message.Length > 50 ? request.Message[..50] + "..." : request.Message);

        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            _logger.LogWarning("[Mock] Session {SessionId} not found", request.SessionId);
            yield break;
        }

        session.LastActivityAt = DateTime.UtcNow;

        // Parse keywords from the message
        var keywords = ParseKeywords(request.Message);

        // Create channel for this response sequence
        var channel = Channel.CreateUnbounded<SdkMessage>();
        session.MessageChannel = channel;

        // Start background task to emit messages
        _ = Task.Run(async () =>
        {
            try
            {
                await EmitResponseSequence(session, keywords, channel.Writer, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Mock] Error in response sequence for session {SessionId}", session.SessionId);
                channel.Writer.TryComplete(ex);
            }
        }, cancellationToken);

        // Yield messages from channel as they arrive
        await foreach (var msg in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return msg;
        }
    }

    /// <summary>
    /// Continues reading messages from the channel after an answer/approval.
    /// Used by tests to read continuation messages.
    /// </summary>
    public async IAsyncEnumerable<SdkMessage> ContinueReadingMessages(
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            yield break;
        }

        if (session.MessageChannel == null)
        {
            yield break;
        }

        await foreach (var msg in session.MessageChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return msg;
        }
    }

    private async Task EmitResponseSequence(
        MockSession session,
        HashSet<MockKeyword> keywords,
        ChannelWriter<SdkMessage> writer,
        CancellationToken ct)
    {
        var sessionId = session.SessionId;

        // Order per issue spec: think → tool → question → plan

        // 1. Think sequence (if keyword present) - comes first per spec
        if (keywords.Contains(MockKeyword.Think))
        {
            await EmitThinkingSequence(sessionId, writer, ct);
        }

        // 2. Tool sequence (if keyword present)
        if (keywords.Contains(MockKeyword.Tool))
        {
            await EmitToolSequence(sessionId, writer, ct);
        }

        // 3. Question sequence (if keyword present)
        if (keywords.Contains(MockKeyword.Question))
        {
            // Question continuation will handle plan if both keywords present
            await EmitQuestionSequence(session, keywords, writer, ct);
            return; // Don't close channel yet - continuation will close it
        }

        // 4. Plan sequence (if keyword present and not already triggered by question)
        if (keywords.Contains(MockKeyword.Plan))
        {
            await EmitPlanSequence(session, writer, ct);
            return; // Plan continuation will close channel
        }

        // No question or plan - emit result and close
        await EmitResultMessage(sessionId, writer);
        writer.Complete();
    }

    private async Task EmitThinkingSequence(
        string sessionId,
        ChannelWriter<SdkMessage> writer,
        CancellationToken ct)
    {
        // Emit thinking block
        var thinkingContent = new List<SdkContentBlock>
        {
            new SdkThinkingBlock("I'm analyzing the prompt and considering the best approach. Let me think through this step by step...")
        };
        var thinkingMessage = new SdkApiMessage("assistant", thinkingContent);
        await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), thinkingMessage, null), ct);
        await Task.Delay(100, ct);

        // Emit text response after thinking
        var textContent = new List<SdkContentBlock>
        {
            new SdkTextBlock("I have thought about the message and here is my response based on my analysis.")
        };
        var textMessage = new SdkApiMessage("assistant", textContent);
        await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), textMessage, null), ct);
        await Task.Delay(100, ct);
    }

    private async Task EmitToolSequence(
        string sessionId,
        ChannelWriter<SdkMessage> writer,
        CancellationToken ct)
    {
        // Read tool use
        var readToolUseId = $"toolu_mock_{Interlocked.Increment(ref _toolUseCounter)}";
        var readInput = JsonDocument.Parse("{\"file_path\":\"test.txt\"}").RootElement;
        var readToolContent = new List<SdkContentBlock>
        {
            new SdkToolUseBlock(readToolUseId, "Read", readInput)
        };
        var readToolMessage = new SdkApiMessage("assistant", readToolContent);
        await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), readToolMessage, null), ct);
        await Task.Delay(50, ct);

        // Read tool result
        var readResultContent = new List<SdkContentBlock>
        {
            new SdkToolResultBlock(readToolUseId, JsonDocument.Parse("\"File content: Hello, World!\"").RootElement, false)
        };
        var readResultMessage = new SdkApiMessage("user", readResultContent);
        await writer.WriteAsync(new SdkUserMessage(sessionId, Guid.NewGuid().ToString(), readResultMessage, readToolUseId), ct);
        await Task.Delay(50, ct);

        // Write tool use
        var writeToolUseId = $"toolu_mock_{Interlocked.Increment(ref _toolUseCounter)}";
        var writeInput = JsonDocument.Parse("{\"file_path\":\"output.txt\",\"content\":\"Modified content\"}").RootElement;
        var writeToolContent = new List<SdkContentBlock>
        {
            new SdkToolUseBlock(writeToolUseId, "Write", writeInput)
        };
        var writeToolMessage = new SdkApiMessage("assistant", writeToolContent);
        await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), writeToolMessage, null), ct);
        await Task.Delay(50, ct);

        // Write tool result
        var writeResultContent = new List<SdkContentBlock>
        {
            new SdkToolResultBlock(writeToolUseId, JsonDocument.Parse("\"File written successfully\"").RootElement, false)
        };
        var writeResultMessage = new SdkApiMessage("user", writeResultContent);
        await writer.WriteAsync(new SdkUserMessage(sessionId, Guid.NewGuid().ToString(), writeResultMessage, writeToolUseId), ct);
        await Task.Delay(50, ct);

        // Final message after tool use
        var textContent = new List<SdkContentBlock>
        {
            new SdkTextBlock("I have used the Read and Write tools to read and modify the file.")
        };
        var textMessage = new SdkApiMessage("assistant", textContent);
        await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), textMessage, null), ct);
        await Task.Delay(50, ct);
    }

    private async Task EmitQuestionSequence(
        MockSession session,
        HashSet<MockKeyword> keywords,
        ChannelWriter<SdkMessage> writer,
        CancellationToken ct)
    {
        var sessionId = session.SessionId;

        // Emit intro message
        var introContent = new List<SdkContentBlock>
        {
            new SdkTextBlock("I have some questions for you before I proceed with the task.")
        };
        var introMessage = new SdkApiMessage("assistant", introContent);
        await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), introMessage, null), ct);
        await Task.Delay(100, ct);

        // Build questions JSON
        var questionsJson = BuildQuestionsJson();

        // Store continuation BEFORE emitting question pending - ensures it's available
        // when the consumer receives the pending message and calls AnswerQuestionAsync
        session.QuestionContinuation = async (answers) =>
        {
            try
            {
                // Emit response acknowledging answers
                var answerSummary = FormatAnswers(answers);
                var answerContent = new List<SdkContentBlock>
                {
                    new SdkTextBlock($"Thank you for your answers:\n{answerSummary}\n\nI will now proceed with the task.")
                };
                var answerMessage = new SdkApiMessage("assistant", answerContent);
                await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), answerMessage, null), ct);
                await Task.Delay(100, ct);

                // Question flow always creates a plan per spec
                await EmitPlanSequence(session, writer, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Mock] Error in question continuation for session {SessionId}", sessionId);
                writer.TryComplete(ex);
            }
        };

        // Emit question pending AFTER continuation is set - ensures it's available
        // when the consumer receives the pending message and calls AnswerQuestionAsync
        await writer.WriteAsync(new SdkQuestionPendingMessage(sessionId, questionsJson), ct);
    }

    private async Task EmitPlanSequence(
        MockSession session,
        ChannelWriter<SdkMessage> writer,
        CancellationToken ct)
    {
        var sessionId = session.SessionId;

        // Emit intro message
        var introContent = new List<SdkContentBlock>
        {
            new SdkTextBlock("I am creating a plan for your approval.")
        };
        var introMessage = new SdkApiMessage("assistant", introContent);
        await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), introMessage, null), ct);
        await Task.Delay(100, ct);

        // Build plan JSON
        var planJson = BuildPlanJson();

        // Store continuation BEFORE emitting plan pending - ensures it's available
        // when the consumer receives the pending message and calls ApprovePlanAsync
        session.PlanContinuation = async (approved, keepContext, feedback) =>
        {
            try
            {
                if (approved)
                {
                    var approvalContent = new List<SdkContentBlock>
                    {
                        new SdkTextBlock(keepContext
                            ? "Plan approved. I will now execute the plan while keeping the current context."
                            : "Plan approved. I will now execute the plan with a fresh context.")
                    };
                    var approvalMessage = new SdkApiMessage("assistant", approvalContent);
                    await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), approvalMessage, null), ct);
                }
                else
                {
                    var rejectionContent = new List<SdkContentBlock>
                    {
                        new SdkTextBlock($"Plan was not approved.{(string.IsNullOrEmpty(feedback) ? "" : $" Feedback: {feedback}")}")
                    };
                    var rejectionMessage = new SdkApiMessage("assistant", rejectionContent);
                    await writer.WriteAsync(new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), rejectionMessage, null), ct);
                }

                await Task.Delay(100, ct);

                // Emit result and close
                await EmitResultMessage(sessionId, writer);
                writer.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Mock] Error in plan continuation for session {SessionId}", sessionId);
                writer.TryComplete(ex);
            }
        };

        // Emit plan pending AFTER continuation is set - ensures it's available
        // when the consumer receives the pending message and calls ApprovePlanAsync
        await writer.WriteAsync(new SdkPlanPendingMessage(sessionId, planJson), ct);
    }

    private async Task EmitResultMessage(string sessionId, ChannelWriter<SdkMessage> writer)
    {
        await writer.WriteAsync(new SdkResultMessage(
            SessionId: sessionId,
            Uuid: Guid.NewGuid().ToString(),
            Subtype: "success",
            DurationMs: 500,
            DurationApiMs: 300,
            IsError: false,
            NumTurns: 1,
            TotalCostUsd: 0.01m,
            Result: null), default);
    }

    private static string BuildQuestionsJson()
    {
        var questions = new
        {
            questions = new object[]
            {
                new
                {
                    question = "What is your favorite color?",
                    header = "Color",
                    options = new object[]
                    {
                        new { label = "Red", description = "A warm, vibrant color" },
                        new { label = "Blue", description = "A cool, calming color" },
                        new { label = "Green", description = "A natural, refreshing color" },
                        new { label = "Yellow", description = "A bright, cheerful color" }
                    },
                    multiSelect = false
                },
                new
                {
                    question = "Which features do you want to enable?",
                    header = "Features",
                    options = new object[]
                    {
                        new { label = "Feature1", description = "Enable the first feature" },
                        new { label = "Feature2", description = "Enable the second feature" },
                        new { label = "Feature3", description = "Enable the third feature" }
                    },
                    multiSelect = true
                }
            }
        };

        return JsonSerializer.Serialize(questions, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static string BuildPlanJson()
    {
        var plan = new
        {
            plan = @"## Implementation Plan

### Overview
This plan outlines the steps to complete the requested task.

### Steps

1. **Analyze Requirements**
   - Review the current codebase
   - Identify necessary changes

2. **Implement Changes**
   - Create new files if needed
   - Modify existing code
   - Add proper error handling

3. **Testing**
   - Write unit tests
   - Run existing tests
   - Verify functionality

4. **Documentation**
   - Update README if needed
   - Add code comments

### Estimated Effort
Medium complexity, approximately 2-4 hours of work."
        };

        return JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static string FormatAnswers(Dictionary<string, string> answers)
    {
        return string.Join("\n", answers.Select(kv => $"- {kv.Key}: {kv.Value}"));
    }

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, bool forceStopContainer = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] Stopping session {SessionId}", sessionId);
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] Interrupting session {SessionId}", sessionId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<AgentSessionStatus?>(new AgentSessionStatus(
                session.SessionId,
                session.WorkingDirectory,
                session.Mode,
                session.Model,
                session.ConversationId,
                session.CreatedAt,
                session.LastActivityAt));
        }

        return Task.FromResult<AgentSessionStatus?>(null);
    }

    /// <inheritdoc />
    public Task<string?> ReadFileFromAgentAsync(string sessionId, string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] ReadFile from session {SessionId}: {FilePath}", sessionId, filePath);
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var statuses = _sessions.Values.Select(session => new AgentSessionStatus(
            session.SessionId,
            session.WorkingDirectory,
            session.Mode,
            session.Model,
            session.ConversationId,
            session.CreatedAt,
            session.LastActivityAt
        )).ToList().AsReadOnly();

        return Task.FromResult<IReadOnlyList<AgentSessionStatus>>(statuses);
    }

    /// <inheritdoc />
    public Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] CleanupOrphanedContainers (no-op)");
        return Task.FromResult(0);
    }

    /// <inheritdoc />
    public Task<bool> AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] AnswerQuestion for session {SessionId}", sessionId);

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogWarning("[Mock] Session {SessionId} not found for AnswerQuestion", sessionId);
            return Task.FromResult(false);
        }

        if (session.QuestionContinuation == null)
        {
            _logger.LogWarning("[Mock] No question continuation for session {SessionId}", sessionId);
            return Task.FromResult(false);
        }

        // Invoke the continuation with answers - this writes to the channel
        var continuation = session.QuestionContinuation;
        session.QuestionContinuation = null;

        // Fire and forget - the continuation will write to the channel
        _ = Task.Run(async () =>
        {
            try
            {
                await continuation(answers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Mock] Error executing question continuation for session {SessionId}", sessionId);
            }
        }, cancellationToken);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] ApprovePlan for session {SessionId}: approved={Approved}", sessionId, approved);

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogWarning("[Mock] Session {SessionId} not found for ApprovePlan", sessionId);
            return Task.FromResult(false);
        }

        if (session.PlanContinuation == null)
        {
            _logger.LogWarning("[Mock] No plan continuation for session {SessionId}", sessionId);
            return Task.FromResult(false);
        }

        // Invoke the continuation - this writes final messages and closes channel
        var continuation = session.PlanContinuation;
        session.PlanContinuation = null;

        // Fire and forget - the continuation will write to the channel
        _ = Task.Run(async () =>
        {
            try
            {
                await continuation(approved, keepContext, feedback);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Mock] Error executing plan continuation for session {SessionId}", sessionId);
            }
        }, cancellationToken);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<CloneContainerState?> GetCloneContainerStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] GetCloneContainerState for {WorkingDirectory}", workingDirectory);
        return Task.FromResult<CloneContainerState?>(null);
    }

    /// <inheritdoc />
    public Task TerminateCloneSessionAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] TerminateCloneSession for {WorkingDirectory}", workingDirectory);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ListContainers");
        return Task.FromResult<IReadOnlyList<ContainerInfo>>(Array.Empty<ContainerInfo>());
    }

    /// <inheritdoc />
    public Task<bool> StopContainerByIdAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] StopContainerById {ContainerId}", containerId);
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<ContainerRestartResult?> RestartContainerAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] RestartContainer for session {SessionId}", sessionId);
        return Task.FromResult<ContainerRestartResult?>(null);
    }

    /// <inheritdoc />
    public Task<bool> SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] SetSessionMode for session {SessionId}: {Mode}", sessionId, mode);

        if (_sessions.TryGetValue(sessionId, out _))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] SetSessionModel for session {SessionId}: {Model}", sessionId, model);

        if (_sessions.TryGetValue(sessionId, out _))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}

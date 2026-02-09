using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IClaudeSessionService that simulates Claude Code sessions.
/// Includes realistic tool use and tool result blocks for testing rich tool display.
/// </summary>
public class MockClaudeSessionService : IClaudeSessionService
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly IToolResultParser _toolResultParser;
    private readonly IHubContext<ClaudeCodeHub> _hubContext;
    private readonly ILogger<MockClaudeSessionService> _logger;
    private int _toolUseCounter = 0;

    public MockClaudeSessionService(
        IClaudeSessionStore sessionStore,
        IToolResultParser toolResultParser,
        IHubContext<ClaudeCodeHub> hubContext,
        ILogger<MockClaudeSessionService> logger)
    {
        _sessionStore = sessionStore;
        _toolResultParser = toolResultParser;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task<ClaudeSession> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StartSession for entity {EntityId} in project {ProjectId}, mode: {Mode}",
            entityId, projectId, mode);

        var session = new ClaudeSession
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Mode = mode,
            Model = model,
            SystemPrompt = systemPrompt,
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        // Add an initial assistant message
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = $"[Mock Session] Ready to help with your {mode.ToString().ToLower()} task. " +
                           "This is a mock session - no actual Claude API calls will be made."
                }
            ],
            CreatedAt = DateTime.UtcNow
        });

        _sessionStore.Add(session);

        return Task.FromResult(session);
    }

    public async Task SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        await SendMessageAsync(sessionId, message, PermissionMode.Default, cancellationToken);
    }

    public Task SendMessageAsync(
        string sessionId,
        string message,
        PermissionMode permissionMode,
        CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(sessionId, message, permissionMode, null, cancellationToken);
    }

    public async Task SendMessageAsync(
        string sessionId,
        string message,
        PermissionMode permissionMode,
        string? model,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] SendMessage to session {SessionId} with model {Model}: {Message}", sessionId, model ?? "default", message);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        // Add user message
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = message
                }
            ],
            CreatedAt = DateTime.UtcNow
        });

        session.Status = ClaudeSessionStatus.Running;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);

        // Simulate processing delay
        await Task.Delay(300, cancellationToken);

        // Generate mock response with tool use/results based on message content
        var (assistantContent, toolResultMessages) = GenerateMockResponseWithTools(session.Id, message, session.Mode);

        // Add assistant message with tool use blocks
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.Assistant,
            Content = assistantContent,
            CreatedAt = DateTime.UtcNow
        });

        // Add tool result messages (role=User with ToolResult content)
        foreach (var toolResultMessage in toolResultMessages)
        {
            session.Messages.Add(toolResultMessage);
        }

        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.LastActivityAt = DateTime.UtcNow;
        session.TotalCostUsd += 0.01m; // Mock cost
        session.TotalDurationMs += 500;
        _sessionStore.Update(session);
    }

    public Task ClearContextAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ClearContext for session {SessionId}", sessionId);

        var session = _sessionStore.GetById(sessionId);
        if (session != null)
        {
            // Don't add a context clear marker here - the ContextCleared broadcast
            // will trigger the client-side handler to add it. In mock mode, the session
            // object is shared between server and client, so adding here would cause duplicates.
            session.LastActivityAt = DateTime.UtcNow;
            _sessionStore.Update(session);
        }

        return Task.CompletedTask;
    }

    public async Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ExecutePlan for session {SessionId}, clearContext={ClearContext}", sessionId, clearContext);

        var session = _sessionStore.GetById(sessionId);
        if (session == null || string.IsNullOrEmpty(session.PlanContent))
        {
            _logger.LogWarning("[Mock] Cannot execute plan: session {SessionId} not found or no plan content", sessionId);
            return;
        }

        if (clearContext)
        {
            await ClearContextAsync(sessionId, cancellationToken);
            await _hubContext.BroadcastContextCleared(sessionId);
        }

        // Update status to Running and broadcast
        session.Status = ClaudeSessionStatus.Running;
        _sessionStore.Update(session);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        // Add user message with the plan content
        var executionMessage = $"Please proceed with the implementation of {session.PlanFilePath ?? "the plan"}.\n\n{session.PlanContent}";
        var userMessage = new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = executionMessage
                }
            ],
            CreatedAt = DateTime.UtcNow
        };
        session.Messages.Add(userMessage);
        await _hubContext.BroadcastMessageReceived(sessionId, userMessage);

        // Simulate processing delay
        await Task.Delay(300, cancellationToken);

        // Add mock response
        var assistantMessage = new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = "[Mock Response] Plan execution initiated. In a real session, I would now implement the plan."
                }
            ],
            CreatedAt = DateTime.UtcNow
        };
        session.Messages.Add(assistantMessage);
        await _hubContext.BroadcastMessageReceived(sessionId, assistantMessage);

        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);
    }

    public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StopSession {SessionId}", sessionId);

        var session = _sessionStore.GetById(sessionId);
        if (session != null)
        {
            session.Status = ClaudeSessionStatus.Stopped;
            session.LastActivityAt = DateTime.UtcNow;
            _sessionStore.Update(session);
        }

        return Task.CompletedTask;
    }

    public async Task<int> StopAllSessionsForEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StopAllSessionsForEntity {EntityId}", entityId);

        var sessions = _sessionStore.GetAllByEntityId(entityId);
        var stoppedCount = 0;

        foreach (var session in sessions)
        {
            await StopSessionAsync(session.Id, cancellationToken);
            stoppedCount++;
        }

        return stoppedCount;
    }

    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] InterruptSession {SessionId}", sessionId);

        var session = _sessionStore.GetById(sessionId);
        if (session != null)
        {
            session.Status = ClaudeSessionStatus.WaitingForInput;
            session.PendingQuestion = null;
            session.LastActivityAt = DateTime.UtcNow;
            _sessionStore.Update(session);
        }

        return Task.CompletedTask;
    }

    public ClaudeSession? GetSession(string sessionId)
    {
        return _sessionStore.GetById(sessionId);
    }

    public ClaudeSession? GetSessionByEntityId(string entityId)
    {
        return _sessionStore.GetByEntityId(entityId);
    }

    public IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId)
    {
        return _sessionStore.GetByProjectId(projectId);
    }

    public IReadOnlyList<ClaudeSession> GetAllSessions()
    {
        return _sessionStore.GetAll();
    }

    public Task<ClaudeSession> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ResumeSession {SessionId} for entity {EntityId}", sessionId, entityId);

        var session = new ClaudeSession
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Mode = SessionMode.Build,
            Model = "opus",
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        // Add an initial message indicating this is a resumed session
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = $"[Mock Resumed Session] Resumed from session {sessionId}. " +
                           "This is a mock session - no actual Claude API calls will be made."
                }
            ],
            CreatedAt = DateTime.UtcNow
        });

        _sessionStore.Add(session);

        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] GetResumableSessions for entity {EntityId}", entityId);

        // Return a mock resumable session
        var sessions = new List<ResumableSession>
        {
            new ResumableSession(
                SessionId: Guid.NewGuid().ToString(),
                LastActivityAt: DateTime.UtcNow.AddHours(-1),
                Mode: SessionMode.Build,
                Model: "opus",
                MessageCount: 5
            )
        };

        return Task.FromResult<IReadOnlyList<ResumableSession>>(sessions);
    }

    public async Task AnswerQuestionAsync(
        string sessionId,
        Dictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] AnswerQuestion for session {SessionId} with {AnswerCount} answers",
            sessionId, answers.Count);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        // Clear pending question
        session.PendingQuestion = null;
        session.Status = ClaudeSessionStatus.Running;
        _sessionStore.Update(session);

        // Simulate processing the answer
        await Task.Delay(300, cancellationToken);

        // Format the answers
        var formattedAnswers = string.Join("\n", answers.Select(a => $"- {a.Key}: {a.Value}"));

        // Add mock response acknowledging the answers
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.Assistant,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = $"""
                        [Mock Response]

                        Thank you for answering my questions. Here's what you said:

                        {formattedAnswers}

                        I'll proceed with these preferences in mind. (This is a mock session - no actual processing will occur.)
                        """
                }
            ],
            CreatedAt = DateTime.UtcNow
        });

        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);
    }

    public Task<IReadOnlyList<ClaudeMessage>> GetCachedMessagesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] GetCachedMessages for session {SessionId}", sessionId);

        // Return the in-memory messages for the session as "cached" messages
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            return Task.FromResult<IReadOnlyList<ClaudeMessage>>(Array.Empty<ClaudeMessage>());
        }

        return Task.FromResult<IReadOnlyList<ClaudeMessage>>(session.Messages.ToList());
    }

    public Task<IReadOnlyList<SessionCacheSummary>> GetSessionHistoryAsync(
        string projectId,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] GetSessionHistory for project {ProjectId}, entity {EntityId}", projectId, entityId);

        // Return mock session history
        var history = new List<SessionCacheSummary>
        {
            new SessionCacheSummary(
                SessionId: Guid.NewGuid().ToString(),
                EntityId: entityId,
                ProjectId: projectId,
                MessageCount: 10,
                CreatedAt: DateTime.UtcNow.AddHours(-2),
                LastMessageAt: DateTime.UtcNow.AddHours(-1),
                Mode: SessionMode.Build,
                Model: "opus"
            ),
            new SessionCacheSummary(
                SessionId: Guid.NewGuid().ToString(),
                EntityId: entityId,
                ProjectId: projectId,
                MessageCount: 5,
                CreatedAt: DateTime.UtcNow.AddDays(-1),
                LastMessageAt: DateTime.UtcNow.AddDays(-1).AddHours(1),
                Mode: SessionMode.Plan,
                Model: "sonnet"
            )
        };

        return Task.FromResult<IReadOnlyList<SessionCacheSummary>>(history);
    }

    /// <summary>
    /// Generates a mock response with tool use and tool result blocks.
    /// Returns the assistant message content and any tool result messages.
    /// </summary>
    private (List<ClaudeMessageContent> assistantContent, List<ClaudeMessage> toolResultMessages)
        GenerateMockResponseWithTools(string sessionId, string userMessage, SessionMode mode)
    {
        var assistantContent = new List<ClaudeMessageContent>();
        var toolResultMessages = new List<ClaudeMessage>();
        var lowerMessage = userMessage.ToLowerInvariant();

        // Add thinking block
        assistantContent.Add(new ClaudeMessageContent
        {
            Type = ClaudeContentType.Thinking,
            Text = "Analyzing the request and determining which tools to use..."
        });

        // Determine which tools to simulate based on message content
        if (lowerMessage.Contains("read") || lowerMessage.Contains("file") || lowerMessage.Contains("show"))
        {
            AddReadToolSequence(sessionId, assistantContent, toolResultMessages);
        }
        else if (lowerMessage.Contains("search") || lowerMessage.Contains("find") || lowerMessage.Contains("grep"))
        {
            AddGrepToolSequence(sessionId, assistantContent, toolResultMessages);
        }
        else if (lowerMessage.Contains("run") || lowerMessage.Contains("test") || lowerMessage.Contains("bash") || lowerMessage.Contains("command"))
        {
            AddBashToolSequence(sessionId, assistantContent, toolResultMessages);
        }
        else if (lowerMessage.Contains("write") || lowerMessage.Contains("create") || lowerMessage.Contains("edit"))
        {
            AddWriteToolSequence(sessionId, assistantContent, toolResultMessages);
        }
        else if (lowerMessage.Contains("glob") || lowerMessage.Contains("list") || lowerMessage.Contains("files"))
        {
            AddGlobToolSequence(sessionId, assistantContent, toolResultMessages);
        }
        else
        {
            // Default: show a mix of tools
            AddReadToolSequence(sessionId, assistantContent, toolResultMessages);
            AddBashToolSequence(sessionId, assistantContent, toolResultMessages);
        }

        // Add final text response
        var truncatedMessage = userMessage.Length > 50 ? userMessage[..50] + "..." : userMessage;
        assistantContent.Add(new ClaudeMessageContent
        {
            Type = ClaudeContentType.Text,
            Text = mode switch
            {
                SessionMode.Plan => $"[Mock Plan Response]\n\nI've analyzed your request: \"{truncatedMessage}\"\n\nBased on the tool results above, here's my analysis...",
                SessionMode.Build => $"[Mock Build Response]\n\nI've processed your request: \"{truncatedMessage}\"\n\nThe tool operations above show the mock results.",
                _ => $"[Mock Response] Processed: {truncatedMessage}"
            }
        });

        return (assistantContent, toolResultMessages);
    }

    private void AddReadToolSequence(string sessionId, List<ClaudeMessageContent> assistantContent, List<ClaudeMessage> toolResultMessages)
    {
        var toolUseId = $"toolu_mock_{++_toolUseCounter:D6}";
        var filePath = "/src/Homespun/Program.cs";
        var fileContent = """
                 1→using Microsoft.AspNetCore.Builder;
                 2→using Microsoft.Extensions.DependencyInjection;
                 3→
                 4→var builder = WebApplication.CreateBuilder(args);
                 5→
                 6→// Add services to the container
                 7→builder.Services.AddRazorPages();
                 8→builder.Services.AddServerSideBlazor();
                 9→
                10→var app = builder.Build();
                11→
                12→app.UseStaticFiles();
                13→app.UseRouting();
                14→app.MapBlazorHub();
                15→app.MapFallbackToPage("/_Host");
                16→
                17→app.Run();
            """;

        // Tool Use block
        assistantContent.Add(new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = "Read",
            ToolUseId = toolUseId,
            ToolInput = JsonSerializer.Serialize(new { file_path = filePath })
        });

        // Tool Result message
        var parsedResult = _toolResultParser.Parse("Read", fileContent, isError: false);
        toolResultMessages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = toolUseId,
                    ToolName = "Read",
                    ToolSuccess = true,
                    Text = fileContent,
                    ParsedToolResult = parsedResult
                }
            ],
            CreatedAt = DateTime.UtcNow
        });
    }

    private void AddGrepToolSequence(string sessionId, List<ClaudeMessageContent> assistantContent, List<ClaudeMessage> toolResultMessages)
    {
        var toolUseId = $"toolu_mock_{++_toolUseCounter:D6}";
        var grepOutput = """
            src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs:45:    private readonly IToolResultParser _toolResultParser;
            src/Homespun/Features/ClaudeCode/Services/ToolResultParser.cs:1:namespace Homespun.Features.ClaudeCode.Services;
            src/Homespun/Features/ClaudeCode/Services/IToolResultParser.cs:3:public interface IToolResultParser
            src/Homespun/Features/Testing/Services/MockClaudeSessionService.cs:18:    private readonly IToolResultParser _toolResultParser;
            """;

        // Tool Use block
        assistantContent.Add(new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = "Grep",
            ToolUseId = toolUseId,
            ToolInput = JsonSerializer.Serialize(new { pattern = "IToolResultParser", path = "src/" })
        });

        // Tool Result message
        var parsedResult = _toolResultParser.Parse("Grep", grepOutput, isError: false);
        toolResultMessages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = toolUseId,
                    ToolName = "Grep",
                    ToolSuccess = true,
                    Text = grepOutput,
                    ParsedToolResult = parsedResult
                }
            ],
            CreatedAt = DateTime.UtcNow
        });
    }

    private void AddBashToolSequence(string sessionId, List<ClaudeMessageContent> assistantContent, List<ClaudeMessage> toolResultMessages)
    {
        var toolUseId = $"toolu_mock_{++_toolUseCounter:D6}";
        var bashOutput = """
            Running tests...

            Test run for /src/Homespun/tests/bin/Debug/net8.0/Homespun.Tests.dll (.NETCoreApp,Version=v8.0)
            Microsoft (R) Test Execution Command Line Tool Version 17.8.0

            Starting test execution, please wait...
            A total of 42 test files matched the specified pattern.

            Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42
            """;

        // Tool Use block
        assistantContent.Add(new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = "Bash",
            ToolUseId = toolUseId,
            ToolInput = JsonSerializer.Serialize(new { command = "dotnet test" })
        });

        // Tool Result message
        var parsedResult = _toolResultParser.Parse("Bash", bashOutput, isError: false);
        toolResultMessages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = toolUseId,
                    ToolName = "Bash",
                    ToolSuccess = true,
                    Text = bashOutput,
                    ParsedToolResult = parsedResult
                }
            ],
            CreatedAt = DateTime.UtcNow
        });
    }

    private void AddWriteToolSequence(string sessionId, List<ClaudeMessageContent> assistantContent, List<ClaudeMessage> toolResultMessages)
    {
        var toolUseId = $"toolu_mock_{++_toolUseCounter:D6}";
        var writeOutput = "Successfully wrote 25 lines to /src/Homespun/Features/NewFeature.cs";

        // Tool Use block
        assistantContent.Add(new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = "Write",
            ToolUseId = toolUseId,
            ToolInput = JsonSerializer.Serialize(new
            {
                file_path = "/src/Homespun/Features/NewFeature.cs",
                content = "public class NewFeature { }"
            })
        });

        // Tool Result message
        var parsedResult = _toolResultParser.Parse("Write", writeOutput, isError: false);
        toolResultMessages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = toolUseId,
                    ToolName = "Write",
                    ToolSuccess = true,
                    Text = writeOutput,
                    ParsedToolResult = parsedResult
                }
            ],
            CreatedAt = DateTime.UtcNow
        });
    }

    private void AddGlobToolSequence(string sessionId, List<ClaudeMessageContent> assistantContent, List<ClaudeMessage> toolResultMessages)
    {
        var toolUseId = $"toolu_mock_{++_toolUseCounter:D6}";
        var globOutput = """
            src/Homespun/Program.cs
            src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs
            src/Homespun/Features/ClaudeCode/Services/ToolResultParser.cs
            src/Homespun/Features/ClaudeCode/Data/ToolResultData.cs
            src/Homespun/Features/Testing/Services/MockClaudeSessionService.cs
            src/Homespun/Components/Pages/Home.razor.cs
            """;

        // Tool Use block
        assistantContent.Add(new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = "Glob",
            ToolUseId = toolUseId,
            ToolInput = JsonSerializer.Serialize(new { pattern = "**/*.cs" })
        });

        // Tool Result message
        var parsedResult = _toolResultParser.Parse("Glob", globOutput, isError: false);
        toolResultMessages.Add(new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content =
            [
                new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolResult,
                    ToolUseId = toolUseId,
                    ToolName = "Glob",
                    ToolSuccess = true,
                    Text = globOutput,
                    ParsedToolResult = parsedResult
                }
            ],
            CreatedAt = DateTime.UtcNow
        });
    }
}

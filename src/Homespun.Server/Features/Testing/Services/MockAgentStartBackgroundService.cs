using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Notifications;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IAgentStartBackgroundService for testing.
/// Synchronously creates a session and broadcasts SignalR events.
/// </summary>
public class MockAgentStartBackgroundService(
    IClaudeSessionService sessionService,
    IAgentPromptService agentPromptService,
    IAgentStartupTracker startupTracker,
    IHubContext<ClaudeCodeHub> claudeCodeHub,
    IHubContext<NotificationHub> notificationHub,
    ILogger<MockAgentStartBackgroundService> logger)
    : IAgentStartBackgroundService
{
    /// <inheritdoc/>
    public async Task QueueAgentStartAsync(AgentOrchestration.Services.AgentStartRequest request)
    {
        logger.LogDebug(
            "[Mock] QueueAgentStartAsync for issue {IssueId} with branch {BranchName}",
            request.IssueId, request.BranchName);

        try
        {
            // Broadcast agent starting
            await notificationHub.BroadcastAgentStarting(request.IssueId, request.ProjectId, request.BranchName);

            // Resolve prompt and determine mode
            AgentPrompt? prompt = null;
            string? renderedMessage = null;
            var mode = SessionMode.Plan; // Default for None

            if (!string.IsNullOrEmpty(request.PromptId))
            {
                prompt = agentPromptService.GetPrompt(request.PromptId);
                if (prompt != null)
                {
                    mode = prompt.Mode;

                    var promptContext = new PromptContext
                    {
                        Title = request.Issue.Title,
                        Id = request.Issue.Id,
                        Description = request.Issue.Description,
                        Branch = request.BranchName,
                        Type = request.Issue.Type.ToString()
                    };

                    renderedMessage = agentPromptService.RenderTemplate(prompt.InitialMessage, promptContext);
                }
            }

            // Create session using the mock session service
            // Use a mock working directory since we don't have real clones in mock mode
            var mockWorkingDirectory = $"/mock/clones/{request.BranchName}";

            var session = await sessionService.StartSessionAsync(
                request.IssueId,
                request.ProjectId,
                mockWorkingDirectory,
                mode,
                request.Model);

            // Broadcast session started
            await claudeCodeHub.BroadcastSessionStarted(session);

            // Send the rendered initial message if present
            if (!string.IsNullOrWhiteSpace(renderedMessage))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await sessionService.SendMessageAsync(session.Id, renderedMessage, mode);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[Mock] Error sending initial message for session {SessionId}", session.Id);
                    }
                });
            }

            // Mark as successfully started
            startupTracker.MarkAsStarted(request.IssueId);

            logger.LogInformation(
                "[Mock] Agent started successfully for issue {IssueId}, session {SessionId}",
                request.IssueId, session.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Mock] Error starting agent for issue {IssueId}", request.IssueId);
            startupTracker.MarkAsFailed(request.IssueId, ex.Message);
            await notificationHub.BroadcastAgentStartFailed(
                request.IssueId, request.ProjectId, ex.Message);
        }
    }
}

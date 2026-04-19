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
    ISkillDiscoveryService skillDiscovery,
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

            // Prefer the seeded project directory (a real writable git repo under the
            // mock temp folder) so live-session profiles like dev-live can exec real
            // workers against it. Fall back to a `/mock/clones/…` placeholder only
            // when no ProjectLocalPath is supplied — MockAgentExecutionService never
            // touches the path, so the placeholder is fine there.
            var workingDirectory = !string.IsNullOrEmpty(request.ProjectLocalPath)
                ? request.ProjectLocalPath
                : $"/mock/clones/{request.BranchName}";

            // Resolve mode and initial message using the shared skill-dispatch path
            var (initialMessage, mode) = await AgentStartBackgroundService
                .ResolveDispatchAsync(request, skillDiscovery, workingDirectory, CancellationToken.None);

            var session = await sessionService.StartSessionAsync(
                request.IssueId,
                request.ProjectId,
                workingDirectory,
                mode,
                request.Model);

            // Broadcast session started
            await claudeCodeHub.BroadcastSessionStarted(session);

            // Send the composed initial message if present
            if (!string.IsNullOrWhiteSpace(initialMessage))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await sessionService.SendMessageAsync(session.Id, initialMessage, mode);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[Mock] Error sending initial message for session {SessionId}", session.Id);
                    }
                });
            }

            // Mark as successfully started and clear tracker entry
            startupTracker.MarkAsStarted(request.IssueId);
            startupTracker.Clear(request.IssueId);

            logger.LogInformation(
                "[Mock] Agent started successfully for issue {IssueId}, session {SessionId}",
                request.IssueId, session.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Mock] Error starting agent for issue {IssueId}", request.IssueId);
            startupTracker.MarkAsFailed(request.IssueId, ex.Message);
            startupTracker.Clear(request.IssueId);
            await notificationHub.BroadcastAgentStartFailed(
                request.IssueId, request.ProjectId, ex.Message);
        }
    }
}

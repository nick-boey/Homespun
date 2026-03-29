using System.Collections.Concurrent;
using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Notifications;
using Homespun.Features.Workflows.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Background service for handling asynchronous agent startup.
/// </summary>
public class AgentStartBackgroundService(
    IServiceProvider serviceProvider,
    IAgentStartupTracker startupTracker,
    ILogger<AgentStartBackgroundService> logger)
    : IAgentStartBackgroundService
{
    private readonly ConcurrentDictionary<string, DateTime> _pendingStartups = new();
    private readonly TimeSpan _startupTimeout = TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    public Task QueueAgentStartAsync(AgentStartRequest request)
    {
        // Check if already starting for this issue
        if (_pendingStartups.ContainsKey(request.IssueId))
        {
            logger.LogDebug(
                "Agent startup already pending for issue {IssueId}. Pending count: {PendingCount}, Pending IDs: [{PendingIds}]",
                request.IssueId, _pendingStartups.Count, string.Join(", ", _pendingStartups.Keys));
            return Task.CompletedTask;
        }

        // Mark as pending
        _pendingStartups[request.IssueId] = DateTime.UtcNow;
        logger.LogInformation(
            "Queued agent start for issue {IssueId}. Pending count: {PendingCount}, Pending IDs: [{PendingIds}]",
            request.IssueId, _pendingStartups.Count, string.Join(", ", _pendingStartups.Keys));

        // Fire and forget with proper error handling
        _ = Task.Run(async () =>
        {
            try
            {
                await StartAgentAsync(request);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in agent startup for issue {IssueId}", request.IssueId);
            }
            finally
            {
                _pendingStartups.TryRemove(request.IssueId, out _);
                logger.LogDebug(
                    "Removed issue {IssueId} from pending startups. Remaining count: {PendingCount}",
                    request.IssueId, _pendingStartups.Count);
            }
        });

        return Task.CompletedTask;
    }

    private async Task StartAgentAsync(AgentStartRequest request)
    {
        using var scope = serviceProvider.CreateScope();
        var cloneService = scope.ServiceProvider.GetRequiredService<IGitCloneService>();
        var sessionService = scope.ServiceProvider.GetRequiredService<IClaudeSessionService>();
        var agentPromptService = scope.ServiceProvider.GetRequiredService<IAgentPromptService>();
        var fleeceService = scope.ServiceProvider.GetRequiredService<IFleeceService>();
        var fleeceIssuesSyncService = scope.ServiceProvider.GetRequiredService<IFleeceIssuesSyncService>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();
        var baseBranchResolver = scope.ServiceProvider.GetRequiredService<IBaseBranchResolver>();

        using var cts = new CancellationTokenSource(_startupTimeout);

        try
        {
            // Step 0: Resolve base branch and check for blocking issues
            var resolution = await baseBranchResolver.ResolveBaseBranchAsync(request, cts.Token);
            if (resolution.Error != null)
            {
                // Blocked by open issues - notify failure without starting
                logger.LogWarning(
                    "Agent start blocked for issue {IssueId}: {Error}",
                    request.IssueId, resolution.Error);
                startupTracker.MarkAsFailed(request.IssueId, resolution.Error);
                startupTracker.Clear(request.IssueId);
                await hubContext.BroadcastAgentStartFailed(
                    request.IssueId, request.ProjectId, resolution.Error);
                return;
            }

            var resolvedBaseBranch = resolution.BaseBranch!;

            // Broadcast agent starting
            await hubContext.BroadcastAgentStarting(request.IssueId, request.ProjectId, request.BranchName);

            logger.LogInformation(
                "Starting agent for issue {IssueId} with branch {BranchName}",
                request.IssueId, request.BranchName);

            // Step 1: Get or create clone
            var clonePath = await cloneService.GetClonePathForBranchAsync(
                request.ProjectLocalPath, request.BranchName);

            if (string.IsNullOrEmpty(clonePath))
            {
                // Clone doesn't exist, create it using the resolved base branch
                var baseBranch = resolvedBaseBranch;

                // Pull latest changes on main repo before creating clone
                logger.LogInformation(
                    "Pulling latest changes from {BaseBranch} before creating clone",
                    baseBranch);

                var pullResult = await fleeceIssuesSyncService.PullFleeceOnlyAsync(
                    request.ProjectLocalPath,
                    baseBranch,
                    cts.Token);

                if (!pullResult.Success)
                {
                    logger.LogWarning(
                        "Auto-pull failed before clone creation: {Error}, continuing anyway",
                        pullResult.ErrorMessage);
                }
                else if (pullResult.WasBehindRemote)
                {
                    logger.LogInformation(
                        "Pulled {Commits} commits and merged {Issues} issues before clone creation",
                        pullResult.CommitsPulled, pullResult.IssuesMerged);
                }

                logger.LogInformation(
                    "Creating clone for branch {BranchName} from base {BaseBranch}",
                    request.BranchName, baseBranch);

                clonePath = await cloneService.CreateCloneAsync(
                    request.ProjectLocalPath,
                    request.BranchName,
                    createBranch: true,
                    baseBranch: baseBranch);

                if (string.IsNullOrEmpty(clonePath))
                {
                    throw new InvalidOperationException("Failed to create clone for issue branch");
                }

                logger.LogInformation(
                    "Created clone at {ClonePath} for issue {IssueId}",
                    clonePath, request.IssueId);
            }
            else
            {
                logger.LogInformation(
                    "Using existing clone at {ClonePath} for branch {BranchName}",
                    clonePath, request.BranchName);
            }

            // Step 2: Resolve prompt and render template
            AgentPrompt? prompt = null;
            string? renderedMessage = null;
            var mode = SessionMode.Plan; // Default for None

            if (!string.IsNullOrWhiteSpace(request.UserInstructions))
            {
                // User instructions override the prompt template
                renderedMessage = request.UserInstructions;

                // If a prompt was also provided, use its mode; otherwise default to Build
                if (!string.IsNullOrEmpty(request.PromptId))
                {
                    prompt = agentPromptService.GetPrompt(request.PromptId);
                    mode = prompt?.Mode ?? SessionMode.Build;
                }
                else
                {
                    mode = SessionMode.Build;
                }
            }
            else if (!string.IsNullOrEmpty(request.PromptId))
            {
                prompt = agentPromptService.GetPrompt(request.PromptId);
                if (prompt != null)
                {
                    mode = prompt.Mode;

                    // Build hierarchical context (ancestors and direct children)
                    var allIssues = await fleeceService.ListIssuesAsync(request.ProjectLocalPath);
                    var treeContext = IssueTreeFormatter.FormatIssueTree(request.Issue, allIssues);

                    var promptContext = new PromptContext
                    {
                        Title = request.Issue.Title,
                        Id = request.Issue.Id,
                        Description = request.Issue.Description,
                        Branch = request.BranchName,
                        Type = request.Issue.Type.ToString(),
                        Context = treeContext
                    };

                    renderedMessage = agentPromptService.RenderTemplate(prompt.InitialMessage, promptContext);
                }
            }

            // Step 3: Create session
            var session = await sessionService.StartSessionAsync(
                request.IssueId,
                request.ProjectId,
                clonePath,
                mode,
                request.Model,
                systemPrompt: null);

            // Step 3.5: Register with workflow if this is a workflow request
            if (request.IsWorkflowRequest)
            {
                var workflowSessionCallback = scope.ServiceProvider.GetRequiredService<IWorkflowSessionCallback>();
                var workflowContext = new WorkflowSessionContext
                {
                    ExecutionId = request.WorkflowExecutionId!,
                    StepId = request.WorkflowStepId!,
                    WorkflowId = request.WorkflowExecutionId!,
                    ProjectPath = request.ProjectLocalPath
                };

                workflowSessionCallback.RegisterSession(session.Id, workflowContext);

                logger.LogInformation(
                    "Registered session {SessionId} with workflow execution {ExecutionId}, step {StepId}",
                    session.Id, request.WorkflowExecutionId, request.WorkflowStepId);
            }

            // Step 4: Send the rendered initial message (fire and forget)
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
                        logger.LogError(ex, "Error sending initial message for session {SessionId}", session.Id);
                    }
                });
            }

            // Mark as successfully started and clear tracker entry
            startupTracker.MarkAsStarted(request.IssueId);
            startupTracker.Clear(request.IssueId);

            logger.LogInformation(
                "Agent started successfully for issue {IssueId}, session {SessionId}",
                request.IssueId, session.Id);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Agent startup timed out for issue {IssueId}", request.IssueId);
            startupTracker.MarkAsFailed(request.IssueId, "Agent startup timed out");
            startupTracker.Clear(request.IssueId);
            await hubContext.BroadcastAgentStartFailed(
                request.IssueId, request.ProjectId, "Agent startup timed out");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting agent for issue {IssueId}", request.IssueId);
            startupTracker.MarkAsFailed(request.IssueId, ex.Message);
            startupTracker.Clear(request.IssueId);
            await hubContext.BroadcastAgentStartFailed(
                request.IssueId, request.ProjectId, ex.Message);
        }
    }
}

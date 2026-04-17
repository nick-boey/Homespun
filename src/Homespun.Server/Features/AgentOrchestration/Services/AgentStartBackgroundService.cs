using System.Collections.Concurrent;
using System.Text;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.Notifications;
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
        var skillDiscovery = scope.ServiceProvider.GetRequiredService<ISkillDiscoveryService>();
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

            // Step 2: Resolve mode and initial message via skill (if any)
            var (initialMessage, mode) = await ResolveDispatchAsync(
                request, skillDiscovery, clonePath, cts.Token);

            // Step 3: Create session
            var session = await sessionService.StartSessionAsync(
                request.IssueId,
                request.ProjectId,
                clonePath,
                mode,
                request.Model,
                systemPrompt: request.SystemPromptOverride);

            // Step 4: Send the composed initial message (fire and forget)
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

    /// <summary>
    /// Resolve the initial message and session mode for a dispatch:
    /// - If the request names a skill, compose the skill body + args + user instructions.
    /// - Otherwise, use the user instructions as the initial message.
    /// - Session mode: explicit request.Mode wins; else skill.Mode; else Plan.
    /// </summary>
    internal static async Task<(string? InitialMessage, SessionMode Mode)> ResolveDispatchAsync(
        AgentStartRequest request,
        ISkillDiscoveryService skillDiscovery,
        string clonePath,
        CancellationToken cancellationToken)
    {
        SkillDescriptor? skill = null;
        if (!string.IsNullOrWhiteSpace(request.SkillName))
        {
            skill = await skillDiscovery
                .GetSkillAsync(clonePath, request.SkillName, cancellationToken)
                .ConfigureAwait(false);
        }

        var mode = request.Mode ?? skill?.Mode ?? SessionMode.Plan;
        var message = ComposeInitialMessage(skill, request.SkillArgs, request.UserInstructions);
        return (message, mode);
    }

    /// <summary>
    /// Build the dispatch message. Shape:
    /// <code>
    /// {skill body}
    ///
    /// ## Args
    /// arg-name: value
    ///
    /// {user instructions}
    /// </code>
    /// Each section is omitted when empty. When no skill is resolved, only
    /// the user instructions are returned (or null when blank).
    /// </summary>
    internal static string? ComposeInitialMessage(
        SkillDescriptor? skill,
        IReadOnlyDictionary<string, string>? args,
        string? userInstructions)
    {
        var hasSkill = skill is { SkillBody: { } body } && !string.IsNullOrWhiteSpace(body);
        var hasUser = !string.IsNullOrWhiteSpace(userInstructions);
        var argEntries = args?
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToList() ?? new List<KeyValuePair<string, string>>();

        if (!hasSkill && !hasUser)
        {
            return null;
        }

        var sb = new StringBuilder();
        if (hasSkill)
        {
            sb.Append(skill!.SkillBody!.TrimEnd());
        }

        if (argEntries.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }
            sb.AppendLine("## Args");
            foreach (var (name, value) in argEntries)
            {
                sb.Append(name);
                sb.Append(": ");
                sb.AppendLine(value);
            }
        }

        if (hasUser)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.Append(userInstructions!.TrimEnd());
        }

        return sb.ToString();
    }
}

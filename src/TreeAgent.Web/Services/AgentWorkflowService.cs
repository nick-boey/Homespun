using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Data;
using TreeAgent.Web.Data.Entities;
using TreeAgent.Web.Features.Agents.Data;
using TreeAgent.Web.Models;

namespace TreeAgent.Web.Services;

/// <summary>
/// Result of starting work on a future change.
/// </summary>
public record StartWorkResult(Feature? Feature, Agent? Agent);

/// <summary>
/// Service for managing agent workflow and PR status transitions.
/// </summary>
public class AgentWorkflowService
{
    private readonly TreeAgentDbContext _db;
    private readonly ICommandRunner _commandRunner;
    private readonly IRoadmapService _roadmapService;

    public AgentWorkflowService(
        TreeAgentDbContext db,
        ICommandRunner commandRunner,
        IRoadmapService roadmapService)
    {
        _db = db;
        _commandRunner = commandRunner;
        _roadmapService = roadmapService;
    }

    #region 5.1 Agent Status Updates

    /// <summary>
    /// Called when an agent starts working. Updates the feature to InDevelopment.
    /// </summary>
    public async Task OnAgentStartedAsync(string agentId)
    {
        var agent = await _db.Agents
            .Include(a => a.Feature)
            .FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent?.Feature == null) return;

        if (agent.Feature.Status == FeatureStatus.Future ||
            agent.Feature.Status == FeatureStatus.ReadyForReview)
        {
            agent.Feature.Status = FeatureStatus.InDevelopment;
            agent.Feature.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Called when an agent completes its work. Updates the feature to ReadyForReview.
    /// </summary>
    public async Task OnAgentCompletedAsync(string agentId)
    {
        var agent = await _db.Agents
            .Include(a => a.Feature)
            .FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent?.Feature == null) return;

        if (agent.Feature.Status == FeatureStatus.InDevelopment)
        {
            agent.Feature.Status = FeatureStatus.ReadyForReview;
            agent.Feature.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Checks if the agent's work only modified ROADMAP.json (plan update only).
    /// </summary>
    public async Task<bool> IsPlanUpdateOnlyAsync(string agentId)
    {
        var agent = await _db.Agents
            .Include(a => a.Feature)
            .FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent?.Feature == null) return false;

        return await _roadmapService.IsPlanUpdateOnlyAsync(agent.Feature.Id);
    }

    #endregion

    #region 5.2 Review Comment Handling

    /// <summary>
    /// Called when review comments are received on a PR.
    /// Transitions the feature back to InDevelopment.
    /// </summary>
    public async Task OnReviewCommentsReceivedAsync(string featureId)
    {
        var feature = await _db.Features.FindAsync(featureId);
        if (feature == null) return;

        if (feature.Status == FeatureStatus.ReadyForReview)
        {
            feature.Status = FeatureStatus.InDevelopment;
            feature.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Spawns a new agent to address review comments.
    /// </summary>
    public async Task<Agent?> SpawnAgentForReviewAsync(string featureId, string reviewComments)
    {
        var feature = await _db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == featureId);

        if (feature == null) return null;

        // Build system prompt with review context
        var systemPrompt = BuildReviewSystemPrompt(feature, reviewComments);

        var agent = new Agent
        {
            FeatureId = featureId,
            SystemPrompt = systemPrompt,
            Status = AgentStatus.Idle
        };

        _db.Agents.Add(agent);

        // Update feature status
        feature.Status = FeatureStatus.InDevelopment;
        feature.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return agent;
    }

    private static string BuildReviewSystemPrompt(Feature feature, string reviewComments)
    {
        return $"""
            You are working on the feature: {feature.Title}

            This feature has received review comments that need to be addressed.

            ## Review Comments
            {reviewComments}

            ## Instructions
            1. Read and understand the review comments
            2. Make the necessary changes to address the feedback
            3. Ensure tests pass after your changes
            4. Commit your changes with a descriptive message

            Please address all the review comments.
            """;
    }

    #endregion

    #region Starting Work on Future Changes

    /// <summary>
    /// Starts work on a future change from the roadmap.
    /// Creates a feature, worktree, and agent.
    /// </summary>
    public async Task<StartWorkResult> StartWorkOnFutureChangeAsync(string projectId, string changeId)
    {
        // Get the change details for instructions
        var change = await _roadmapService.FindChangeByIdAsync(projectId, changeId);
        if (change == null)
        {
            return new StartWorkResult(null, null);
        }

        // Promote the change (creates feature and worktree)
        var feature = await _roadmapService.PromoteChangeAsync(projectId, changeId);
        if (feature == null)
        {
            return new StartWorkResult(null, null);
        }

        // Reload to get full feature with relationships
        feature = await _db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == feature.Id);

        if (feature == null)
        {
            return new StartWorkResult(null, null);
        }

        // Create agent with instructions from the change
        var systemPrompt = BuildWorkSystemPrompt(feature, change);

        var agent = new Agent
        {
            FeatureId = feature.Id,
            SystemPrompt = systemPrompt,
            Status = AgentStatus.Idle
        };

        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        return new StartWorkResult(feature, agent);
    }

    private static string BuildWorkSystemPrompt(Feature feature, RoadmapChange change)
    {
        var prompt = $"""
            You are working on: {change.Title}

            ## Description
            {change.Description ?? "No description provided."}

            ## Type
            {change.Type}

            ## Group
            {change.Group}

            """;

        if (!string.IsNullOrEmpty(change.Instructions))
        {
            prompt += $"""

            ## Implementation Instructions
            {change.Instructions}

            """;
        }

        prompt += """

            ## General Guidelines
            1. Follow TDD practices where applicable
            2. Write clean, maintainable code
            3. Ensure all tests pass before completing
            4. Commit your changes with descriptive messages
            """;

        return prompt;
    }

    #endregion
}

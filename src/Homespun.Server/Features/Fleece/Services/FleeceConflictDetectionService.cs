using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Issues;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Implementation of conflict detection service.
/// </summary>
public class FleeceConflictDetectionService : IFleeceConflictDetectionService
{
    private readonly IProjectService _projectService;
    private readonly IClaudeSessionService _sessionService;
    private readonly IProjectFleeceService _fleeceService;
    private readonly ILogger<FleeceConflictDetectionService> _logger;

    public FleeceConflictDetectionService(
        IProjectService projectService,
        IClaudeSessionService sessionService,
        IProjectFleeceService fleeceService,
        ILogger<FleeceConflictDetectionService> logger)
    {
        _projectService = projectService;
        _sessionService = sessionService;
        _fleeceService = fleeceService;
        _logger = logger;
    }

    public async Task<List<IssueConflictDto>> DetectConflictsAsync(
        string projectId,
        string sessionId,
        List<IssueChangeDto> changes,
        CancellationToken cancellationToken = default)
    {
        var conflicts = new List<IssueConflictDto>();

        // Get project
        var project = await _projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            throw new ArgumentException($"Project {projectId} not found");
        }

        // Get session to find when it started (base point for three-way merge)
        var session = _sessionService.GetSession(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        // For now, we'll do a simple two-way conflict detection
        // In a full implementation, we would track the base state when the session started
        foreach (var change in changes.Where(c => c.ChangeType == ChangeType.Updated))
        {
            var currentIssue = await _fleeceService.GetIssueAsync(project.LocalPath, change.IssueId, cancellationToken);
            if (currentIssue == null)
                continue;

            // Check if the main branch issue has changed since the agent's base
            // For simplicity, we check if LastUpdate is after session start
            if (currentIssue.LastUpdate > session.CreatedAt)
            {
                // Potential conflict - compare fields
                var fieldConflicts = DetectFieldConflicts(change.OriginalIssue!, currentIssue.ToDto(), change.ModifiedIssue!);

                if (fieldConflicts.Any())
                {
                    conflicts.Add(new IssueConflictDto
                    {
                        IssueId = change.IssueId,
                        Title = currentIssue.Title,
                        FieldConflicts = fieldConflicts,
                        BaseIssue = change.OriginalIssue, // Approximate base
                        MainIssue = currentIssue.ToDto(),
                        AgentIssue = change.ModifiedIssue
                    });
                }
            }
        }

        _logger.LogInformation("Detected {Count} conflicts in session {SessionId}", conflicts.Count, sessionId);
        return conflicts;
    }

    private List<FieldConflictDto> DetectFieldConflicts(Homespun.Shared.Models.Issues.IssueDto baseIssue, Homespun.Shared.Models.Issues.IssueDto mainIssue, Homespun.Shared.Models.Issues.IssueDto agentIssue)
    {
        var conflicts = new List<FieldConflictDto>();

        // Check each field for three-way conflicts
        CheckFieldConflict(conflicts, "Title", baseIssue.Title, mainIssue.Title, agentIssue.Title);
        CheckFieldConflict(conflicts, "Description", baseIssue.Description, mainIssue.Description, agentIssue.Description);
        CheckFieldConflict(conflicts, "Status", baseIssue.Status.ToString(), mainIssue.Status.ToString(), agentIssue.Status.ToString());
        CheckFieldConflict(conflicts, "Type", baseIssue.Type.ToString(), mainIssue.Type.ToString(), agentIssue.Type.ToString());
        CheckFieldConflict(conflicts, "Priority", baseIssue.Priority?.ToString(), mainIssue.Priority?.ToString(), agentIssue.Priority?.ToString());
        CheckFieldConflict(conflicts, "WorkingBranchId", baseIssue.WorkingBranchId, mainIssue.WorkingBranchId, agentIssue.WorkingBranchId);
        CheckFieldConflict(conflicts, "AssignedTo", baseIssue.AssignedTo, mainIssue.AssignedTo, agentIssue.AssignedTo);

        return conflicts;
    }

    private void CheckFieldConflict(
        List<FieldConflictDto> conflicts,
        string fieldName,
        string? baseValue,
        string? mainValue,
        string? agentValue)
    {
        // No conflict if agent didn't change the field
        if (baseValue == agentValue)
            return;

        // No conflict if main didn't change the field
        if (baseValue == mainValue)
            return;

        // No conflict if both changed to the same value
        if (mainValue == agentValue)
            return;

        // We have a conflict - both changed the field to different values
        conflicts.Add(new FieldConflictDto
        {
            FieldName = fieldName,
            BaseValue = baseValue,
            MainValue = mainValue,
            AgentValue = agentValue
        });
    }
}

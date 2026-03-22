using Fleece.Core.Models;
using Fleece.Core.Serialization;
using Fleece.Core.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Git;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Issues;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for applying agent changes back to the main branch.
/// </summary>
public interface IFleeceChangeApplicationService
{
    /// <summary>
    /// Applies agent changes to the main branch, handling conflicts as specified.
    /// </summary>
    Task<ApplyAgentChangesResponse> ApplyChangesAsync(
        string projectId,
        string sessionId,
        ConflictResolutionStrategy conflictStrategy,
        bool dryRun = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves specific conflicts with manual resolutions.
    /// </summary>
    Task<ApplyAgentChangesResponse> ResolveConflictsAsync(
        string projectId,
        string sessionId,
        List<ConflictResolution> resolutions,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of change application service.
/// </summary>
public class FleeceChangeApplicationService : IFleeceChangeApplicationService
{
    private readonly IProjectService _projectService;
    private readonly IClaudeSessionService _sessionService;
    private readonly IFleeceService _fleeceService;
    private readonly IFleeceChangeDetectionService _changeDetectionService;
    private readonly IFleeceConflictDetectionService _conflictDetectionService;
    private readonly ILogger<FleeceChangeApplicationService> _logger;
    private readonly IssueMerger _issueMerger = new();

    // Store pending conflicts for manual resolution
    private readonly Dictionary<string, List<IssueConflictDto>> _pendingConflicts = new();

    public FleeceChangeApplicationService(
        IProjectService projectService,
        IClaudeSessionService sessionService,
        IFleeceService fleeceService,
        IFleeceChangeDetectionService changeDetectionService,
        IFleeceConflictDetectionService conflictDetectionService,
        ILogger<FleeceChangeApplicationService> logger)
    {
        _projectService = projectService;
        _sessionService = sessionService;
        _fleeceService = fleeceService;
        _changeDetectionService = changeDetectionService;
        _conflictDetectionService = conflictDetectionService;
        _logger = logger;
    }

    public async Task<ApplyAgentChangesResponse> ApplyChangesAsync(
        string projectId,
        string sessionId,
        ConflictResolutionStrategy conflictStrategy,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get project
            var project = await _projectService.GetByIdAsync(projectId);
            if (project == null)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = $"Project {projectId} not found"
                };
            }

            // Get session
            var session = _sessionService.GetSession(sessionId);
            if (session == null)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = $"Session {sessionId} not found"
                };
            }

            // Validate session state
            if (session.Status == ClaudeSessionStatus.Running || session.Status == ClaudeSessionStatus.RunningHooks)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = "Cannot apply changes while session is active. Stop the session first."
                };
            }

            // Detect changes
            var changes = await _changeDetectionService.DetectChangesAsync(projectId, sessionId, cancellationToken);
            if (!changes.Any())
            {
                return new ApplyAgentChangesResponse
                {
                    Success = true,
                    Message = "No changes detected",
                    Changes = [],
                    WouldApply = false
                };
            }

            // Detect conflicts
            var conflicts = await _conflictDetectionService.DetectConflictsAsync(
                projectId, sessionId, changes, cancellationToken);

            // Handle conflicts based on strategy
            if (conflicts.Any())
            {
                switch (conflictStrategy)
                {
                    case ConflictResolutionStrategy.Abort:
                        return new ApplyAgentChangesResponse
                        {
                            Success = false,
                            Message = $"Aborted due to {conflicts.Count} conflicts",
                            Changes = changes,
                            Conflicts = conflicts,
                            WouldApply = false
                        };

                    case ConflictResolutionStrategy.Manual:
                        if (!dryRun)
                        {
                            // Store conflicts for later resolution
                            _pendingConflicts[$"{projectId}:{sessionId}"] = conflicts;
                        }
                        return new ApplyAgentChangesResponse
                        {
                            Success = false,
                            Message = $"Manual resolution required for {conflicts.Count} conflicts",
                            Changes = changes,
                            Conflicts = conflicts,
                            WouldApply = false
                        };

                    case ConflictResolutionStrategy.AgentWins:
                    case ConflictResolutionStrategy.MainWins:
                        // Will be handled during application
                        break;
                }
            }

            // If dry run, return preview
            if (dryRun)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = true,
                    Message = $"Would apply {changes.Count} changes" + (conflicts.Any() ? $" with {conflicts.Count} conflicts" : ""),
                    Changes = changes,
                    Conflicts = conflicts,
                    WouldApply = true
                };
            }

            // Use file merge approach for AgentWins strategy (the common case)
            // This uses IssueMerger with field-level LWW merging, which is the proven
            // approach used by FleeceIssuesSyncService
            if (conflictStrategy == ConflictResolutionStrategy.AgentWins)
            {
                return await ApplyChangesViaFileMergeAsync(
                    project.LocalPath, session.WorkingDirectory, cancellationToken);
            }

            // Apply changes one-by-one for other strategies
            var appliedChanges = new List<IssueChangeDto>();
            var errors = new List<string>();

            foreach (var change in changes)
            {
                try
                {
                    // Check if this change has conflicts
                    var changeConflicts = conflicts.FirstOrDefault(c => c.IssueId == change.IssueId);

                    if (changeConflicts != null && conflictStrategy == ConflictResolutionStrategy.MainWins)
                    {
                        // Skip changes that have conflicts when main wins
                        _logger.LogInformation("Skipping change to issue {IssueId} due to MainWins conflict strategy", change.IssueId);
                        continue;
                    }

                    await ApplyChangeAsync(project.LocalPath, change, changeConflicts, conflictStrategy, cancellationToken);
                    appliedChanges.Add(change);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error applying change to issue {IssueId}", change.IssueId);
                    errors.Add($"Failed to apply change to {change.IssueId}: {ex.Message}");
                }
            }

            var success = errors.Count == 0;
            var message = success
                ? $"Applied {appliedChanges.Count} changes successfully"
                : $"Applied {appliedChanges.Count} changes with {errors.Count} errors";

            if (errors.Any())
            {
                message += "\nErrors:\n" + string.Join("\n", errors);
            }

            return new ApplyAgentChangesResponse
            {
                Success = success,
                Message = message,
                Changes = appliedChanges,
                Conflicts = conflicts
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying changes from session {SessionId}", sessionId);
            return new ApplyAgentChangesResponse
            {
                Success = false,
                Message = $"Error applying changes: {ex.Message}"
            };
        }
    }

    public async Task<ApplyAgentChangesResponse> ResolveConflictsAsync(
        string projectId,
        string sessionId,
        List<ConflictResolution> resolutions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{projectId}:{sessionId}";
            if (!_pendingConflicts.TryGetValue(key, out var conflicts))
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = "No pending conflicts found for this session"
                };
            }

            // Get project
            var project = await _projectService.GetByIdAsync(projectId);
            if (project == null)
            {
                return new ApplyAgentChangesResponse
                {
                    Success = false,
                    Message = $"Project {projectId} not found"
                };
            }

            var appliedChanges = new List<IssueChangeDto>();
            var errors = new List<string>();

            // Apply resolutions
            foreach (var resolution in resolutions)
            {
                var conflict = conflicts.FirstOrDefault(c => c.IssueId == resolution.IssueId);
                if (conflict == null)
                    continue;

                try
                {
                    var mergedIssue = await ApplyResolutionAsync(
                        project.LocalPath, conflict, resolution, cancellationToken);

                    appliedChanges.Add(new IssueChangeDto
                    {
                        IssueId = conflict.IssueId,
                        ChangeType = ChangeType.Updated,
                        Title = mergedIssue.Title,
                        OriginalIssue = conflict.MainIssue,
                        ModifiedIssue = mergedIssue.ToDto()
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error applying resolution to issue {IssueId}", resolution.IssueId);
                    errors.Add($"Failed to resolve {resolution.IssueId}: {ex.Message}");
                }
            }

            // Clear pending conflicts if all resolved
            if (errors.Count == 0)
            {
                _pendingConflicts.Remove(key);
            }

            var success = errors.Count == 0;
            var message = success
                ? $"Resolved {appliedChanges.Count} conflicts successfully"
                : $"Resolved {appliedChanges.Count} conflicts with {errors.Count} errors";

            return new ApplyAgentChangesResponse
            {
                Success = success,
                Message = message,
                Changes = appliedChanges
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving conflicts for session {SessionId}", sessionId);
            return new ApplyAgentChangesResponse
            {
                Success = false,
                Message = $"Error resolving conflicts: {ex.Message}"
            };
        }
    }

    private async Task ApplyChangeAsync(
        string projectPath,
        IssueChangeDto change,
        IssueConflictDto? conflicts,
        ConflictResolutionStrategy conflictStrategy,
        CancellationToken cancellationToken)
    {
        switch (change.ChangeType)
        {
            case ChangeType.Created:
                // Create new issue
                var issue = await _fleeceService.CreateIssueAsync(
                    projectPath,
                    change.ModifiedIssue!.Title,
                    change.ModifiedIssue.Type,
                    change.ModifiedIssue.Description,
                    change.ModifiedIssue.Priority,
                    change.ModifiedIssue.ExecutionMode,
                    change.ModifiedIssue.Status,
                    change.ModifiedIssue.AssignedTo,
                    cancellationToken);

                // Update additional fields if needed
                if (change.ModifiedIssue.WorkingBranchId != issue.WorkingBranchId ||
                    change.ModifiedIssue.AssignedTo != issue.AssignedTo)
                {
                    await _fleeceService.UpdateIssueAsync(
                        projectPath,
                        issue.Id,
                        workingBranchId: change.ModifiedIssue.WorkingBranchId);
                }

                // Apply parent relationships
                foreach (var parentRef in change.ModifiedIssue.ParentIssues)
                {
                    await _fleeceService.AddParentAsync(
                        projectPath, issue.Id, parentRef.ParentIssue, parentRef.SortOrder, cancellationToken);
                }
                break;

            case ChangeType.Updated:
                // Handle conflicts if any
                var modifiedIssue = change.ModifiedIssue!;
                if (conflicts != null && conflictStrategy == ConflictResolutionStrategy.AgentWins)
                {
                    // Agent wins - use all agent values
                    await UpdateIssueAsync(projectPath, change.IssueId, modifiedIssue, cancellationToken);
                }
                else if (conflicts == null)
                {
                    // No conflicts - apply all changes
                    await UpdateIssueAsync(projectPath, change.IssueId, modifiedIssue, cancellationToken);
                }
                break;

            case ChangeType.Deleted:
                await _fleeceService.DeleteIssueAsync(projectPath, change.IssueId, cancellationToken);
                break;
        }
    }

    private async Task UpdateIssueAsync(
        string projectPath,
        string issueId,
        Homespun.Shared.Models.Issues.IssueDto modifiedIssue,
        CancellationToken cancellationToken)
    {
        // Update basic fields
        await _fleeceService.UpdateIssueAsync(
            projectPath,
            issueId,
            modifiedIssue.Title,
            modifiedIssue.Status,
            modifiedIssue.Type,
            modifiedIssue.Description,
            modifiedIssue.Priority,
            modifiedIssue.ExecutionMode,
            modifiedIssue.WorkingBranchId,
            modifiedIssue.AssignedTo,
            cancellationToken);

        // Update parent relationships if changed
        var currentIssue = await _fleeceService.GetIssueAsync(projectPath, issueId, cancellationToken);
        if (currentIssue != null)
        {
            // Remove parents that are no longer in the modified issue
            var parentsToRemove = currentIssue.ParentIssues
                .Where(p => !modifiedIssue.ParentIssues.Any(mp => mp.ParentIssue == p.ParentIssue))
                .ToList();

            foreach (var parent in parentsToRemove)
            {
                await _fleeceService.RemoveParentAsync(projectPath, issueId, parent.ParentIssue, cancellationToken);
            }

            // Add new parents
            var parentsToAdd = modifiedIssue.ParentIssues
                .Where(mp => !currentIssue.ParentIssues.Any(p => p.ParentIssue == mp.ParentIssue))
                .ToList();

            foreach (var parent in parentsToAdd)
            {
                await _fleeceService.AddParentAsync(projectPath, issueId, parent.ParentIssue, parent.SortOrder, cancellationToken);
            }
        }
    }

    private async Task<Issue> ApplyResolutionAsync(
        string projectPath,
        IssueConflictDto conflict,
        ConflictResolution resolution,
        CancellationToken cancellationToken)
    {
        // Get the current issue state
        var currentIssue = await _fleeceService.GetIssueAsync(projectPath, conflict.IssueId, cancellationToken);
        if (currentIssue == null)
        {
            throw new InvalidOperationException($"Issue {conflict.IssueId} not found");
        }

        // Build the resolved issue state based on field resolutions
        var updates = new Dictionary<string, object?>();

        foreach (var fieldResolution in resolution.FieldResolutions)
        {
            var fieldConflict = conflict.FieldConflicts.FirstOrDefault(f => f.FieldName == fieldResolution.FieldName);
            if (fieldConflict == null)
                continue;

            var value = fieldResolution.Choice switch
            {
                ConflictChoice.UseMain => fieldConflict.MainValue,
                ConflictChoice.UseAgent => fieldConflict.AgentValue,
                ConflictChoice.Custom => fieldResolution.CustomValue,
                _ => fieldConflict.MainValue
            };

            updates[fieldResolution.FieldName] = value;
        }

        // Apply updates to the issue
        await _fleeceService.UpdateIssueAsync(
            projectPath,
            conflict.IssueId,
            updates.GetValueOrDefault("Title")?.ToString(),
            ParseEnum<IssueStatus>(updates.GetValueOrDefault("Status")),
            ParseEnum<IssueType>(updates.GetValueOrDefault("Type")),
            updates.GetValueOrDefault("Description")?.ToString(),
            ParseInt(updates.GetValueOrDefault("Priority")),
            ParseEnum<ExecutionMode>(updates.GetValueOrDefault("ExecutionMode")),
            updates.GetValueOrDefault("WorkingBranchId")?.ToString(),
            updates.GetValueOrDefault("AssignedTo")?.ToString(),
            cancellationToken);

        // Return the updated issue
        return (await _fleeceService.GetIssueAsync(projectPath, conflict.IssueId, cancellationToken))!;
    }

    private static T? ParseEnum<T>(object? value) where T : struct, Enum
    {
        if (value == null)
            return null;

        if (Enum.TryParse<T>(value.ToString(), out var result))
            return result;

        return null;
    }

    private static int? ParseInt(object? value)
    {
        if (value == null)
            return null;

        if (int.TryParse(value.ToString(), out var result))
            return result;

        return null;
    }

    /// <summary>
    /// Applies changes using file-based merging with IssueMerger (field-level LWW).
    /// This is the proven approach used by FleeceIssuesSyncService.
    /// </summary>
    private async Task<ApplyAgentChangesResponse> ApplyChangesViaFileMergeAsync(
        string mainPath,
        string agentClonePath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Applying changes via file merge: main={MainPath}, agent={AgentPath}",
            mainPath, agentClonePath);

        try
        {
            // Load issues from main branch
            var mainIssues = await LoadIssuesFromPathAsync(mainPath, cancellationToken);
            var mainIssueMap = mainIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

            _logger.LogDebug("Loaded {Count} issues from main branch", mainIssues.Count);

            // Load issues from agent clone
            var agentIssues = await LoadIssuesFromPathAsync(agentClonePath, cancellationToken);
            var agentIssueMap = agentIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

            _logger.LogDebug("Loaded {Count} issues from agent clone", agentIssues.Count);

            // Collect all issue IDs from both sides
            var allIssueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var issue in mainIssues)
                allIssueIds.Add(issue.Id);
            foreach (var issue in agentIssues)
                allIssueIds.Add(issue.Id);

            // Merge issues using IssueMerger (field-level LWW)
            var mergedIssues = new List<Issue>();
            var appliedChanges = new List<IssueChangeDto>();

            foreach (var issueId in allIssueIds)
            {
                var hasMain = mainIssueMap.TryGetValue(issueId, out var mainIssue);
                var hasAgent = agentIssueMap.TryGetValue(issueId, out var agentIssue);

                if (hasMain && hasAgent)
                {
                    // Both sides have the issue - merge using IssueMerger (LWW per field)
                    var mergeResult = _issueMerger.Merge(mainIssue!, agentIssue!);
                    mergedIssues.Add(mergeResult.MergedIssue);

                    // Check if there are actual differences from main
                    if (HasDifferences(mainIssue!, mergeResult.MergedIssue))
                    {
                        appliedChanges.Add(CreateChangeDto(
                            mergeResult.MergedIssue,
                            ChangeType.Updated,
                            mainIssue!,
                            mergeResult.MergedIssue));
                    }
                }
                else if (hasAgent)
                {
                    // Only agent has it - new issue created by agent
                    mergedIssues.Add(agentIssue!);
                    appliedChanges.Add(CreateChangeDto(
                        agentIssue!,
                        ChangeType.Created,
                        null,
                        agentIssue!));
                }
                else
                {
                    // Only main has it - keep it (agent didn't touch it)
                    mergedIssues.Add(mainIssue!);
                }
            }

            // Save merged result to main branch
            var serializer = new JsonlSerializer();
            var schemaValidator = new SchemaValidator();
            var storage = new JsonlStorageService(mainPath, serializer, schemaValidator);
            await storage.SaveIssuesAsync(mergedIssues, cancellationToken);

            _logger.LogInformation("Saved {Count} merged issues to main branch", mergedIssues.Count);

            // Clear the FleeceService cache so it picks up the new state
            await _fleeceService.ReloadFromDiskAsync(mainPath, cancellationToken);

            return new ApplyAgentChangesResponse
            {
                Success = true,
                Message = $"Applied {appliedChanges.Count} changes successfully",
                Changes = appliedChanges,
                Conflicts = []
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying changes via file merge");
            return new ApplyAgentChangesResponse
            {
                Success = false,
                Message = $"Error applying changes: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Loads issues directly from disk at the given path.
    /// </summary>
    private async Task<List<Issue>> LoadIssuesFromPathAsync(string path, CancellationToken cancellationToken)
    {
        var fleeceDir = Path.Combine(path, ".fleece");

        if (!Directory.Exists(fleeceDir))
        {
            _logger.LogWarning(".fleece directory not found at {Path}", fleeceDir);
            return [];
        }

        try
        {
            var serializer = new JsonlSerializer();
            var schemaValidator = new SchemaValidator();
            var storage = new JsonlStorageService(path, serializer, schemaValidator);
            var issues = await storage.LoadIssuesAsync(cancellationToken);
            return issues.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading issues from {Path}", path);
            return [];
        }
    }

    /// <summary>
    /// Checks if there are actual differences between two issues.
    /// </summary>
    private static bool HasDifferences(Issue original, Issue merged)
    {
        // Compare key fields that represent meaningful changes
        return original.Title != merged.Title ||
               original.Description != merged.Description ||
               original.Status != merged.Status ||
               original.Type != merged.Type ||
               original.Priority != merged.Priority ||
               original.ExecutionMode != merged.ExecutionMode ||
               original.WorkingBranchId != merged.WorkingBranchId ||
               original.AssignedTo != merged.AssignedTo ||
               !AreParentIssuesEqual(original.ParentIssues, merged.ParentIssues) ||
               !AreLinkedPRsEqual(original.LinkedPRs, merged.LinkedPRs);
    }

    private static bool AreParentIssuesEqual(IReadOnlyList<ParentIssueRef> list1, IReadOnlyList<ParentIssueRef> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        var set1 = list1.Select(p => $"{p.ParentIssue}:{p.SortOrder}").OrderBy(s => s).ToList();
        var set2 = list2.Select(p => $"{p.ParentIssue}:{p.SortOrder}").OrderBy(s => s).ToList();

        return set1.SequenceEqual(set2);
    }

    private static bool AreLinkedPRsEqual(IReadOnlyList<int> list1, IReadOnlyList<int> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        return list1.OrderBy(x => x).SequenceEqual(list2.OrderBy(x => x));
    }

    /// <summary>
    /// Creates an IssueChangeDto from an issue and change type.
    /// </summary>
    private static IssueChangeDto CreateChangeDto(
        Issue issue,
        ChangeType changeType,
        Issue? originalIssue,
        Issue modifiedIssue)
    {
        return new IssueChangeDto
        {
            IssueId = issue.Id,
            ChangeType = changeType,
            Title = issue.Title,
            OriginalIssue = originalIssue?.ToDto(),
            ModifiedIssue = modifiedIssue.ToDto(),
            FieldChanges = changeType == ChangeType.Created
                ? GetAllFieldsAsChanges(modifiedIssue)
                : GetFieldChanges(originalIssue!, modifiedIssue)
        };
    }

    private static List<FieldChangeDto> GetAllFieldsAsChanges(Issue issue)
    {
        return
        [
            new() { FieldName = "Title", OldValue = null, NewValue = issue.Title },
            new() { FieldName = "Description", OldValue = null, NewValue = issue.Description },
            new() { FieldName = "Status", OldValue = null, NewValue = issue.Status.ToString() },
            new() { FieldName = "Type", OldValue = null, NewValue = issue.Type.ToString() },
            new() { FieldName = "Priority", OldValue = null, NewValue = issue.Priority?.ToString() },
            new() { FieldName = "ExecutionMode", OldValue = null, NewValue = issue.ExecutionMode.ToString() },
            new() { FieldName = "WorkingBranchId", OldValue = null, NewValue = issue.WorkingBranchId },
            new() { FieldName = "AssignedTo", OldValue = null, NewValue = issue.AssignedTo }
        ];
    }

    private static List<FieldChangeDto> GetFieldChanges(Issue original, Issue modified)
    {
        var changes = new List<FieldChangeDto>();

        if (original.Title != modified.Title)
            changes.Add(new FieldChangeDto { FieldName = "Title", OldValue = original.Title, NewValue = modified.Title });

        if (original.Description != modified.Description)
            changes.Add(new FieldChangeDto { FieldName = "Description", OldValue = original.Description, NewValue = modified.Description });

        if (original.Status != modified.Status)
            changes.Add(new FieldChangeDto { FieldName = "Status", OldValue = original.Status.ToString(), NewValue = modified.Status.ToString() });

        if (original.Type != modified.Type)
            changes.Add(new FieldChangeDto { FieldName = "Type", OldValue = original.Type.ToString(), NewValue = modified.Type.ToString() });

        if (original.Priority != modified.Priority)
            changes.Add(new FieldChangeDto { FieldName = "Priority", OldValue = original.Priority?.ToString(), NewValue = modified.Priority?.ToString() });

        if (original.ExecutionMode != modified.ExecutionMode)
            changes.Add(new FieldChangeDto { FieldName = "ExecutionMode", OldValue = original.ExecutionMode.ToString(), NewValue = modified.ExecutionMode.ToString() });

        if (original.WorkingBranchId != modified.WorkingBranchId)
            changes.Add(new FieldChangeDto { FieldName = "WorkingBranchId", OldValue = original.WorkingBranchId, NewValue = modified.WorkingBranchId });

        if (original.AssignedTo != modified.AssignedTo)
            changes.Add(new FieldChangeDto { FieldName = "AssignedTo", OldValue = original.AssignedTo, NewValue = modified.AssignedTo });

        return changes;
    }
}

/// <summary>
/// Service for detecting conflicts between agent and main branch changes.
/// </summary>
public interface IFleeceConflictDetectionService
{
    /// <summary>
    /// Detects conflicts in the given changes.
    /// </summary>
    Task<List<IssueConflictDto>> DetectConflictsAsync(
        string projectId,
        string sessionId,
        List<IssueChangeDto> changes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of conflict detection service.
/// </summary>
public class FleeceConflictDetectionService : IFleeceConflictDetectionService
{
    private readonly IProjectService _projectService;
    private readonly IClaudeSessionService _sessionService;
    private readonly IFleeceService _fleeceService;
    private readonly ILogger<FleeceConflictDetectionService> _logger;

    public FleeceConflictDetectionService(
        IProjectService projectService,
        IClaudeSessionService sessionService,
        IFleeceService fleeceService,
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
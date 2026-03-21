using Fleece.Core.Models;
using Fleece.Core.Serialization;
using Fleece.Core.Services;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Git;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Issues;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for detecting changes between agent and main branch Fleece issues.
/// </summary>
public interface IFleeceChangeDetectionService
{
    /// <summary>
    /// Detects changes made by an agent session compared to the main branch.
    /// </summary>
    Task<List<IssueChangeDto>> DetectChangesAsync(
        string projectId,
        string sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of change detection service.
/// </summary>
public class FleeceChangeDetectionService : IFleeceChangeDetectionService
{
    private readonly IProjectService _projectService;
    private readonly IGitCloneService _cloneService;
    private readonly IClaudeSessionService _sessionService;
    private readonly IFleeceService _fleeceService;
    private readonly ILogger<FleeceChangeDetectionService> _logger;

    public FleeceChangeDetectionService(
        IProjectService projectService,
        IGitCloneService cloneService,
        IClaudeSessionService sessionService,
        IFleeceService fleeceService,
        ILogger<FleeceChangeDetectionService> logger)
    {
        _projectService = projectService;
        _cloneService = cloneService;
        _sessionService = sessionService;
        _fleeceService = fleeceService;
        _logger = logger;
    }

    public async Task<List<IssueChangeDto>> DetectChangesAsync(
        string projectId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        // Get project
        var project = await _projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            throw new ArgumentException($"Project {projectId} not found");
        }

        // Get session to find the clone path
        var session = _sessionService.GetSession(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        // Validate session is linked to an issue
        if (string.IsNullOrEmpty(session.EntityId))
        {
            throw new InvalidOperationException("Session is not linked to an issue");
        }

        var clonePath = session.WorkingDirectory;
        if (string.IsNullOrEmpty(clonePath))
        {
            throw new InvalidOperationException("Session does not have a working directory");
        }

        _logger.LogInformation(
            "Detecting changes for session {SessionId}: main branch path={MainPath}, clone path={ClonePath}",
            sessionId, project.LocalPath, clonePath);

        // Load issues from main branch (using FleeceService cache)
        var mainIssues = await _fleeceService.ListIssuesAsync(project.LocalPath);
        var mainIssueMap = mainIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug(
            "Loaded {MainCount} issues from main branch at {MainPath}",
            mainIssues.Count, project.LocalPath);

        // Load issues from agent clone
        var agentIssues = await LoadIssuesFromPathAsync(clonePath, cancellationToken);
        var agentIssueMap = agentIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug(
            "Loaded {AgentCount} issues from clone at {ClonePath}",
            agentIssues.Count, clonePath);

        var changes = new List<IssueChangeDto>();

        // Detect created and updated issues
        foreach (var agentIssue in agentIssues)
        {
            if (!mainIssueMap.TryGetValue(agentIssue.Id, out var mainIssue))
            {
                // Issue was created by agent
                changes.Add(new IssueChangeDto
                {
                    IssueId = agentIssue.Id,
                    ChangeType = ChangeType.Created,
                    Title = agentIssue.Title,
                    ModifiedIssue = agentIssue.ToDto(),
                    FieldChanges = GetAllFieldsAsChanges(agentIssue)
                });
            }
            else
            {
                // Check if issue was modified
                var fieldChanges = GetFieldChanges(mainIssue, agentIssue);
                if (fieldChanges.Any())
                {
                    changes.Add(new IssueChangeDto
                    {
                        IssueId = agentIssue.Id,
                        ChangeType = ChangeType.Updated,
                        Title = agentIssue.Title,
                        OriginalIssue = mainIssue.ToDto(),
                        ModifiedIssue = agentIssue.ToDto(),
                        FieldChanges = fieldChanges
                    });
                }
            }
        }

        // Detect deleted issues (marked as deleted, not physically removed)
        foreach (var mainIssue in mainIssues)
        {
            if (!agentIssueMap.TryGetValue(mainIssue.Id, out var agentIssue))
            {
                // Issue exists in main but not in agent - shouldn't happen unless agent deleted the file
                _logger.LogWarning("Issue {IssueId} exists in main but not in agent clone", mainIssue.Id);
            }
            else if (agentIssue.Status == IssueStatus.Deleted && mainIssue.Status != IssueStatus.Deleted)
            {
                // Issue was marked as deleted by agent
                changes.Add(new IssueChangeDto
                {
                    IssueId = mainIssue.Id,
                    ChangeType = ChangeType.Deleted,
                    Title = mainIssue.Title,
                    OriginalIssue = mainIssue.ToDto(),
                    FieldChanges = new List<FieldChangeDto>
                    {
                        new() { FieldName = "Status", OldValue = mainIssue.Status.ToString(), NewValue = "Deleted" }
                    }
                });
            }
        }

        _logger.LogInformation("Detected {Count} changes from session {SessionId}", changes.Count, sessionId);
        return changes;
    }

    private async Task<List<Issue>> LoadIssuesFromPathAsync(string path, CancellationToken cancellationToken)
    {
        var fleeceDir = Path.Combine(path, ".fleece");
        _logger.LogDebug("Looking for .fleece directory at {FleecePath}", fleeceDir);

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

            _logger.LogDebug(
                "Loaded {IssueCount} issues from {FleecePath}",
                issues.Count, fleeceDir);

            return issues.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading issues from {Path}", path);
            return [];
        }
    }

    private List<FieldChangeDto> GetFieldChanges(Issue original, Issue modified)
    {
        var changes = new List<FieldChangeDto>();

        // Check each field for changes
        if (original.Title != modified.Title)
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "Title",
                OldValue = original.Title,
                NewValue = modified.Title
            });
        }

        if (original.Description != modified.Description)
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "Description",
                OldValue = original.Description,
                NewValue = modified.Description
            });
        }

        if (original.Status != modified.Status)
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "Status",
                OldValue = original.Status.ToString(),
                NewValue = modified.Status.ToString()
            });
        }

        if (original.Type != modified.Type)
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "Type",
                OldValue = original.Type.ToString(),
                NewValue = modified.Type.ToString()
            });
        }

        if (original.Priority != modified.Priority)
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "Priority",
                OldValue = original.Priority?.ToString(),
                NewValue = modified.Priority?.ToString()
            });
        }

        if (!AreIntListsEqual(original.LinkedPRs, modified.LinkedPRs))
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "LinkedPRs",
                OldValue = string.Join(", ", original.LinkedPRs),
                NewValue = string.Join(", ", modified.LinkedPRs)
            });
        }

        if (original.WorkingBranchId != modified.WorkingBranchId)
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "WorkingBranchId",
                OldValue = original.WorkingBranchId,
                NewValue = modified.WorkingBranchId
            });
        }

        if (original.ExecutionMode != modified.ExecutionMode)
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "ExecutionMode",
                OldValue = original.ExecutionMode.ToString(),
                NewValue = modified.ExecutionMode.ToString()
            });
        }

        if (original.AssignedTo != modified.AssignedTo)
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "AssignedTo",
                OldValue = original.AssignedTo,
                NewValue = modified.AssignedTo
            });
        }

        // Check parent issues changes
        if (!AreParentIssuesEqual(original.ParentIssues, modified.ParentIssues))
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "ParentIssues",
                OldValue = SerializeParentIssues(original.ParentIssues),
                NewValue = SerializeParentIssues(modified.ParentIssues)
            });
        }

        // Check linked issues changes
        if (!AreStringListsEqual(original.LinkedIssues, modified.LinkedIssues))
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "LinkedIssues",
                OldValue = string.Join(", ", original.LinkedIssues),
                NewValue = string.Join(", ", modified.LinkedIssues)
            });
        }

        // Check tags changes
        if (!AreStringListsEqual(original.Tags, modified.Tags))
        {
            changes.Add(new FieldChangeDto
            {
                FieldName = "Tags",
                OldValue = string.Join(", ", original.Tags),
                NewValue = string.Join(", ", modified.Tags)
            });
        }

        return changes;
    }

    private List<FieldChangeDto> GetAllFieldsAsChanges(Issue issue)
    {
        return new List<FieldChangeDto>
        {
            new() { FieldName = "Title", OldValue = null, NewValue = issue.Title },
            new() { FieldName = "Description", OldValue = null, NewValue = issue.Description },
            new() { FieldName = "Status", OldValue = null, NewValue = issue.Status.ToString() },
            new() { FieldName = "Type", OldValue = null, NewValue = issue.Type.ToString() },
            new() { FieldName = "Priority", OldValue = null, NewValue = issue.Priority?.ToString() },
            new() { FieldName = "ExecutionMode", OldValue = null, NewValue = issue.ExecutionMode.ToString() },
            new() { FieldName = "WorkingBranchId", OldValue = null, NewValue = issue.WorkingBranchId },
            new() { FieldName = "AssignedTo", OldValue = null, NewValue = issue.AssignedTo }
        };
    }

    private bool AreParentIssuesEqual(IReadOnlyList<ParentIssueRef> list1, IReadOnlyList<ParentIssueRef> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        var set1 = list1.Select(p => $"{p.ParentIssue}:{p.SortOrder}").OrderBy(s => s).ToList();
        var set2 = list2.Select(p => $"{p.ParentIssue}:{p.SortOrder}").OrderBy(s => s).ToList();

        return set1.SequenceEqual(set2);
    }

    private bool AreStringListsEqual(IReadOnlyList<string> list1, IReadOnlyList<string> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        var set1 = list1.OrderBy(s => s).ToList();
        var set2 = list2.OrderBy(s => s).ToList();

        return set1.SequenceEqual(set2);
    }

    private bool AreIntListsEqual(IReadOnlyList<int> list1, IReadOnlyList<int> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        var set1 = list1.OrderBy(x => x).ToList();
        var set2 = list2.OrderBy(x => x).ToList();

        return set1.SequenceEqual(set2);
    }

    private string SerializeParentIssues(IReadOnlyList<ParentIssueRef> parents)
    {
        return string.Join(", ", parents.Select(p => $"{p.ParentIssue}:{p.SortOrder ?? "0"}"));
    }
}
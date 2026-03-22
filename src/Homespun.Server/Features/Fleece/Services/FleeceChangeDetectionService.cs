using Fleece.Core.Models;
using Fleece.Core.Serialization;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;
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
    private readonly IDiffService _diffService;
    private readonly ILogger<FleeceChangeDetectionService> _logger;
    private readonly IssueMerger _issueMerger = new();

    public FleeceChangeDetectionService(
        IProjectService projectService,
        IGitCloneService cloneService,
        IClaudeSessionService sessionService,
        IFleeceService fleeceService,
        IDiffService diffService,
        ILogger<FleeceChangeDetectionService> logger)
    {
        _projectService = projectService;
        _cloneService = cloneService;
        _sessionService = sessionService;
        _fleeceService = fleeceService;
        _diffService = diffService;
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

        var mainFleeceDir = Path.Combine(project.LocalPath, ".fleece");
        var cloneFleeceDir = Path.Combine(clonePath, ".fleece");

        // Consolidate all JSONL files into single files for IDiffService comparison
        var mainConsolidatedFile = await ConsolidateJsonlFilesAsync(mainFleeceDir, cancellationToken);
        var cloneConsolidatedFile = await ConsolidateJsonlFilesAsync(cloneFleeceDir, cancellationToken);

        // Track temp files for cleanup
        var tempFiles = new List<string>();

        try
        {
            if (mainConsolidatedFile == null || cloneConsolidatedFile == null)
            {
                _logger.LogWarning("No JSONL files found for diff - main: {MainExists}, clone: {CloneExists}",
                    mainConsolidatedFile != null, cloneConsolidatedFile != null);
                return [];
            }

            // Track if we created temp files (vs using existing single files)
            if (!mainConsolidatedFile.StartsWith(mainFleeceDir))
                tempFiles.Add(mainConsolidatedFile);
            if (!cloneConsolidatedFile.StartsWith(cloneFleeceDir))
                tempFiles.Add(cloneConsolidatedFile);

            // Use IDiffService to compare consolidated files for high-level issue identification
            var diffResult = await _diffService.CompareFilesAsync(
                mainConsolidatedFile, cloneConsolidatedFile, cancellationToken);

            _logger.LogDebug(
                "IDiffService comparison: {Created} created, {Modified} modified, {Deleted} only in main",
                diffResult.OnlyInFile2.Count, diffResult.Modified.Count, diffResult.OnlyInFile1.Count);

            var changes = new List<IssueChangeDto>();

            // Created: Issues only in clone (OnlyInFile2)
            foreach (var issue in diffResult.OnlyInFile2)
            {
                changes.Add(new IssueChangeDto
                {
                    IssueId = issue.Id,
                    ChangeType = ChangeType.Created,
                    Title = issue.Title,
                    ModifiedIssue = issue.ToDto(),
                    FieldChanges = GetAllFieldsAsChanges(issue)
                });
            }

            // Modified: Apply IssueMerger for LWW field-level merging, then GetFieldChanges
            foreach (var (mainIssue, cloneIssue) in diffResult.Modified)
            {
                var mergeResult = _issueMerger.Merge(mainIssue, cloneIssue);
                var mergedIssue = mergeResult.MergedIssue;

                var fieldChanges = GetFieldChanges(mainIssue, mergedIssue);
                if (fieldChanges.Any())
                {
                    changes.Add(new IssueChangeDto
                    {
                        IssueId = mergedIssue.Id,
                        ChangeType = ChangeType.Updated,
                        Title = mergedIssue.Title,
                        OriginalIssue = mainIssue.ToDto(),
                        ModifiedIssue = mergedIssue.ToDto(),
                        FieldChanges = fieldChanges
                    });
                }

                // Check for deletion (status changed to Deleted)
                if (mergedIssue.Status == IssueStatus.Deleted && mainIssue.Status != IssueStatus.Deleted)
                {
                    changes.Add(new IssueChangeDto
                    {
                        IssueId = mainIssue.Id,
                        ChangeType = ChangeType.Deleted,
                        Title = mainIssue.Title,
                        OriginalIssue = mainIssue.ToDto(),
                        FieldChanges = [new() { FieldName = "Status", OldValue = mainIssue.Status.ToString(), NewValue = "Deleted" }]
                    });
                }
            }

            // OnlyInFile1: Issues exist in main but not in clone
            // This shouldn't happen unless agent deleted the file physically
            foreach (var mainIssue in diffResult.OnlyInFile1)
            {
                _logger.LogWarning("Issue {IssueId} exists in main but not in agent clone", mainIssue.Id);
            }

            _logger.LogInformation("Detected {Count} changes using IDiffService from session {SessionId}",
                changes.Count, sessionId);
            return changes;
        }
        finally
        {
            // Clean up temp files
            foreach (var tempFile in tempFiles)
            {
                try { File.Delete(tempFile); }
                catch { /* ignore cleanup errors */ }
            }
        }
    }

    /// <summary>
    /// Consolidates all JSONL files in a .fleece directory into a single temp file for comparison.
    /// If only one file exists, returns that file path directly (no consolidation needed).
    /// If the directory exists but has no JSONL files, creates an empty temp file.
    /// Returns null only if the .fleece directory doesn't exist.
    /// </summary>
    private async Task<string?> ConsolidateJsonlFilesAsync(string fleeceDir, CancellationToken ct)
    {
        if (!Directory.Exists(fleeceDir))
        {
            _logger.LogDebug(".fleece directory not found at {Path}", fleeceDir);
            return null;
        }

        var jsonlFiles = Directory.GetFiles(fleeceDir, "issues_*.jsonl");
        if (jsonlFiles.Length == 0)
        {
            // Create an empty temp file for comparison (empty .fleece directory)
            _logger.LogDebug("No issues_*.jsonl files found in {Path}, creating empty temp file", fleeceDir);
            var emptyTempFile = Path.Combine(Path.GetTempPath(), $"fleece_empty_{Guid.NewGuid():N}.jsonl");
            await File.WriteAllTextAsync(emptyTempFile, "", ct);
            return emptyTempFile;
        }

        // If only one file, return it directly (no consolidation needed)
        if (jsonlFiles.Length == 1)
        {
            _logger.LogDebug("Single JSONL file found at {Path}", jsonlFiles[0]);
            return jsonlFiles[0];
        }

        _logger.LogDebug("Consolidating {Count} JSONL files from {Path}", jsonlFiles.Length, fleeceDir);

        // Load all issues from all files using JsonlStorageService (handles deduplication)
        var basePath = Path.GetDirectoryName(fleeceDir)!;
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();
        var storage = new JsonlStorageService(basePath, serializer, schemaValidator);
        var allIssues = await storage.LoadIssuesAsync(ct);

        // Write consolidated issues to a temp file
        var tempFile = Path.Combine(Path.GetTempPath(), $"fleece_consolidated_{Guid.NewGuid():N}.jsonl");
        var lines = allIssues.Select(issue => serializer.SerializeIssue(issue));
        await File.WriteAllLinesAsync(tempFile, lines, ct);

        _logger.LogDebug("Created consolidated temp file with {Count} issues at {Path}", allIssues.Count, tempFile);
        return tempFile;
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
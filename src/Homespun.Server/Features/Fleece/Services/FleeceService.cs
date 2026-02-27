using System.Collections.Concurrent;
using System.Text.Json;
using Fleece.Core.Models;
using Fleece.Core.Serialization;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Project-aware implementation of IFleeceService.
/// Uses a write-through cache pattern: reads are served from an in-memory cache,
/// while writes update the cache immediately and queue persistence to disk
/// via the <see cref="IIssueSerializationQueue"/>.
/// </summary>
public sealed class FleeceService : IFleeceService, IDisposable
{
    private readonly ConcurrentDictionary<string, IIssueService> _issueServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Issue>> _issueCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _cacheInitialized = new(StringComparer.OrdinalIgnoreCase);
    private readonly IJsonlSerializer _serializer;
    private readonly IIdGenerator _idGenerator;
    private readonly IGitConfigService _gitConfigService;
    private readonly IIssueSerializationQueue _serializationQueue;
    private readonly IIssueHistoryService _historyService;
    private readonly ILogger<FleeceService> _logger;
    private bool _disposed;

    public FleeceService(
        IIssueSerializationQueue serializationQueue,
        IIssueHistoryService historyService,
        ILogger<FleeceService> logger)
    {
        _serializationQueue = serializationQueue;
        _historyService = historyService;
        _logger = logger;
        _serializer = new JsonlSerializer();
        _idGenerator = new Sha256IdGenerator();
        _gitConfigService = new GitConfigService();
    }

    private IIssueService GetOrCreateIssueService(string projectPath)
    {
        return _issueServices.GetOrAdd(projectPath, path =>
        {
            _logger.LogDebug("Creating new IIssueService for project: {ProjectPath}", path);

            var schemaValidator = new SchemaValidator();
            var storageService = new JsonlStorageService(path, _serializer, schemaValidator);
            // Note: ChangeService was removed in Fleece.Core v1.2.0
            return new IssueService(storageService, _idGenerator, _gitConfigService);
        });
    }

    /// <summary>
    /// Ensures the in-memory cache for a project is populated from Fleece.Core (disk).
    /// Only loads on first access; subsequent calls are no-ops.
    /// </summary>
    private async Task<ConcurrentDictionary<string, Issue>> EnsureCacheLoadedAsync(string projectPath, CancellationToken ct)
    {
        var cache = _issueCache.GetOrAdd(projectPath, _ => new ConcurrentDictionary<string, Issue>(StringComparer.OrdinalIgnoreCase));

        if (!_cacheInitialized.TryGetValue(projectPath, out var initialized) || !initialized)
        {
            var service = GetOrCreateIssueService(projectPath);
            var allIssues = await service.GetAllAsync(ct);
            foreach (var issue in allIssues)
            {
                cache[issue.Id] = issue;
            }
            _cacheInitialized[projectPath] = true;

            _logger.LogDebug("Loaded {Count} issues into cache for project: {ProjectPath}", allIssues.Count, projectPath);
        }

        return cache;
    }

    #region Cache Management

    public async Task ReloadFromDiskAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("Reloading issues from disk for project: {ProjectPath}", projectPath);

        // Remove the cached IssueService so a fresh one reads current disk state
        _issueServices.TryRemove(projectPath, out _);

        // Clear the cache initialized flag and existing cache
        _cacheInitialized.TryRemove(projectPath, out _);
        _issueCache.TryRemove(projectPath, out _);

        // Force re-read from disk
        await EnsureCacheLoadedAsync(projectPath, ct);

        _logger.LogInformation("Reloaded issues from disk for project: {ProjectPath}", projectPath);
    }

    #endregion

    #region Read Operations

    public async Task<Issue?> GetIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        return cache.TryGetValue(issueId, out var issue) ? issue : null;
    }

    public async Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string projectPath,
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        IEnumerable<Issue> issues = cache.Values;

        if (status.HasValue)
        {
            issues = issues.Where(i => i.Status == status.Value);
        }

        if (type.HasValue)
        {
            issues = issues.Where(i => i.Type == type.Value);
        }

        if (priority.HasValue)
        {
            issues = issues.Where(i => i.Priority == priority.Value);
        }

        // If no filters specified, exclude deleted/archived/closed/complete (matching original behavior)
        if (!status.HasValue && !type.HasValue && !priority.HasValue)
        {
            issues = issues.Where(i => i.Status is not (IssueStatus.Deleted or IssueStatus.Archived or IssueStatus.Closed or IssueStatus.Complete));
        }

        return issues.ToList();
    }

    public async Task<IReadOnlyList<Issue>> GetReadyIssuesAsync(string projectPath, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        var allIssues = cache.Values.ToList();
        var openIssues = allIssues.Where(i => i.Status is IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review).ToList();

        // Filter to issues that have no blocking parent issues (parents that are not Complete/Closed)
        var issueMap = allIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

        return openIssues
            .Where(issue =>
            {
                // If no parent issues, it's ready
                if (issue.ParentIssues.Count == 0)
                {
                    return true;
                }

                // Check all parent issues - if all are Complete or Closed, this issue is ready
                return issue.ParentIssues.All(parentRef =>
                {
                    if (issueMap.TryGetValue(parentRef.ParentIssue, out var parent))
                    {
                        return parent.Status is IssueStatus.Complete or IssueStatus.Closed;
                    }
                    // If parent doesn't exist, assume it's done
                    return true;
                });
            })
            .ToList();
    }

    #endregion

    #region Write Operations

    public async Task<Issue> CreateIssueAsync(
        string projectPath,
        string title,
        IssueType type,
        string? description = null,
        int? priority = null,
        ExecutionMode? executionMode = null,
        IssueStatus? status = null,
        CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        // Use Fleece.Core to create the issue (this also persists to disk, but we'll
        // rely on this for generating the ID and applying defaults)
        var service = GetOrCreateIssueService(projectPath);
        var issue = await service.CreateAsync(
            title: title,
            type: type,
            description: description,
            priority: priority,
            executionMode: executionMode,
            cancellationToken: ct);

        // If a specific status was requested (other than the default Open), update the issue
        if (status.HasValue && status.Value != IssueStatus.Open)
        {
            issue = await service.UpdateAsync(issue.Id, status: status.Value, cancellationToken: ct);
        }

        // Update the in-memory cache immediately
        cache[issue.Id] = issue;

        // Queue an async re-serialization to ensure consistency
        await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
            ProjectPath: projectPath,
            IssueId: issue.Id,
            Type: WriteOperationType.Create,
            WriteAction: async (innerCt) =>
            {
                // The issue is already persisted by the CreateAsync call above.
                // This queue entry serves as a consistency checkpoint - if the initial
                // write was interrupted, the background service ensures eventual persistence.
                var svc = GetOrCreateIssueService(projectPath);
                var existing = await svc.GetByIdAsync(issue.Id, innerCt);
                if (existing == null)
                {
                    // Re-create if the initial write was lost
                    await svc.CreateAsync(
                        title: issue.Title,
                        type: issue.Type,
                        description: issue.Description,
                        priority: issue.Priority,
                        executionMode: issue.ExecutionMode,
                        cancellationToken: innerCt);
                }
            },
            QueuedAt: DateTimeOffset.UtcNow
        ), ct);

        _logger.LogInformation(
            "Created issue '{IssueId}' ({Type}): {Title}{ExecutionMode}{Status}",
            issue.Id,
            type,
            title,
            executionMode.HasValue ? $" [ExecutionMode: {executionMode}]" : "",
            status.HasValue && status.Value != IssueStatus.Open ? $" [Status: {status}]" : "");

        // Record history snapshot after create
        await RecordHistorySnapshotAsync(projectPath, "Create", issue.Id, $"Created '{title}'", ct);

        return issue;
    }

    public async Task<Issue?> UpdateIssueAsync(
        string projectPath,
        string issueId,
        string? title = null,
        IssueStatus? status = null,
        IssueType? type = null,
        string? description = null,
        int? priority = null,
        ExecutionMode? executionMode = null,
        string? workingBranchId = null,
        CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        // Check if the issue exists in cache
        if (!cache.TryGetValue(issueId, out _))
        {
            _logger.LogWarning("Issue '{IssueId}' not found in project '{ProjectPath}'", issueId, projectPath);
            return null;
        }

        // Perform the update via Fleece.Core (synchronous I/O)
        var service = GetOrCreateIssueService(projectPath);
        try
        {
            var issue = await service.UpdateAsync(
                id: issueId,
                title: title,
                status: status,
                type: type,
                description: description,
                priority: priority,
                executionMode: executionMode,
                workingBranchId: workingBranchId,
                cancellationToken: ct);

            // Update the in-memory cache immediately
            cache[issueId] = issue;

            // Queue a background persistence operation for consistency
            await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
                ProjectPath: projectPath,
                IssueId: issueId,
                Type: WriteOperationType.Update,
                WriteAction: async (innerCt) =>
                {
                    var svc = GetOrCreateIssueService(projectPath);
                    await svc.UpdateAsync(
                        id: issueId,
                        title: title,
                        status: status,
                        type: type,
                        description: description,
                        priority: priority,
                        executionMode: executionMode,
                        workingBranchId: workingBranchId,
                        cancellationToken: innerCt);
                },
                QueuedAt: DateTimeOffset.UtcNow
            ), ct);

            var changes = new List<string>();
            if (title != null) changes.Add($"title='{title}'");
            if (status != null) changes.Add($"status={status}");
            if (type != null) changes.Add($"type={type}");
            if (description != null) changes.Add("description updated");
            if (priority != null) changes.Add($"priority={priority}");
            if (executionMode != null) changes.Add($"executionMode={executionMode}");
            if (workingBranchId != null) changes.Add($"workingBranchId='{workingBranchId}'");

            _logger.LogInformation(
                "Updated issue '{IssueId}': {Changes}",
                issueId,
                string.Join(", ", changes));

            // Record history snapshot after update
            var historyDescription = changes.Count > 0 ? $"Updated: {string.Join(", ", changes)}" : "Updated issue";
            await RecordHistorySnapshotAsync(projectPath, "Update", issueId, historyDescription, ct);

            return issue;
        }
        catch (KeyNotFoundException)
        {
            // Issue was in our cache but not in Fleece.Core - remove from cache
            cache.TryRemove(issueId, out _);
            _logger.LogWarning("Issue '{IssueId}' not found in project '{ProjectPath}'", issueId, projectPath);
            return null;
        }
    }

    public async Task<bool> DeleteIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        var service = GetOrCreateIssueService(projectPath);
        var deleted = await service.DeleteAsync(issueId, ct);

        if (deleted)
        {
            // Update cache: get the updated issue (now marked as Deleted) from Fleece.Core
            var updatedIssue = await service.GetByIdAsync(issueId, ct);
            if (updatedIssue != null)
            {
                cache[issueId] = updatedIssue;
            }
            else
            {
                cache.TryRemove(issueId, out _);
            }

            // Queue background persistence for consistency
            await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
                ProjectPath: projectPath,
                IssueId: issueId,
                Type: WriteOperationType.Delete,
                WriteAction: async (innerCt) =>
                {
                    var svc = GetOrCreateIssueService(projectPath);
                    await svc.DeleteAsync(issueId, innerCt);
                },
                QueuedAt: DateTimeOffset.UtcNow
            ), ct);

            _logger.LogInformation("Deleted issue '{IssueId}'", issueId);

            // Record history snapshot after delete
            await RecordHistorySnapshotAsync(projectPath, "Delete", issueId, $"Deleted issue '{issueId}'", ct);
        }
        else
        {
            _logger.LogWarning("Failed to delete issue '{IssueId}' - not found", issueId);
        }

        return deleted;
    }

    public async Task<Issue> AddParentAsync(string projectPath, string childId, string parentId, string? sortOrder = null, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        // Check if the child issue exists in cache
        if (!cache.TryGetValue(childId, out var existingIssue))
        {
            _logger.LogWarning("Child issue '{ChildId}' not found in project '{ProjectPath}'", childId, projectPath);
            throw new KeyNotFoundException($"Issue '{childId}' not found");
        }

        // Build the new parent issues list with the added parent
        var newParentIssues = existingIssue.ParentIssues.ToList();
        newParentIssues.Add(new ParentIssueRef { ParentIssue = parentId, SortOrder = sortOrder ?? "0" });

        // Perform the update via Fleece.Core
        var service = GetOrCreateIssueService(projectPath);
        var issue = await service.UpdateAsync(childId, parentIssues: newParentIssues, cancellationToken: ct);

        // Update the in-memory cache immediately
        cache[childId] = issue;

        // Queue a background persistence operation for consistency
        await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
            ProjectPath: projectPath,
            IssueId: childId,
            Type: WriteOperationType.Update,
            WriteAction: async (innerCt) =>
            {
                var svc = GetOrCreateIssueService(projectPath);
                var currentIssue = await svc.GetByIdAsync(childId, innerCt);
                if (currentIssue != null)
                {
                    var updatedParents = currentIssue.ParentIssues.ToList();
                    if (!updatedParents.Any(p => p.ParentIssue == parentId))
                    {
                        updatedParents.Add(new ParentIssueRef { ParentIssue = parentId, SortOrder = sortOrder ?? "0" });
                        await svc.UpdateAsync(childId, parentIssues: updatedParents, cancellationToken: innerCt);
                    }
                }
            },
            QueuedAt: DateTimeOffset.UtcNow
        ), ct);

        _logger.LogInformation("Added parent '{ParentId}' to issue '{ChildId}'", parentId, childId);

        // Record history snapshot after adding parent (merge operation)
        await RecordHistorySnapshotAsync(projectPath, "AddParent", childId, $"Linked '{childId}' to parent '{parentId}'", ct);

        return issue;
    }

    public async Task<Issue> RemoveParentAsync(string projectPath, string childId, string parentId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        // Check if the child issue exists in cache
        if (!cache.TryGetValue(childId, out var existingIssue))
        {
            _logger.LogWarning("Child issue '{ChildId}' not found in project '{ProjectPath}'", childId, projectPath);
            throw new KeyNotFoundException($"Issue '{childId}' not found");
        }

        // Build the new parent issues list without the removed parent
        var newParentIssues = existingIssue.ParentIssues
            .Where(p => p.ParentIssue != parentId)
            .ToList();

        // Perform the update via Fleece.Core
        var service = GetOrCreateIssueService(projectPath);
        var issue = await service.UpdateAsync(childId, parentIssues: newParentIssues, cancellationToken: ct);

        // Update the in-memory cache immediately
        cache[childId] = issue;

        // Queue a background persistence operation for consistency
        await _serializationQueue.EnqueueAsync(new IssueWriteOperation(
            ProjectPath: projectPath,
            IssueId: childId,
            Type: WriteOperationType.Update,
            WriteAction: async (innerCt) =>
            {
                var svc = GetOrCreateIssueService(projectPath);
                var currentIssue = await svc.GetByIdAsync(childId, innerCt);
                if (currentIssue != null)
                {
                    var updatedParents = currentIssue.ParentIssues
                        .Where(p => p.ParentIssue != parentId)
                        .ToList();
                    await svc.UpdateAsync(childId, parentIssues: updatedParents, cancellationToken: innerCt);
                }
            },
            QueuedAt: DateTimeOffset.UtcNow
        ), ct);

        _logger.LogInformation("Removed parent '{ParentId}' from issue '{ChildId}'", parentId, childId);

        // Record history snapshot after removing parent
        await RecordHistorySnapshotAsync(projectPath, "RemoveParent", childId, $"Unlinked '{childId}' from parent '{parentId}'", ct);

        return issue;
    }

    #endregion

    #region History Operations

    /// <summary>
    /// Records a history snapshot of the current issues state.
    /// </summary>
    private async Task RecordHistorySnapshotAsync(
        string projectPath,
        string operationType,
        string? issueId,
        string? description,
        CancellationToken ct)
    {
        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);
            var issues = cache.Values.ToList();
            await _historyService.RecordSnapshotAsync(projectPath, issues, operationType, issueId, description, ct);
        }
        catch (Exception ex)
        {
            // Don't fail the main operation if history recording fails
            _logger.LogWarning(ex, "Failed to record history snapshot for {OperationType}", operationType);
        }
    }

    /// <summary>
    /// Applies issues from a history snapshot to the cache and persists to disk.
    /// Used by undo/redo operations.
    /// </summary>
    public async Task ApplyHistorySnapshotAsync(string projectPath, IReadOnlyList<Issue> issues, CancellationToken ct = default)
    {
        // Clear and repopulate cache with snapshot issues
        var cache = _issueCache.GetOrAdd(projectPath, _ => new ConcurrentDictionary<string, Issue>(StringComparer.OrdinalIgnoreCase));
        cache.Clear();
        foreach (var issue in issues)
        {
            cache[issue.Id] = issue;
        }
        _cacheInitialized[projectPath] = true;

        // Write the issues directly to the .fleece/ folder using the serializer
        var fleeceDir = Path.Combine(projectPath, ".fleece");
        Directory.CreateDirectory(fleeceDir);

        // Find and delete existing issues file(s)
        var existingFiles = Directory.GetFiles(fleeceDir, "issues_*.jsonl");
        foreach (var file in existingFiles)
        {
            File.Delete(file);
        }

        // Write new issues file with a deterministic name based on content hash
        var issuesFile = Path.Combine(fleeceDir, $"issues_{Guid.NewGuid().ToString()[..6]}.jsonl");
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await using (var writer = new StreamWriter(issuesFile, false))
        {
            foreach (var issue in issues)
            {
                var json = JsonSerializer.Serialize(issue, jsonOptions);
                await writer.WriteLineAsync(json);
            }
        }

        // Reset the issue service so it reloads from the new file
        _issueServices.TryRemove(projectPath, out _);

        _logger.LogInformation("Applied history snapshot with {Count} issues to project {ProjectPath}", issues.Count, projectPath);
    }

    #endregion

    #region Task Graph Operations

    public async Task<TaskGraph?> GetTaskGraphAsync(string projectPath, CancellationToken ct = default)
    {
        return await GetTaskGraphWithAdditionalIssuesAsync(projectPath, additionalIssueIds: null, ct);
    }

    public async Task<TaskGraph?> GetTaskGraphWithAdditionalIssuesAsync(
        string projectPath,
        IEnumerable<string>? additionalIssueIds,
        CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        // Build a set of additional issue IDs for case-insensitive lookup
        var additionalIds = additionalIssueIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        // Get issues that are either:
        // 1. In an open status (Open, Progress, Review)
        // 2. OR in the additionalIssueIds set (regardless of status)
        var includedIssues = cache.Values
            .Where(i => i.Status is IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review
                        || additionalIds.Contains(i.Id))
            .ToList();

        if (includedIssues.Count == 0)
        {
            _logger.LogDebug("No issues found for task graph in project: {ProjectPath}", projectPath);
            return null;
        }

        _logger.LogDebug(
            "Building task graph with {TotalCount} issues ({OpenCount} open, {AdditionalCount} additional) for project: {ProjectPath}",
            includedIssues.Count,
            includedIssues.Count(i => i.Status is IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review),
            includedIssues.Count(i => additionalIds.Contains(i.Id) && i.Status is not (IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review)),
            projectPath);

        // Create a temporary IssueService with only the included issues
        // This allows Fleece.Core's BuildTaskGraphLayoutAsync to work correctly
        var mockStorage = new InMemoryGraphStorageService(includedIssues);
        var issueService = new global::Fleece.Core.Services.IssueService(
            mockStorage,
            _idGenerator,
            _gitConfigService);

        var taskGraph = await issueService.BuildTaskGraphLayoutAsync(ct);

        _logger.LogDebug(
            "Built task graph with {NodeCount} nodes and {TotalLanes} lanes for project: {ProjectPath}",
            taskGraph.Nodes.Count, taskGraph.TotalLanes, projectPath);

        return taskGraph;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Clear all caches
        _issueServices.Clear();
        _issueCache.Clear();
        _cacheInitialized.Clear();
    }
}

/// <summary>
/// In-memory storage service for building task graphs with a subset of issues.
/// This allows us to create a temporary IssueService with only the issues we want
/// to include in the graph, while using Fleece.Core's graph building logic.
/// </summary>
internal class InMemoryGraphStorageService : global::Fleece.Core.Services.Interfaces.IStorageService
{
    private readonly IReadOnlyList<Issue> _issues;

    public InMemoryGraphStorageService(IReadOnlyList<Issue> issues)
    {
        _issues = issues;
    }

    public Task<IReadOnlyList<Issue>> LoadIssuesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_issues);

    public Task SaveIssuesAsync(IReadOnlyList<Issue> issues, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AppendIssueAsync(Issue issue, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task EnsureDirectoryExistsAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetAllIssueFilesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<Issue>> LoadIssuesFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult(_issues);

    public Task DeleteIssueFileAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string> SaveIssuesWithHashAsync(IReadOnlyList<Issue> issues, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);

    public Task<(bool HasMultiple, string Message)> HasMultipleUnmergedFilesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((false, string.Empty));

    public Task<LoadIssuesResult> LoadIssuesWithDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new LoadIssuesResult { Issues = _issues.ToList(), Diagnostics = [] });

    public Task<IReadOnlyList<Tombstone>> LoadTombstonesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Tombstone>>([]);

    public Task SaveTombstonesAsync(IReadOnlyList<Tombstone> tombstones, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AppendTombstonesAsync(IReadOnlyList<Tombstone> tombstones, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetAllTombstoneFilesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}

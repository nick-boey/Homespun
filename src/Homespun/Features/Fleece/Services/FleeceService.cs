using System.Collections.Concurrent;
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
    private readonly ILogger<FleeceService> _logger;
    private bool _disposed;

    public FleeceService(IIssueSerializationQueue serializationQueue, ILogger<FleeceService> logger)
    {
        _serializationQueue = serializationQueue;
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
            var changeService = new ChangeService(storageService);
            return new IssueService(storageService, _idGenerator, _gitConfigService, changeService);
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
        }
        else
        {
            _logger.LogWarning("Failed to delete issue '{IssueId}' - not found", issueId);
        }

        return deleted;
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

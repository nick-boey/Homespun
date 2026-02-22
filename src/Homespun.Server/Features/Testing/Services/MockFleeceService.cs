using System.Collections.Concurrent;
using Fleece.Core.Models;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;
using Homespun.Features.Fleece.Services;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IFleeceService with in-memory issue storage per project.
/// Since Fleece.Core.Models.Issue has init-only properties, this service returns
/// new Issue instances on updates rather than modifying existing ones.
/// </summary>
public class MockFleeceService : IFleeceService
{
    private readonly ConcurrentDictionary<string, List<Issue>> _issuesByProject = new();
    private readonly ILogger<MockFleeceService> _logger;
    private int _nextIssueNumber = 1;

    public MockFleeceService(ILogger<MockFleeceService> logger)
    {
        _logger = logger;
    }

    public Task<Issue?> GetIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] GetIssue {IssueId} from {ProjectPath}", issueId, projectPath);

        if (_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            lock (issues)
            {
                var issue = issues.FirstOrDefault(i => i.Id == issueId);
                return Task.FromResult(issue);
            }
        }

        return Task.FromResult<Issue?>(null);
    }

    public Task<IReadOnlyList<Issue>> ListIssuesAsync(
        string projectPath,
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] ListIssues from {ProjectPath}", projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        List<Issue> snapshot;
        lock (issues)
        {
            snapshot = issues.ToList();
        }

        var filtered = snapshot.AsEnumerable();

        if (status.HasValue)
        {
            filtered = filtered.Where(i => i.Status == status.Value);
        }
        else
        {
            // When no status filter specified, exclude terminal statuses (matching FleeceService behavior)
            filtered = filtered.Where(i => i.Status is not (
                IssueStatus.Deleted or IssueStatus.Archived or
                IssueStatus.Closed or IssueStatus.Complete));
        }

        if (type.HasValue)
        {
            filtered = filtered.Where(i => i.Type == type.Value);
        }

        if (priority.HasValue)
        {
            filtered = filtered.Where(i => i.Priority == priority.Value);
        }

        return Task.FromResult<IReadOnlyList<Issue>>(filtered.ToList());
    }

    public Task<IReadOnlyList<Issue>> GetReadyIssuesAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] GetReadyIssues from {ProjectPath}", projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult<IReadOnlyList<Issue>>(Array.Empty<Issue>());
        }

        List<Issue> snapshot;
        lock (issues)
        {
            snapshot = issues.ToList();
        }

        // Ready issues are those in Open or Progress status
        var readyIssues = snapshot
            .Where(i => i.Status is IssueStatus.Open or IssueStatus.Progress)
            .ToList();

        return Task.FromResult<IReadOnlyList<Issue>>(readyIssues);
    }

    public Task ReloadFromDiskAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] ReloadFromDisk for {ProjectPath} (no-op in mock)", projectPath);
        return Task.CompletedTask;
    }

    public Task<Issue> CreateIssueAsync(
        string projectPath,
        string title,
        IssueType type,
        string? description = null,
        int? priority = null,
        ExecutionMode? executionMode = null,
        IssueStatus? status = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] CreateIssue '{Title}' in {ProjectPath}", title, projectPath);

        var now = DateTime.UtcNow;
        var issue = new Issue
        {
            Id = GenerateIssueId(type),
            Title = title,
            Description = description ?? string.Empty,
            Type = type,
            Status = status ?? IssueStatus.Open,
            Priority = priority ?? 3,
            ExecutionMode = executionMode ?? ExecutionMode.Series,
            CreatedAt = now,
            LastUpdate = now
        };

        var issues = _issuesByProject.GetOrAdd(projectPath, _ => []);
        lock (issues)
        {
            issues.Add(issue);
        }

        return Task.FromResult(issue);
    }

    public Task<Issue?> UpdateIssueAsync(
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
        _logger.LogDebug("[Mock] UpdateIssue {IssueId} in {ProjectPath}", issueId, projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult<Issue?>(null);
        }

        lock (issues)
        {
            var existingIndex = issues.FindIndex(i => i.Id == issueId);
            if (existingIndex < 0)
            {
                return Task.FromResult<Issue?>(null);
            }

            var existing = issues[existingIndex];

            // Create a new issue with updated values (Issue has init-only properties)
            // Preserve all existing properties not being updated
            var updated = new Issue
            {
                Id = existing.Id,
                Title = title ?? existing.Title,
                Description = description ?? existing.Description,
                Type = type ?? existing.Type,
                Status = status ?? existing.Status,
                Priority = priority ?? existing.Priority,
                ExecutionMode = executionMode ?? existing.ExecutionMode,
                WorkingBranchId = workingBranchId ?? existing.WorkingBranchId,
                ParentIssues = existing.ParentIssues,
                Tags = existing.Tags,
                LinkedIssues = existing.LinkedIssues,
                LinkedPR = existing.LinkedPR,
                CreatedBy = existing.CreatedBy,
                AssignedTo = existing.AssignedTo,
                CreatedAt = existing.CreatedAt,
                LastUpdate = DateTime.UtcNow
            };

            issues[existingIndex] = updated;
            return Task.FromResult<Issue?>(updated);
        }
    }

    public Task<bool> DeleteIssueAsync(string projectPath, string issueId, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] DeleteIssue {IssueId} from {ProjectPath}", issueId, projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult(false);
        }

        lock (issues)
        {
            var existingIndex = issues.FindIndex(i => i.Id == issueId);
            if (existingIndex < 0)
            {
                return Task.FromResult(false);
            }

            var existing = issues[existingIndex];

            // Create a new issue with Deleted status (Issue has init-only properties)
            // Preserve all existing properties
            var deleted = new Issue
            {
                Id = existing.Id,
                Title = existing.Title,
                Description = existing.Description,
                Type = existing.Type,
                Status = IssueStatus.Deleted,
                Priority = existing.Priority,
                ExecutionMode = existing.ExecutionMode,
                WorkingBranchId = existing.WorkingBranchId,
                ParentIssues = existing.ParentIssues,
                Tags = existing.Tags,
                LinkedIssues = existing.LinkedIssues,
                LinkedPR = existing.LinkedPR,
                CreatedBy = existing.CreatedBy,
                AssignedTo = existing.AssignedTo,
                CreatedAt = existing.CreatedAt,
                LastUpdate = DateTime.UtcNow
            };

            issues[existingIndex] = deleted;
        }

        return Task.FromResult(true);
    }

    public Task<Issue> AddParentAsync(string projectPath, string childId, string parentId, string? sortOrder = null, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] AddParent {ParentId} to {ChildId} in {ProjectPath}", parentId, childId, projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            throw new KeyNotFoundException($"Issue '{childId}' not found");
        }

        lock (issues)
        {
            var existingIndex = issues.FindIndex(i => i.Id == childId);
            if (existingIndex < 0)
            {
                throw new KeyNotFoundException($"Issue '{childId}' not found");
            }

            var existing = issues[existingIndex];

            // Create a new list with the added parent
            var newParentIssues = existing.ParentIssues.ToList();
            newParentIssues.Add(new ParentIssueRef { ParentIssue = parentId, SortOrder = sortOrder ?? "0" });

            // Create a new issue with the updated parent list (Issue has init-only properties)
            var updated = new Issue
            {
                Id = existing.Id,
                Title = existing.Title,
                Description = existing.Description,
                Type = existing.Type,
                Status = existing.Status,
                Priority = existing.Priority,
                ExecutionMode = existing.ExecutionMode,
                WorkingBranchId = existing.WorkingBranchId,
                ParentIssues = newParentIssues,
                Tags = existing.Tags,
                LinkedIssues = existing.LinkedIssues,
                LinkedPR = existing.LinkedPR,
                CreatedBy = existing.CreatedBy,
                AssignedTo = existing.AssignedTo,
                CreatedAt = existing.CreatedAt,
                LastUpdate = DateTime.UtcNow
            };

            issues[existingIndex] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<Issue> RemoveParentAsync(string projectPath, string childId, string parentId, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] RemoveParent {ParentId} from {ChildId} in {ProjectPath}", parentId, childId, projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            throw new KeyNotFoundException($"Issue '{childId}' not found");
        }

        lock (issues)
        {
            var existingIndex = issues.FindIndex(i => i.Id == childId);
            if (existingIndex < 0)
            {
                throw new KeyNotFoundException($"Issue '{childId}' not found");
            }

            var existing = issues[existingIndex];

            // Create a new list without the removed parent
            var newParentIssues = existing.ParentIssues
                .Where(p => p.ParentIssue != parentId)
                .ToList();

            // Create a new issue with the updated parent list (Issue has init-only properties)
            var updated = new Issue
            {
                Id = existing.Id,
                Title = existing.Title,
                Description = existing.Description,
                Type = existing.Type,
                Status = existing.Status,
                Priority = existing.Priority,
                ExecutionMode = existing.ExecutionMode,
                WorkingBranchId = existing.WorkingBranchId,
                ParentIssues = newParentIssues,
                Tags = existing.Tags,
                LinkedIssues = existing.LinkedIssues,
                LinkedPR = existing.LinkedPR,
                CreatedBy = existing.CreatedBy,
                AssignedTo = existing.AssignedTo,
                CreatedAt = existing.CreatedAt,
                LastUpdate = DateTime.UtcNow
            };

            issues[existingIndex] = updated;
            return Task.FromResult(updated);
        }
    }

    /// <summary>
    /// Seeds an issue directly for testing/demo purposes.
    /// </summary>
    public void SeedIssue(string projectPath, Issue issue)
    {
        var issues = _issuesByProject.GetOrAdd(projectPath, _ => []);
        lock (issues)
        {
            issues.Add(issue);
        }
    }

    /// <summary>
    /// Clears all issues. Useful for test isolation.
    /// </summary>
    public void Clear()
    {
        _issuesByProject.Clear();
    }

    public Task<TaskGraph?> GetTaskGraphAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("[Mock] GetTaskGraph from {ProjectPath}", projectPath);

        if (!_issuesByProject.TryGetValue(projectPath, out var issues))
        {
            return Task.FromResult<TaskGraph?>(null);
        }

        List<Issue> snapshot;
        lock (issues)
        {
            snapshot = issues.ToList();
        }

        var openIssues = snapshot
            .Where(i => i.Status is IssueStatus.Open or IssueStatus.Progress or IssueStatus.Review)
            .ToList();

        if (openIssues.Count == 0)
        {
            return Task.FromResult<TaskGraph?>(null);
        }

        // Build a mock TaskGraph - use a MockIssueService to satisfy the TaskGraphService dependencies
        var mockIssueService = new MockIssueServiceAdapter(openIssues);
        var nextService = new NextService(mockIssueService);
        var taskGraphService = new TaskGraphService(mockIssueService, nextService);

        return taskGraphService.BuildGraphAsync(ct);
    }

    private string GenerateIssueId(IssueType type)
    {
        var prefix = type switch
        {
            IssueType.Task => "task",
            IssueType.Bug => "bug",
            IssueType.Feature => "feat",
            _ => "issue"
        };

        var number = Interlocked.Increment(ref _nextIssueNumber);
        var randomPart = Guid.NewGuid().ToString("N")[..6];
        return $"{prefix}/{randomPart}";
    }
}

/// <summary>
/// Adapter to satisfy IIssueService interface for TaskGraphService with a list of in-memory issues.
/// Only implements the read methods required by TaskGraphService; write methods throw NotImplementedException.
/// </summary>
internal class MockIssueServiceAdapter(IReadOnlyList<Issue> issues) : IIssueService
{
    public Task<IReadOnlyList<Issue>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(issues);

    public Task<Issue?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(issues.FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<Issue>> ResolveByPartialIdAsync(string partialId, CancellationToken cancellationToken = default)
    {
        var matches = issues.Where(i => i.Id.Contains(partialId, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult<IReadOnlyList<Issue>>(matches);
    }

    public Task<IReadOnlyList<Issue>> FilterAsync(
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        string? assignedTo = null,
        IReadOnlyList<string>? tags = null,
        int? linkedPr = null,
        bool includeTerminal = false,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Issue> result = issues;
        if (status.HasValue) result = result.Where(i => i.Status == status.Value);
        if (type.HasValue) result = result.Where(i => i.Type == type.Value);
        if (priority.HasValue) result = result.Where(i => i.Priority == priority.Value);
        if (!includeTerminal) result = result.Where(i => i.Status is not (IssueStatus.Complete or IssueStatus.Closed or IssueStatus.Archived or IssueStatus.Deleted));
        return Task.FromResult<IReadOnlyList<Issue>>(result.ToList());
    }

    public Task<IReadOnlyList<Issue>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        var results = issues.Where(i =>
            i.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            (i.Description?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false));
        return Task.FromResult<IReadOnlyList<Issue>>(results.ToList());
    }

    // Write methods - not needed for TaskGraphService, throw NotImplementedException
    public Task<Issue> CreateAsync(
        string title,
        IssueType type,
        string? description = null,
        IssueStatus status = IssueStatus.Open,
        int? priority = null,
        int? linkedPr = null,
        IReadOnlyList<string>? linkedIssues = null,
        IReadOnlyList<ParentIssueRef>? parentIssues = null,
        string? assignedTo = null,
        IReadOnlyList<string>? tags = null,
        string? workingBranchId = null,
        ExecutionMode? executionMode = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Mock does not support create");

    public Task<Issue> UpdateAsync(
        string id,
        string? title = null,
        string? description = null,
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        int? linkedPr = null,
        IReadOnlyList<string>? linkedIssues = null,
        IReadOnlyList<ParentIssueRef>? parentIssues = null,
        string? assignedTo = null,
        IReadOnlyList<string>? tags = null,
        string? workingBranchId = null,
        ExecutionMode? executionMode = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Mock does not support update");

    public Task<Issue> UpdateQuestionsAsync(string id, IReadOnlyList<Question> questions, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Mock does not support UpdateQuestions");

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Mock does not support delete");
}

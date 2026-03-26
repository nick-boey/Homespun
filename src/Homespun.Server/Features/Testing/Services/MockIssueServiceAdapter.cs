using Fleece.Core.Models;
using Fleece.Core.Services;
using Fleece.Core.Services.Interfaces;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Adapter to satisfy IIssueService interface with a list of in-memory issues.
/// Uses Fleece.Core's IssueService via IssueServiceFactory with a mock storage service
/// to provide full graph functionality (BuildTaskGraphLayoutAsync, etc.).
/// </summary>
internal class MockIssueServiceAdapter : IIssueService
{
    private readonly IIssueService _innerService;

    public MockIssueServiceAdapter(IReadOnlyList<Issue> issues)
    {
        // Create a mock storage service that returns our issues
        var mockStorage = new InMemoryStorageService(issues);
        var idGenerator = new Sha256IdGenerator();
        var gitConfigService = new GitConfigService();
        var tagService = new TagService();
        _innerService = new IssueService(mockStorage, idGenerator, gitConfigService, tagService);
    }

    // Delegate all methods to the inner IssueService which has full graph implementation
    public Task<IReadOnlyList<Issue>> GetAllAsync(CancellationToken cancellationToken = default)
        => _innerService.GetAllAsync(cancellationToken);

    public Task<Issue?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => _innerService.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Issue>> ResolveByPartialIdAsync(string partialId, CancellationToken cancellationToken = default)
        => _innerService.ResolveByPartialIdAsync(partialId, cancellationToken);

    public Task<IReadOnlyList<Issue>> FilterAsync(
        IssueStatus? status = null,
        IssueType? type = null,
        int? priority = null,
        string? assignedTo = null,
        IReadOnlyList<string>? tags = null,
        int? linkedPr = null,
        bool includeTerminal = false,
        CancellationToken cancellationToken = default)
        => _innerService.FilterAsync(status, type, priority, assignedTo, tags, linkedPr, includeTerminal, cancellationToken);

    public Task<IReadOnlyList<Issue>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
        => _innerService.SearchAsync(searchTerm, cancellationToken);

    // Write methods - delegate but these shouldn't be used in mock scenarios
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
        => _innerService.CreateAsync(title, type, description, status, priority, linkedPr, linkedIssues, parentIssues, assignedTo, tags, workingBranchId, executionMode, cancellationToken);

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
        => _innerService.UpdateAsync(id, title, description, status, type, priority, linkedPr, linkedIssues, parentIssues, assignedTo, tags, workingBranchId, executionMode, cancellationToken);

    public Task<Issue> UpdateQuestionsAsync(string id, IReadOnlyList<Question> questions, CancellationToken cancellationToken = default)
        => _innerService.UpdateQuestionsAsync(id, questions, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => _innerService.DeleteAsync(id, cancellationToken);

    // Graph methods - now built into IssueService in Fleece.Core v1.4.0
    public Task<IssueGraph> BuildGraphAsync(CancellationToken cancellationToken = default)
        => _innerService.BuildGraphAsync(cancellationToken);

    public Task<IssueGraph> QueryGraphAsync(GraphQuery query, GraphSortConfig? sortConfig = null, CancellationToken cancellationToken = default)
        => _innerService.QueryGraphAsync(query, sortConfig, cancellationToken);

    public Task<IReadOnlyList<Issue>> GetNextIssuesAsync(string? parentId = null, GraphSortConfig? sortConfig = null, CancellationToken cancellationToken = default)
        => _innerService.GetNextIssuesAsync(parentId, sortConfig, cancellationToken);

    public Task<TaskGraph> BuildTaskGraphLayoutAsync(InactiveVisibility inactiveVisibility = InactiveVisibility.Hide, string? assignedTo = null, GraphSortConfig? sortConfig = null, CancellationToken cancellationToken = default)
        => _innerService.BuildTaskGraphLayoutAsync(inactiveVisibility, assignedTo, sortConfig, cancellationToken);

    public Task<TaskGraph> BuildFilteredTaskGraphLayoutAsync(IReadOnlySet<string> issueIds, GraphSortConfig? sortConfig = null, CancellationToken cancellationToken = default)
        => _innerService.BuildFilteredTaskGraphLayoutAsync(issueIds, sortConfig, cancellationToken);

    public Task<IReadOnlyList<Issue>> GetIssueHierarchyAsync(string issueId, bool ancestors = true, bool descendants = true, CancellationToken cancellationToken = default)
        => _innerService.GetIssueHierarchyAsync(issueId, ancestors, descendants, cancellationToken);
}

/// <summary>
/// In-memory storage service for testing that returns pre-loaded issues.
/// Implements IStorageService to work with Fleece.Core's IssueService.
/// </summary>
internal class InMemoryStorageService : IStorageService
{
    private readonly List<Issue> _issues;

    public InMemoryStorageService(IReadOnlyList<Issue> issues)
    {
        _issues = issues.ToList();
    }

    public Task<IReadOnlyList<Issue>> LoadIssuesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Issue>>(_issues);

    public Task SaveIssuesAsync(IReadOnlyList<Issue> issues, CancellationToken cancellationToken = default)
    {
        _issues.Clear();
        _issues.AddRange(issues);
        return Task.CompletedTask;
    }

    public Task AppendIssueAsync(Issue issue, CancellationToken cancellationToken = default)
    {
        _issues.Add(issue);
        return Task.CompletedTask;
    }

    public Task EnsureDirectoryExistsAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetAllIssueFilesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(["mock-issues.jsonl"]);

    public Task<IReadOnlyList<Issue>> LoadIssuesFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Issue>>(_issues);

    public Task DeleteIssueFileAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string> SaveIssuesWithHashAsync(IReadOnlyList<Issue> issues, CancellationToken cancellationToken = default)
    {
        _issues.Clear();
        _issues.AddRange(issues);
        return Task.FromResult("mock-hash");
    }

    public Task<(bool HasMultiple, string Message)> HasMultipleUnmergedFilesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((false, string.Empty));

    public Task<LoadIssuesResult> LoadIssuesWithDiagnosticsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new LoadIssuesResult { Issues = _issues, Diagnostics = [] });

    public Task<IReadOnlyList<Tombstone>> LoadTombstonesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Tombstone>>([]);

    public Task SaveTombstonesAsync(IReadOnlyList<Tombstone> tombstones, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AppendTombstonesAsync(IReadOnlyList<Tombstone> tombstones, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetAllTombstoneFilesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}

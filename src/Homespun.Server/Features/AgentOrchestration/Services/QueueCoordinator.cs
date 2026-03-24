using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Tracks the execution state for a single project.
/// </summary>
internal class ProjectExecution
{
    public required string ProjectId { get; init; }
    public required string RootIssueId { get; init; }
    public required string ProjectPath { get; init; }
    public required string DefaultBranch { get; init; }
    public QueueCoordinatorStatus Status { get; set; } = QueueCoordinatorStatus.Running;
    public Dictionary<string, string> WorkflowMappings { get; init; } = new();
    public List<ITaskQueue> Queues { get; } = new();

    /// <summary>
    /// Tracks parallel groups: when all queues in a group complete,
    /// the deferred continuation (if any) is executed.
    /// </summary>
    public List<ParallelGroup> ParallelGroups { get; } = new();

    /// <summary>
    /// Series continuations: queued issues waiting for a parallel group to complete.
    /// </summary>
    public List<SeriesContinuation> SeriesContinuations { get; } = new();
}

/// <summary>
/// A group of queues running in parallel that must all complete before continuing.
/// </summary>
internal class ParallelGroup
{
    public required string ParentIssueId { get; init; }
    public List<string> QueueIds { get; } = new();
    public bool IsComplete => QueueIds.All(id => CompletedQueueIds.Contains(id));
    public HashSet<string> CompletedQueueIds { get; } = new();
    public string? ContinuationGroupId { get; init; }
}

/// <summary>
/// Deferred work to execute after a parallel group completes within a series parent.
/// </summary>
internal class SeriesContinuation
{
    public required string GroupId { get; init; }
    public required List<Issue> RemainingChildren { get; init; }

    /// <summary>
    /// When set, newly created queues from this continuation should be
    /// added to the parent parallel group for proper completion tracking.
    /// </summary>
    public ParallelGroup? ParentParallelGroup { get; init; }
}

/// <summary>
/// Coordinates multiple TaskQueues for a project, spawning queues based on
/// the issue hierarchy's execution modes (Series vs Parallel).
/// </summary>
public class QueueCoordinator : IQueueCoordinator
{
    private readonly IFleeceService _fleeceService;
    private readonly IAgentStartBackgroundService _agentStartService;
    private readonly IHubContext<NotificationHub> _notificationHub;
    private readonly ILogger<QueueCoordinator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _maxConcurrency;
    private readonly object _lock = new();
    private readonly Dictionary<string, ProjectExecution> _executions = new();

    /// <summary>
    /// DI-friendly constructor. Max concurrency defaults to 5.
    /// </summary>
    public QueueCoordinator(
        IFleeceService fleeceService,
        IAgentStartBackgroundService agentStartService,
        IHubContext<NotificationHub> notificationHub,
        ILogger<QueueCoordinator> logger,
        ILoggerFactory loggerFactory)
        : this(fleeceService, agentStartService, notificationHub, logger, loggerFactory, maxConcurrency: 5)
    {
    }

    public QueueCoordinator(
        IFleeceService fleeceService,
        IAgentStartBackgroundService agentStartService,
        IHubContext<NotificationHub> notificationHub,
        ILogger<QueueCoordinator> logger,
        ILoggerFactory loggerFactory,
        int maxConcurrency)
    {
        _fleeceService = fleeceService;
        _agentStartService = agentStartService;
        _notificationHub = notificationHub;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _maxConcurrency = maxConcurrency;
    }

    public event Action<QueueCoordinatorEvent>? OnEvent;

    public Task StartExecution(string projectId, string issueId, string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        return StartExecution(projectId, issueId, projectPath, defaultBranch, new Dictionary<string, string>(), ct);
    }

    public async Task StartExecution(string projectId, string issueId, string projectPath, string defaultBranch, Dictionary<string, string> workflowMappings, CancellationToken ct = default)
    {
        var issue = await _fleeceService.GetIssueAsync(projectPath, issueId, ct);
        if (issue == null)
            throw new KeyNotFoundException($"Issue {issueId} not found.");

        var execution = new ProjectExecution
        {
            ProjectId = projectId,
            RootIssueId = issueId,
            ProjectPath = projectPath,
            DefaultBranch = defaultBranch,
            WorkflowMappings = workflowMappings
        };

        lock (_lock)
        {
            _executions[projectId] = execution;
        }

        EmitEvent(projectId, QueueCoordinatorEventType.ExecutionStarted, issueId: issueId);

        await ExpandIssueIntoQueues(execution, issue, ct);

        _ = BroadcastStatusAsync(projectId);
    }

    public IReadOnlyList<ITaskQueue> GetActiveQueues(string projectId)
    {
        lock (_lock)
        {
            return _executions.TryGetValue(projectId, out var execution)
                ? execution.Queues.ToList().AsReadOnly()
                : Array.Empty<ITaskQueue>();
        }
    }

    public void CancelAll(string projectId)
    {
        ProjectExecution? execution;
        lock (_lock)
        {
            if (!_executions.TryGetValue(projectId, out execution))
                return;
            execution.Status = QueueCoordinatorStatus.Cancelled;
        }

        foreach (var queue in execution.Queues)
            queue.Cancel();

        EmitEvent(projectId, QueueCoordinatorEventType.ExecutionCancelled);
        _ = BroadcastStatusAsync(projectId);
    }

    public QueueCoordinatorState? GetStatus(string projectId)
    {
        lock (_lock)
        {
            if (!_executions.TryGetValue(projectId, out var execution))
                return null;

            return new QueueCoordinatorState
            {
                ProjectId = projectId,
                Status = execution.Status,
                ActiveQueues = execution.Queues.ToList().AsReadOnly(),
                MaxConcurrency = _maxConcurrency,
                RunningQueueCount = execution.Queues.Count(q =>
                    q.State == TaskQueueState.Running || q.State == TaskQueueState.Blocked),
                RootIssueId = execution.RootIssueId
            };
        }
    }

    private async Task ExpandIssueIntoQueues(ProjectExecution execution, Issue issue, CancellationToken ct)
    {
        var children = await _fleeceService.GetChildrenAsync(execution.ProjectPath, issue.Id, ct);

        if (children.Count == 0)
        {
            // Leaf issue - create a single queue with just this issue
            var queue = CreateQueue(execution);
            await queue.EnqueueAsync(CreateRequest(execution, issue), ct);
            return;
        }

        if (issue.ExecutionMode == ExecutionMode.Parallel)
        {
            await ExpandParallel(execution, issue, children, null, ct);
        }
        else
        {
            await ExpandSeries(execution, children, ct);
        }
    }

    private async Task ExpandParallel(
        ProjectExecution execution,
        Issue parentIssue,
        IReadOnlyList<Issue> children,
        string? continuationGroupId,
        CancellationToken ct)
    {
        var group = new ParallelGroup
        {
            ParentIssueId = parentIssue.Id,
            ContinuationGroupId = continuationGroupId
        };

        lock (_lock)
        {
            execution.ParallelGroups.Add(group);
        }

        foreach (var child in children)
        {
            var childChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, child.Id, ct);

            if (childChildren.Count == 0)
            {
                // Leaf child - create a queue with just this child
                var queue = CreateQueue(execution);
                group.QueueIds.Add(queue.Id);
                await queue.EnqueueAsync(CreateRequest(execution, child), ct);
            }
            else if (child.ExecutionMode == ExecutionMode.Series)
            {
                // Series child within parallel - create one queue with all grandchildren
                await ExpandSeriesIntoSingleQueue(execution, child, childChildren, group, ct);
            }
            else
            {
                // Nested parallel - recursively expand
                await ExpandParallel(execution, child, childChildren, null, ct);
                // Add inner group's queues to this group
                var innerGroup = execution.ParallelGroups.Last();
                group.QueueIds.AddRange(innerGroup.QueueIds);
            }
        }
    }

    private async Task ExpandSeriesIntoSingleQueue(
        ProjectExecution execution,
        Issue seriesParent,
        IReadOnlyList<Issue> children,
        ParallelGroup? parentGroup,
        CancellationToken ct)
    {
        // For a series parent, we need to process children one at a time.
        // If the first child has children, we need to expand recursively.
        // Otherwise, create a queue and enqueue all leaf children.

        var firstChild = children[0];
        var firstChildChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, firstChild.Id, ct);

        if (firstChildChildren.Count > 0)
        {
            // First child has children - expand it, and set remaining as continuation
            if (children.Count > 1)
            {
                var groupId = Guid.NewGuid().ToString("N")[..12];
                lock (_lock)
                {
                    execution.SeriesContinuations.Add(new SeriesContinuation
                    {
                        GroupId = groupId,
                        RemainingChildren = children.Skip(1).ToList()
                    });
                }

                if (firstChild.ExecutionMode == ExecutionMode.Parallel)
                {
                    await ExpandParallel(execution, firstChild, firstChildChildren, groupId, ct);
                    if (parentGroup != null)
                    {
                        var innerGroup = execution.ParallelGroups.Last();
                        parentGroup.QueueIds.AddRange(innerGroup.QueueIds);
                    }
                }
                else
                {
                    await ExpandSeriesRecursive(execution, firstChild, firstChildChildren, parentGroup, groupId, ct);
                }
            }
            else
            {
                await ExpandIssueIntoQueuesWithParentGroup(execution, firstChild, firstChildChildren, parentGroup, ct);
            }
        }
        else
        {
            // All children are potentially leaves - create one queue
            var queue = CreateQueue(execution);
            parentGroup?.QueueIds.Add(queue.Id);

            // Enqueue first child
            await queue.EnqueueAsync(CreateRequest(execution, firstChild), ct);

            // Check remaining children - enqueue leaves, handle parents with children later
            for (var i = 1; i < children.Count; i++)
            {
                var child = children[i];
                var childChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, child.Id, ct);
                if (childChildren.Count == 0)
                {
                    await queue.EnqueueAsync(CreateRequest(execution, child), ct);
                }
                else
                {
                    // Non-leaf child - stop enqueueing, set up continuation
                    var contGroupId = Guid.NewGuid().ToString("N")[..12];
                    lock (_lock)
                    {
                        execution.SeriesContinuations.Add(new SeriesContinuation
                        {
                            GroupId = contGroupId,
                            RemainingChildren = children.Skip(i).ToList(),
                            ParentParallelGroup = parentGroup
                        });
                        var bridgeGroup = new ParallelGroup
                        {
                            ParentIssueId = firstChild.Id,
                            ContinuationGroupId = contGroupId
                        };
                        bridgeGroup.QueueIds.Add(queue.Id);
                        execution.ParallelGroups.Add(bridgeGroup);
                    }
                    // Remove queue from parent group - it will be re-tracked
                    // via continuation when expansion completes
                    parentGroup?.QueueIds.Remove(queue.Id);
                    return;
                }
            }
        }
    }

    private async Task ExpandSeriesRecursive(
        ProjectExecution execution,
        Issue seriesParent,
        IReadOnlyList<Issue> children,
        ParallelGroup? parentGroup,
        string? continuationGroupId,
        CancellationToken ct)
    {
        var firstChild = children[0];
        var firstChildChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, firstChild.Id, ct);

        string? innerContinuationId = null;
        if (children.Count > 1 || continuationGroupId != null)
        {
            innerContinuationId = continuationGroupId;
            if (children.Count > 1)
            {
                innerContinuationId = Guid.NewGuid().ToString("N")[..12];
                lock (_lock)
                {
                    execution.SeriesContinuations.Add(new SeriesContinuation
                    {
                        GroupId = innerContinuationId,
                        RemainingChildren = children.Skip(1).ToList()
                    });
                }
            }
        }

        if (firstChildChildren.Count == 0)
        {
            var queue = CreateQueue(execution);
            parentGroup?.QueueIds.Add(queue.Id);
            await queue.EnqueueAsync(CreateRequest(execution, firstChild), ct);

            // Enqueue remaining leaf children
            for (var i = 1; i < children.Count; i++)
            {
                await queue.EnqueueAsync(CreateRequest(execution, children[i]), ct);
            }
        }
        else
        {
            await ExpandIssueIntoQueuesWithParentGroup(execution, firstChild, firstChildChildren, parentGroup, ct);
        }
    }

    private async Task ExpandIssueIntoQueuesWithParentGroup(
        ProjectExecution execution,
        Issue issue,
        IReadOnlyList<Issue> children,
        ParallelGroup? parentGroup,
        CancellationToken ct)
    {
        if (issue.ExecutionMode == ExecutionMode.Parallel)
        {
            await ExpandParallel(execution, issue, children, null, ct);
            if (parentGroup != null)
            {
                var innerGroup = execution.ParallelGroups.Last();
                parentGroup.QueueIds.AddRange(innerGroup.QueueIds);
            }
        }
        else
        {
            await ExpandSeriesIntoSingleQueue(execution, issue, children, parentGroup, ct);
        }
    }

    private async Task ExpandSeries(ProjectExecution execution, IReadOnlyList<Issue> children, CancellationToken ct)
    {
        if (children.Count == 0) return;

        var firstChild = children[0];
        var firstChildChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, firstChild.Id, ct);

        if (firstChildChildren.Count > 0)
        {
            // First child has children - expand it and defer remaining
            if (children.Count > 1)
            {
                var groupId = Guid.NewGuid().ToString("N")[..12];
                lock (_lock)
                {
                    execution.SeriesContinuations.Add(new SeriesContinuation
                    {
                        GroupId = groupId,
                        RemainingChildren = children.Skip(1).ToList()
                    });
                }

                if (firstChild.ExecutionMode == ExecutionMode.Parallel)
                {
                    await ExpandParallel(execution, firstChild, firstChildChildren, groupId, ct);
                }
                else
                {
                    // Nested series - recurse, then wire up continuation
                    var queueCountBefore = execution.Queues.Count;
                    await ExpandSeries(execution, firstChildChildren, ct);
                    // Wrap newly created queues in a parallel group linked to continuation
                    lock (_lock)
                    {
                        var innerGroup = new ParallelGroup
                        {
                            ParentIssueId = firstChild.Id,
                            ContinuationGroupId = groupId
                        };
                        for (var qi = queueCountBefore; qi < execution.Queues.Count; qi++)
                            innerGroup.QueueIds.Add(execution.Queues[qi].Id);
                        execution.ParallelGroups.Add(innerGroup);
                    }
                }
            }
            else
            {
                await ExpandIssueIntoQueues(execution, firstChild, ct);
            }
        }
        else
        {
            // First child is a leaf - create queue with all consecutive leaf children
            var queue = CreateQueue(execution);
            await queue.EnqueueAsync(CreateRequest(execution, firstChild), ct);

            for (var i = 1; i < children.Count; i++)
            {
                var child = children[i];
                var childChildren = await _fleeceService.GetChildrenAsync(execution.ProjectPath, child.Id, ct);
                if (childChildren.Count == 0)
                {
                    await queue.EnqueueAsync(CreateRequest(execution, child), ct);
                }
                else
                {
                    // Non-leaf child - stop enqueueing, set up continuation
                    var contGroupId = Guid.NewGuid().ToString("N")[..12];
                    lock (_lock)
                    {
                        execution.SeriesContinuations.Add(new SeriesContinuation
                        {
                            GroupId = contGroupId,
                            RemainingChildren = children.Skip(i).ToList()
                        });
                        var bridgeGroup = new ParallelGroup
                        {
                            ParentIssueId = firstChild.Id,
                            ContinuationGroupId = contGroupId
                        };
                        bridgeGroup.QueueIds.Add(queue.Id);
                        execution.ParallelGroups.Add(bridgeGroup);
                    }
                    return;
                }
            }
        }
    }

    private TaskQueue CreateQueue(ProjectExecution execution)
    {
        var queueLogger = _loggerFactory.CreateLogger<TaskQueue>();
        var queue = new TaskQueue(_agentStartService, queueLogger);

        bool shouldPause;
        lock (_lock)
        {
            execution.Queues.Add(queue);
            var runningCount = execution.Queues.Count(q =>
                q.State == TaskQueueState.Running || q.State == TaskQueueState.Blocked);
            shouldPause = runningCount >= _maxConcurrency;
        }

        if (shouldPause)
            queue.Pause();

        queue.OnEvent += e => HandleQueueEvent(execution, queue, e);

        EmitEvent(execution.ProjectId, QueueCoordinatorEventType.QueueCreated, queueId: queue.Id);

        return queue;
    }

    private void HandleQueueEvent(ProjectExecution execution, TaskQueue queue, TaskQueueEvent e)
    {
        if (e.EventType == TaskQueueEventType.StateChanged &&
            e.NewState == TaskQueueState.Idle &&
            e.PreviousState == TaskQueueState.Running)
        {
            // Queue finished processing its last item
            OnQueueIdle(execution, queue);
        }
    }

    private void OnQueueIdle(ProjectExecution execution, TaskQueue queue)
    {
        // Check if queue is truly done (no pending, no current)
        if (queue.CurrentRequest != null || queue.PendingRequests.Count > 0)
            return;

        EmitEvent(execution.ProjectId, QueueCoordinatorEventType.QueueCompleted, queueId: queue.Id);

        // Check parallel groups - process ALL groups this queue belongs to
        List<SeriesContinuation>? continuationsToFire = null;
        lock (_lock)
        {
            foreach (var group in execution.ParallelGroups)
            {
                if (group.QueueIds.Contains(queue.Id))
                {
                    group.CompletedQueueIds.Add(queue.Id);
                    if (group.IsComplete && group.ContinuationGroupId != null)
                    {
                        var continuation = execution.SeriesContinuations
                            .FirstOrDefault(c => c.GroupId == group.ContinuationGroupId);
                        if (continuation != null)
                        {
                            execution.SeriesContinuations.Remove(continuation);
                            continuationsToFire ??= new List<SeriesContinuation>();
                            continuationsToFire.Add(continuation);
                        }
                    }
                }
            }
        }

        if (continuationsToFire != null)
        {
            foreach (var continuation in continuationsToFire)
            {
                _ = FireContinuationAsync(execution, continuation);
            }
        }

        // Try to resume paused queues if we're under max concurrency
        ResumeWaitingQueues(execution);

        // Check if all queues are idle/completed
        CheckAllComplete(execution);
    }

    private async Task FireContinuationAsync(ProjectExecution execution, SeriesContinuation continuation)
    {
        int queueCountBefore;
        lock (_lock)
        {
            queueCountBefore = execution.Queues.Count;
        }

        await ExpandSeries(execution, continuation.RemainingChildren, CancellationToken.None);

        // If this continuation was spawned from within a parallel group,
        // add newly created queues to the parent group for proper tracking
        if (continuation.ParentParallelGroup != null)
        {
            lock (_lock)
            {
                for (var i = queueCountBefore; i < execution.Queues.Count; i++)
                {
                    continuation.ParentParallelGroup.QueueIds.Add(execution.Queues[i].Id);
                }
            }
        }
    }

    private void ResumeWaitingQueues(ProjectExecution execution)
    {
        lock (_lock)
        {
            var runningCount = execution.Queues.Count(q =>
                q.State == TaskQueueState.Running || q.State == TaskQueueState.Blocked);

            var pausedQueues = execution.Queues
                .Where(q => q.State == TaskQueueState.Idle && q.PendingRequests.Count > 0)
                .ToList();

            foreach (var queue in pausedQueues)
            {
                if (runningCount >= _maxConcurrency)
                    break;

                _ = queue.ResumeAsync();
                runningCount++;
            }
        }
    }

    private void CheckAllComplete(ProjectExecution execution)
    {
        lock (_lock)
        {
            var allDone = execution.Queues.All(q =>
                q.State == TaskQueueState.Idle || q.State == TaskQueueState.Completed);

            var allEmpty = execution.Queues.All(q =>
                q.CurrentRequest == null && q.PendingRequests.Count == 0);

            if (allDone && allEmpty && execution.SeriesContinuations.Count == 0)
            {
                execution.Status = QueueCoordinatorStatus.Completed;
                EmitEvent(execution.ProjectId, QueueCoordinatorEventType.AllQueuesCompleted);
                _ = BroadcastStatusAsync(execution.ProjectId);
            }
        }
    }

    private AgentStartRequest CreateRequest(ProjectExecution execution, Issue issue)
    {
        return new AgentStartRequest
        {
            IssueId = issue.Id,
            ProjectId = execution.ProjectId,
            ProjectLocalPath = execution.ProjectPath,
            ProjectDefaultBranch = execution.DefaultBranch,
            Issue = issue,
            BranchName = $"task/{issue.Id}"
        };
    }

    private void EmitEvent(
        string projectId,
        QueueCoordinatorEventType eventType,
        string? queueId = null,
        string? issueId = null,
        string? error = null)
    {
        OnEvent?.Invoke(new QueueCoordinatorEvent
        {
            ProjectId = projectId,
            EventType = eventType,
            QueueId = queueId,
            IssueId = issueId,
            Error = error
        });
    }

    private async Task BroadcastStatusAsync(string projectId)
    {
        try
        {
            var status = GetStatus(projectId);
            if (status != null)
            {
                await _notificationHub.Clients.All.SendAsync("QueueCoordinatorStatusChanged", status);
                await _notificationHub.Clients.Group($"project-{projectId}")
                    .SendAsync("QueueCoordinatorStatusChanged", status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast queue coordinator status for project {ProjectId}", projectId);
        }
    }
}

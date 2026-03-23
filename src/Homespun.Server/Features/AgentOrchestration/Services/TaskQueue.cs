namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// A sequential execution pipeline that processes issues one at a time
/// by delegating to AgentStartBackgroundService.
/// </summary>
public class TaskQueue : ITaskQueue
{
    private readonly IAgentStartBackgroundService _agentStartService;
    private readonly ILogger<TaskQueue> _logger;
    private readonly List<AgentStartRequest> _pendingRequests = new();
    private readonly List<TaskQueueHistoryEntry> _history = new();
    private readonly object _lock = new();

    private bool _paused;
    private DateTimeOffset? _currentStartedAt;

    public TaskQueue(IAgentStartBackgroundService agentStartService, ILogger<TaskQueue> logger)
    {
        _agentStartService = agentStartService;
        _logger = logger;
        Id = Guid.NewGuid().ToString("N")[..12];
    }

    public string Id { get; }
    public TaskQueueState State { get; private set; } = TaskQueueState.Idle;
    public AgentStartRequest? CurrentRequest { get; private set; }
    public IReadOnlyList<AgentStartRequest> PendingRequests
    {
        get { lock (_lock) return _pendingRequests.ToList().AsReadOnly(); }
    }
    public IReadOnlyList<TaskQueueHistoryEntry> History
    {
        get { lock (_lock) return _history.ToList().AsReadOnly(); }
    }

    public event Action<TaskQueueEvent>? OnEvent;

    public async Task EnqueueAsync(AgentStartRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (State == TaskQueueState.Completed)
                throw new InvalidOperationException("Cannot enqueue to a completed queue.");

            if (CurrentRequest == null && !_paused)
            {
                // Queue is idle, start processing immediately
                CurrentRequest = request;
                _currentStartedAt = DateTimeOffset.UtcNow;
                TransitionState(TaskQueueState.Running);
            }
            else
            {
                // Queue is busy or paused, add to pending
                _pendingRequests.Add(request);
                return;
            }
        }

        // Start processing outside the lock
        EmitEvent(TaskQueueEventType.IssueStarted, request.IssueId);
        await _agentStartService.QueueAgentStartAsync(request);
    }

    public bool Dequeue(string issueId)
    {
        lock (_lock)
        {
            // Cannot remove the currently executing request
            if (CurrentRequest?.IssueId == issueId)
                return false;

            var index = _pendingRequests.FindIndex(r => r.IssueId == issueId);
            if (index < 0)
                return false;

            _pendingRequests.RemoveAt(index);
            return true;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            _paused = true;
            _logger.LogInformation("Queue {QueueId} paused", Id);
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        AgentStartRequest? nextRequest = null;

        lock (_lock)
        {
            _paused = false;

            if (State == TaskQueueState.Idle && _pendingRequests.Count > 0)
            {
                nextRequest = _pendingRequests[0];
                _pendingRequests.RemoveAt(0);
                CurrentRequest = nextRequest;
                _currentStartedAt = DateTimeOffset.UtcNow;
                TransitionState(TaskQueueState.Running);
            }
        }

        if (nextRequest != null)
        {
            EmitEvent(TaskQueueEventType.IssueStarted, nextRequest.IssueId);
            await _agentStartService.QueueAgentStartAsync(nextRequest);
        }
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _pendingRequests.Clear();
            TransitionState(TaskQueueState.Completed);
            _logger.LogInformation("Queue {QueueId} cancelled", Id);
        }
    }

    public void NotifyCompleted(string issueId, bool success, string? error = null)
    {
        AgentStartRequest? nextRequest = null;

        lock (_lock)
        {
            if (CurrentRequest?.IssueId != issueId)
                return;

            var completedRequest = CurrentRequest;
            var startedAt = _currentStartedAt ?? DateTimeOffset.UtcNow;

            _history.Add(new TaskQueueHistoryEntry
            {
                IssueId = issueId,
                Request = completedRequest,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Success = success,
                Error = error
            });

            CurrentRequest = null;
            _currentStartedAt = null;

            // Emit completion/failure event
            if (success)
            {
                EmitEvent(TaskQueueEventType.IssueCompleted, issueId);
            }
            else
            {
                EmitEvent(TaskQueueEventType.IssueFailed, issueId, error: error);
            }

            // Determine next state
            if (_paused || _pendingRequests.Count == 0)
            {
                TransitionState(TaskQueueState.Idle);
                return;
            }

            // Start next issue
            nextRequest = _pendingRequests[0];
            _pendingRequests.RemoveAt(0);
            CurrentRequest = nextRequest;
            _currentStartedAt = DateTimeOffset.UtcNow;
            // State stays Running - emit state change for Running -> Running
        }

        if (nextRequest != null)
        {
            EmitEvent(TaskQueueEventType.IssueStarted, nextRequest.IssueId);
            _ = _agentStartService.QueueAgentStartAsync(nextRequest);
        }
    }

    public void NotifyBlocked(string issueId, string reason)
    {
        lock (_lock)
        {
            if (CurrentRequest?.IssueId != issueId)
                return;

            if (State != TaskQueueState.Running)
                return;

            TransitionState(TaskQueueState.Blocked);
            _logger.LogInformation(
                "Queue {QueueId} blocked on issue {IssueId}: {Reason}",
                Id, issueId, reason);
        }
    }

    public async Task UnblockAsync(CancellationToken cancellationToken = default)
    {
        AgentStartRequest? currentRequest;

        lock (_lock)
        {
            if (State != TaskQueueState.Blocked || CurrentRequest == null)
                return;

            currentRequest = CurrentRequest;
            TransitionState(TaskQueueState.Running);
        }

        EmitEvent(TaskQueueEventType.IssueStarted, currentRequest.IssueId);
        await _agentStartService.QueueAgentStartAsync(currentRequest);
    }

    private void TransitionState(TaskQueueState newState)
    {
        var previousState = State;
        State = newState;

        EmitEvent(TaskQueueEventType.StateChanged,
            previousState: previousState, newState: newState);
    }

    private void EmitEvent(
        TaskQueueEventType eventType,
        string? issueId = null,
        string? error = null,
        TaskQueueState? previousState = null,
        TaskQueueState? newState = null)
    {
        OnEvent?.Invoke(new TaskQueueEvent
        {
            QueueId = Id,
            EventType = eventType,
            IssueId = issueId,
            Error = error,
            PreviousState = previousState,
            NewState = newState
        });
    }
}

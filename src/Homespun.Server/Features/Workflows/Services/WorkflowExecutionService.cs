using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Service for executing workflows with linear (non-parallel) execution.
/// Handles the full lifecycle of workflow execution including starting, pausing,
/// resuming, and cancelling executions.
/// </summary>
public sealed class WorkflowExecutionService : IWorkflowExecutionService, IDisposable
{
    private readonly IWorkflowStorageService _storageService;
    private readonly ILogger<WorkflowExecutionService> _logger;

    // Cache of executions per project, keyed by project path
    private readonly ConcurrentDictionary<string, ProjectExecutionCache> _projectCaches = new();

    // Semaphores for concurrent access per project
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _projectSemaphores = new();

    // Cancellation tokens for running executions
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _executionCts = new();

    private const string ExecutionsDirectory = ".workflows";
    private const string ExecutionsFilePrefix = "executions_";
    private const string ExecutionsFileExtension = ".jsonl";
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WorkflowExecutionService(
        IWorkflowStorageService storageService,
        ILogger<WorkflowExecutionService> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StartWorkflowResult> StartWorkflowAsync(
        string projectPath,
        string workflowId,
        TriggerContext triggerContext,
        CancellationToken ct = default)
    {
        var workflow = await _storageService.GetWorkflowAsync(projectPath, workflowId, ct);

        if (workflow == null)
        {
            return new StartWorkflowResult
            {
                Success = false,
                Error = $"Workflow '{workflowId}' not found"
            };
        }

        if (!workflow.Enabled)
        {
            return new StartWorkflowResult
            {
                Success = false,
                Error = $"Workflow '{workflowId}' is disabled"
            };
        }

        var execution = CreateExecution(workflow, triggerContext);

        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);
            cache.Executions[execution.Id] = execution;
            await PersistExecutionsAsync(projectPath, cache, ct);

            _logger.LogInformation(
                "Started workflow execution {ExecutionId} for workflow {WorkflowId}",
                execution.Id,
                workflowId);

            // Start executing the workflow
            var cts = new CancellationTokenSource();
            _executionCts[execution.Id] = cts;

            // Fire and forget the execution loop
            _ = ExecuteWorkflowAsync(projectPath, execution, workflow, cts.Token);
        }
        finally
        {
            semaphore.Release();
        }

        return new StartWorkflowResult
        {
            Success = true,
            Execution = execution
        };
    }

    /// <inheritdoc />
    public async Task<WorkflowExecution?> GetExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        return cache.Executions.GetValueOrDefault(executionId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowExecution>> ListExecutionsAsync(
        string projectPath,
        string? workflowId = null,
        CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var executions = cache.Executions.Values.AsEnumerable();

        if (workflowId != null)
        {
            executions = executions.Where(e => e.WorkflowId == workflowId);
        }

        return executions.OrderByDescending(e => e.CreatedAt).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> PauseExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default)
    {
        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);

            if (!cache.Executions.TryGetValue(executionId, out var execution))
            {
                return false;
            }

            if (execution.Status != WorkflowExecutionStatus.Running)
            {
                return false;
            }

            execution.Status = WorkflowExecutionStatus.Paused;
            await PersistExecutionsAsync(projectPath, cache, ct);

            _logger.LogInformation("Paused workflow execution {ExecutionId}", executionId);
            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ResumeExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default)
    {
        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);

            if (!cache.Executions.TryGetValue(executionId, out var execution))
            {
                return false;
            }

            if (execution.Status != WorkflowExecutionStatus.Paused)
            {
                return false;
            }

            execution.Status = WorkflowExecutionStatus.Running;
            await PersistExecutionsAsync(projectPath, cache, ct);

            _logger.LogInformation("Resumed workflow execution {ExecutionId}", executionId);

            // Continue execution from where we left off
            var workflow = await _storageService.GetWorkflowAsync(projectPath, execution.WorkflowId, ct);
            if (workflow != null)
            {
                var cts = new CancellationTokenSource();
                _executionCts[executionId] = cts;
                _ = ExecuteWorkflowAsync(projectPath, execution, workflow, cts.Token);
            }

            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default)
    {
        // Cancel the execution token first
        if (_executionCts.TryRemove(executionId, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);

            if (!cache.Executions.TryGetValue(executionId, out var execution))
            {
                return false;
            }

            if (execution.Status is WorkflowExecutionStatus.Completed
                or WorkflowExecutionStatus.Failed
                or WorkflowExecutionStatus.Cancelled)
            {
                return false;
            }

            execution.Status = WorkflowExecutionStatus.Cancelled;
            execution.CompletedAt = DateTime.UtcNow;

            // Cancel any running nodes
            foreach (var nodeExecution in execution.NodeExecutions
                .Where(n => n.Status == NodeExecutionStatus.Running))
            {
                nodeExecution.Status = NodeExecutionStatus.Cancelled;
                nodeExecution.CompletedAt = DateTime.UtcNow;
            }

            await PersistExecutionsAsync(projectPath, cache, ct);

            _logger.LogInformation("Cancelled workflow execution {ExecutionId}", executionId);
            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task OnNodeCompletedAsync(
        string projectPath,
        string executionId,
        string nodeId,
        Dictionary<string, object>? output,
        CancellationToken ct = default)
    {
        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);

            if (!cache.Executions.TryGetValue(executionId, out var execution))
            {
                _logger.LogWarning(
                    "Cannot complete node {NodeId}: Execution {ExecutionId} not found",
                    nodeId,
                    executionId);
                return;
            }

            var nodeExecution = execution.NodeExecutions.FirstOrDefault(n => n.NodeId == nodeId);
            if (nodeExecution == null)
            {
                _logger.LogWarning(
                    "Cannot complete node {NodeId}: Node not found in execution {ExecutionId}",
                    nodeId,
                    executionId);
                return;
            }

            nodeExecution.Status = NodeExecutionStatus.Completed;
            nodeExecution.CompletedAt = DateTime.UtcNow;
            nodeExecution.Output = output;

            if (nodeExecution.StartedAt.HasValue)
            {
                nodeExecution.DurationMs = (long)(DateTime.UtcNow - nodeExecution.StartedAt.Value).TotalMilliseconds;
            }

            // Store output in context
            execution.Context.NodeOutputs[nodeId] = new NodeOutput
            {
                Status = "completed",
                Data = output,
                CompletedAt = DateTime.UtcNow
            };

            await PersistExecutionsAsync(projectPath, cache, ct);

            _logger.LogDebug(
                "Node {NodeId} completed in execution {ExecutionId}",
                nodeId,
                executionId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task OnNodeFailedAsync(
        string projectPath,
        string executionId,
        string nodeId,
        string errorMessage,
        CancellationToken ct = default)
    {
        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);

            if (!cache.Executions.TryGetValue(executionId, out var execution))
            {
                _logger.LogWarning(
                    "Cannot fail node {NodeId}: Execution {ExecutionId} not found",
                    nodeId,
                    executionId);
                return;
            }

            var nodeExecution = execution.NodeExecutions.FirstOrDefault(n => n.NodeId == nodeId);
            if (nodeExecution == null)
            {
                _logger.LogWarning(
                    "Cannot fail node {NodeId}: Node not found in execution {ExecutionId}",
                    nodeId,
                    executionId);
                return;
            }

            nodeExecution.Status = NodeExecutionStatus.Failed;
            nodeExecution.CompletedAt = DateTime.UtcNow;
            nodeExecution.ErrorMessage = errorMessage;

            if (nodeExecution.StartedAt.HasValue)
            {
                nodeExecution.DurationMs = (long)(DateTime.UtcNow - nodeExecution.StartedAt.Value).TotalMilliseconds;
            }

            // Store error in context
            execution.Context.NodeOutputs[nodeId] = new NodeOutput
            {
                Status = "failed",
                Error = errorMessage,
                CompletedAt = DateTime.UtcNow
            };

            // Get the workflow to check settings
            var workflow = await _storageService.GetWorkflowAsync(projectPath, execution.WorkflowId, ct);

            // If not continuing on failure, fail the entire execution
            if (workflow?.Settings.ContinueOnFailure != true)
            {
                execution.Status = WorkflowExecutionStatus.Failed;
                execution.CompletedAt = DateTime.UtcNow;
                execution.ErrorMessage = $"Node '{nodeId}' failed: {errorMessage}";

                if (_executionCts.TryRemove(executionId, out var cts))
                {
                    await cts.CancelAsync();
                    cts.Dispose();
                }
            }

            await PersistExecutionsAsync(projectPath, cache, ct);

            _logger.LogWarning(
                "Node {NodeId} failed in execution {ExecutionId}: {Error}",
                nodeId,
                executionId,
                errorMessage);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var cts in _executionCts.Values)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS was already disposed by execution completing
            }

            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed
            }
        }

        _executionCts.Clear();

        foreach (var semaphore in _projectSemaphores.Values)
        {
            semaphore.Dispose();
        }

        _projectSemaphores.Clear();
    }

    #region Private Methods

    private WorkflowExecution CreateExecution(WorkflowDefinition workflow, TriggerContext triggerContext)
    {
        var executionId = GenerateExecutionId(workflow.Id);

        var execution = new WorkflowExecution
        {
            Id = executionId,
            WorkflowId = workflow.Id,
            WorkflowVersion = workflow.Version,
            ProjectId = workflow.ProjectId,
            Status = WorkflowExecutionStatus.Running,
            Trigger = new ExecutionTriggerInfo
            {
                Type = triggerContext.TriggerType,
                EventType = triggerContext.EventType,
                EventData = triggerContext.EventData,
                Timestamp = DateTime.UtcNow
            },
            Context = new WorkflowContext
            {
                Input = new Dictionary<string, object>(triggerContext.Input)
            },
            NodeExecutions = workflow.Nodes
                .Select(n => new NodeExecution
                {
                    NodeId = n.Id,
                    Status = NodeExecutionStatus.Pending
                })
                .ToList(),
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            TriggeredBy = triggerContext.TriggeredBy
        };

        return execution;
    }

    private async Task ExecuteWorkflowAsync(
        string projectPath,
        WorkflowExecution execution,
        WorkflowDefinition workflow,
        CancellationToken ct)
    {
        try
        {
            // Get nodes in topological order
            var sortedNodes = GetTopologicalOrder(workflow);

            foreach (var node in sortedNodes)
            {
                ct.ThrowIfCancellationRequested();

                // Check if execution is paused
                var currentExecution = await GetExecutionAsync(projectPath, execution.Id, ct);
                if (currentExecution?.Status == WorkflowExecutionStatus.Paused)
                {
                    _logger.LogDebug(
                        "Execution {ExecutionId} is paused, stopping execution loop",
                        execution.Id);
                    return;
                }

                // Skip disabled nodes
                if (node.Disabled)
                {
                    await MarkNodeSkipped(projectPath, execution.Id, node.Id, ct);
                    continue;
                }

                // Execute the node
                await ExecuteNodeAsync(projectPath, execution, workflow, node, ct);

                // Check if execution failed
                currentExecution = await GetExecutionAsync(projectPath, execution.Id, ct);
                if (currentExecution?.Status == WorkflowExecutionStatus.Failed ||
                    currentExecution?.Status == WorkflowExecutionStatus.Cancelled)
                {
                    return;
                }
            }

            // All nodes completed, mark execution as completed
            await CompleteExecutionAsync(projectPath, execution.Id, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Execution {ExecutionId} was cancelled", execution.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution {ExecutionId} failed with error", execution.Id);
            await FailExecutionAsync(projectPath, execution.Id, ex.Message, ct);
        }
    }

    private async Task ExecuteNodeAsync(
        string projectPath,
        WorkflowExecution execution,
        WorkflowDefinition workflow,
        WorkflowNode node,
        CancellationToken ct)
    {
        // Mark node as running
        await MarkNodeRunning(projectPath, execution.Id, node.Id, ct);

        try
        {
            switch (node.Type)
            {
                case WorkflowNodeType.Start:
                    // Start node just passes through
                    await OnNodeCompletedAsync(projectPath, execution.Id, node.Id, null, ct);
                    break;

                case WorkflowNodeType.End:
                    // End node marks completion
                    await OnNodeCompletedAsync(projectPath, execution.Id, node.Id, null, ct);
                    break;

                case WorkflowNodeType.Agent:
                    // Agent nodes will be completed externally via callback
                    // For now in MVP, we just mark them as waiting
                    // The node will stay in Running state until OnNodeCompleted is called
                    _logger.LogInformation(
                        "Agent node {NodeId} started, waiting for completion callback",
                        node.Id);
                    break;

                case WorkflowNodeType.Gate:
                    // Gate nodes wait for approval
                    await MarkNodeWaitingForInput(projectPath, execution.Id, node.Id, ct);
                    break;

                case WorkflowNodeType.Action:
                case WorkflowNodeType.Transform:
                    // These will be implemented in future phases
                    await OnNodeCompletedAsync(projectPath, execution.Id, node.Id, null, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            await OnNodeFailedAsync(projectPath, execution.Id, node.Id, ex.Message, ct);
        }
    }

    private async Task MarkNodeRunning(string projectPath, string executionId, string nodeId, CancellationToken ct)
    {
        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);
            if (cache.Executions.TryGetValue(executionId, out var execution))
            {
                var nodeExecution = execution.NodeExecutions.FirstOrDefault(n => n.NodeId == nodeId);
                if (nodeExecution != null)
                {
                    nodeExecution.Status = NodeExecutionStatus.Running;
                    nodeExecution.StartedAt = DateTime.UtcNow;
                    await PersistExecutionsAsync(projectPath, cache, ct);
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task MarkNodeSkipped(string projectPath, string executionId, string nodeId, CancellationToken ct)
    {
        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);
            if (cache.Executions.TryGetValue(executionId, out var execution))
            {
                var nodeExecution = execution.NodeExecutions.FirstOrDefault(n => n.NodeId == nodeId);
                if (nodeExecution != null)
                {
                    nodeExecution.Status = NodeExecutionStatus.Skipped;
                    await PersistExecutionsAsync(projectPath, cache, ct);
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task MarkNodeWaitingForInput(string projectPath, string executionId, string nodeId, CancellationToken ct)
    {
        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);
            if (cache.Executions.TryGetValue(executionId, out var execution))
            {
                var nodeExecution = execution.NodeExecutions.FirstOrDefault(n => n.NodeId == nodeId);
                if (nodeExecution != null)
                {
                    nodeExecution.Status = NodeExecutionStatus.WaitingForInput;
                    await PersistExecutionsAsync(projectPath, cache, ct);
                }

                // Also pause the execution when waiting for input
                execution.Status = WorkflowExecutionStatus.Paused;
                await PersistExecutionsAsync(projectPath, cache, ct);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task CompleteExecutionAsync(string projectPath, string executionId, CancellationToken ct)
    {
        if (_executionCts.TryRemove(executionId, out var cts))
        {
            cts.Dispose();
        }

        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);
            if (cache.Executions.TryGetValue(executionId, out var execution))
            {
                execution.Status = WorkflowExecutionStatus.Completed;
                execution.CompletedAt = DateTime.UtcNow;
                await PersistExecutionsAsync(projectPath, cache, ct);

                _logger.LogInformation(
                    "Workflow execution {ExecutionId} completed successfully",
                    executionId);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task FailExecutionAsync(string projectPath, string executionId, string error, CancellationToken ct)
    {
        if (_executionCts.TryRemove(executionId, out var cts))
        {
            cts.Dispose();
        }

        var semaphore = GetProjectSemaphore(projectPath);
        await semaphore.WaitAsync(ct);

        try
        {
            var cache = await EnsureCacheLoadedAsync(projectPath, ct);
            if (cache.Executions.TryGetValue(executionId, out var execution))
            {
                execution.Status = WorkflowExecutionStatus.Failed;
                execution.CompletedAt = DateTime.UtcNow;
                execution.ErrorMessage = error;
                await PersistExecutionsAsync(projectPath, cache, ct);

                _logger.LogError(
                    "Workflow execution {ExecutionId} failed: {Error}",
                    executionId,
                    error);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static List<WorkflowNode> GetTopologicalOrder(WorkflowDefinition workflow)
    {
        var result = new List<WorkflowNode>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        var nodeMap = workflow.Nodes.ToDictionary(n => n.Id);
        var edges = workflow.Edges.ToLookup(e => e.Target);

        void Visit(string nodeId)
        {
            if (visited.Contains(nodeId))
                return;

            if (visiting.Contains(nodeId))
                throw new InvalidOperationException($"Cycle detected at node {nodeId}");

            visiting.Add(nodeId);

            // Visit all nodes that this node depends on (predecessors)
            foreach (var edge in workflow.Edges.Where(e => e.Target == nodeId))
            {
                Visit(edge.Source);
            }

            visiting.Remove(nodeId);
            visited.Add(nodeId);

            if (nodeMap.TryGetValue(nodeId, out var node))
            {
                result.Add(node);
            }
        }

        foreach (var node in workflow.Nodes)
        {
            Visit(node.Id);
        }

        return result;
    }

    private SemaphoreSlim GetProjectSemaphore(string projectPath)
    {
        return _projectSemaphores.GetOrAdd(
            NormalizePath(projectPath),
            _ => new SemaphoreSlim(1, 1));
    }

    private async Task<ProjectExecutionCache> EnsureCacheLoadedAsync(string projectPath, CancellationToken ct)
    {
        var normalizedPath = NormalizePath(projectPath);

        if (_projectCaches.TryGetValue(normalizedPath, out var existingCache))
        {
            return existingCache;
        }

        var cache = new ProjectExecutionCache();
        await LoadExecutionsFromDiskAsync(projectPath, cache, ct);
        _projectCaches[normalizedPath] = cache;
        return cache;
    }

    private async Task LoadExecutionsFromDiskAsync(string projectPath, ProjectExecutionCache cache, CancellationToken ct)
    {
        var executionsDir = Path.Combine(projectPath, ExecutionsDirectory);
        if (!Directory.Exists(executionsDir))
        {
            return;
        }

        var files = Directory.GetFiles(executionsDir, $"{ExecutionsFilePrefix}*{ExecutionsFileExtension}");

        foreach (var file in files)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    try
                    {
                        var stored = JsonSerializer.Deserialize<StoredExecution>(line, JsonOptions);
                        if (stored?.Execution != null)
                        {
                            cache.Executions[stored.Execution.Id] = stored.Execution;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse execution from line in {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load executions from {File}", file);
            }
        }
    }

    private async Task PersistExecutionsAsync(string projectPath, ProjectExecutionCache cache, CancellationToken ct)
    {
        var executionsDir = Path.Combine(projectPath, ExecutionsDirectory);
        Directory.CreateDirectory(executionsDir);

        // Delete existing execution files
        var existingFiles = Directory.GetFiles(executionsDir, $"{ExecutionsFilePrefix}*{ExecutionsFileExtension}");
        foreach (var file in existingFiles)
        {
            File.Delete(file);
        }

        // Generate new content hash
        var contentHash = GenerateContentHash(cache.Executions.Keys);
        var newFile = Path.Combine(executionsDir, $"{ExecutionsFilePrefix}{contentHash}{ExecutionsFileExtension}");

        var lines = cache.Executions.Values.Select(e => JsonSerializer.Serialize(
            new StoredExecution { SchemaVersion = SchemaVersion, Execution = e },
            JsonOptions));

        await File.WriteAllLinesAsync(newFile, lines, ct);
    }

    private static string GenerateExecutionId(string workflowId)
    {
        var input = $"{workflowId}-{DateTime.UtcNow.Ticks}-{Guid.NewGuid()}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
    }

    private static string GenerateContentHash(IEnumerable<string> executionIds)
    {
        var input = string.Join(",", executionIds.OrderBy(id => id)) + DateTime.UtcNow.Ticks;
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes)[..6].ToLowerInvariant();
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    #endregion

    #region Inner Classes

    private sealed class ProjectExecutionCache
    {
        public Dictionary<string, WorkflowExecution> Executions { get; } = new();
    }

    private sealed class StoredExecution
    {
        public int SchemaVersion { get; set; }
        public WorkflowExecution? Execution { get; set; }
    }

    #endregion
}

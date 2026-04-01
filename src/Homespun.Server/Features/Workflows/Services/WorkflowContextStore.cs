using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Service for storing and retrieving workflow execution context.
/// Uses a write-through cache pattern with JSON file persistence for crash recovery.
/// </summary>
public sealed class WorkflowContextStore : IWorkflowContextStore
{
    private static readonly string WorkflowsDirectoryName = Path.Combine(".fleece", "workflows");
    private const string ContextFilePrefix = "context_";
    private const string ContextFileExtension = ".json";

    private readonly ConcurrentDictionary<string, StoredWorkflowContext> _contextCache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly ILogger<WorkflowContextStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public WorkflowContextStore(ILogger<WorkflowContextStore> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private static string GetCacheKey(string projectPath, string executionId) =>
        $"{NormalizePath(projectPath)}:{executionId}";

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string GetWorkflowsDirectory(string projectPath) =>
        Path.Combine(projectPath, WorkflowsDirectoryName);

    private static string GetContextFilePath(string projectPath, string executionId) =>
        Path.Combine(GetWorkflowsDirectory(projectPath), $"{ContextFilePrefix}{executionId}{ContextFileExtension}");

    private SemaphoreSlim GetWriteLock(string cacheKey) =>
        _writeLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

    public async Task<StoredWorkflowContext> InitializeContextAsync(
        string projectPath,
        string executionId,
        string workflowId,
        string workingDirectory,
        JsonElement triggerData,
        CancellationToken ct = default)
    {
        var cacheKey = GetCacheKey(projectPath, executionId);
        var writeLock = GetWriteLock(cacheKey);

        await writeLock.WaitAsync(ct);
        try
        {
            var context = new StoredWorkflowContext
            {
                ExecutionId = executionId,
                WorkflowId = workflowId,
                WorkingDirectory = workingDirectory,
                TriggerData = triggerData,
                NodeOutputs = [],
                Variables = [],
                Artifacts = [],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Update cache
            _contextCache[cacheKey] = context;

            // Persist to disk
            await PersistContextAsync(projectPath, executionId, context, ct);

            _logger.LogDebug(
                "Initialized context for execution {ExecutionId} in project {ProjectPath}",
                executionId,
                projectPath);

            return context;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<StoredWorkflowContext?> GetContextAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default)
    {
        var cacheKey = GetCacheKey(projectPath, executionId);

        // Try cache first
        if (_contextCache.TryGetValue(cacheKey, out var cachedContext))
        {
            return cachedContext;
        }

        // Try loading from disk
        var context = await LoadContextFromDiskAsync(projectPath, executionId, ct);
        if (context != null)
        {
            _contextCache[cacheKey] = context;
        }

        return context;
    }

    public async Task<bool> SetValueAsync(
        string projectPath,
        string executionId,
        string key,
        JsonElement value,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var cacheKey = GetCacheKey(projectPath, executionId);
        var writeLock = GetWriteLock(cacheKey);

        await writeLock.WaitAsync(ct);
        try
        {
            var context = await GetContextAsync(projectPath, executionId, ct);
            if (context == null)
            {
                return false;
            }

            context.Variables[key] = value;
            context.UpdatedAt = DateTime.UtcNow;

            // Persist to disk
            await PersistContextAsync(projectPath, executionId, context, ct);

            _logger.LogDebug(
                "Set variable '{Key}' for execution {ExecutionId}",
                key,
                executionId);

            return true;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<JsonElement?> GetValueAsync(
        string projectPath,
        string executionId,
        string key,
        CancellationToken ct = default)
    {
        var context = await GetContextAsync(projectPath, executionId, ct);
        if (context == null)
        {
            return null;
        }

        return context.Variables.TryGetValue(key, out var value) ? value : null;
    }

    public async Task<bool> MergeNodeOutputAsync(
        string projectPath,
        string executionId,
        string nodeId,
        NodeOutput output,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return false;
        }

        var cacheKey = GetCacheKey(projectPath, executionId);
        var writeLock = GetWriteLock(cacheKey);

        await writeLock.WaitAsync(ct);
        try
        {
            var context = await GetContextAsync(projectPath, executionId, ct);
            if (context == null)
            {
                return false;
            }

            context.NodeOutputs[nodeId] = output;
            context.UpdatedAt = DateTime.UtcNow;

            // Persist to disk
            await PersistContextAsync(projectPath, executionId, context, ct);

            _logger.LogDebug(
                "Merged output for node '{NodeId}' in execution {ExecutionId}",
                nodeId,
                executionId);

            return true;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<bool> AddArtifactAsync(
        string projectPath,
        string executionId,
        WorkflowArtifact artifact,
        CancellationToken ct = default)
    {
        var cacheKey = GetCacheKey(projectPath, executionId);
        var writeLock = GetWriteLock(cacheKey);

        await writeLock.WaitAsync(ct);
        try
        {
            var context = await GetContextAsync(projectPath, executionId, ct);
            if (context == null)
            {
                return false;
            }

            context.Artifacts.Add(artifact);
            context.UpdatedAt = DateTime.UtcNow;

            // Persist to disk
            await PersistContextAsync(projectPath, executionId, context, ct);

            _logger.LogDebug(
                "Added artifact '{Name}' to execution {ExecutionId}",
                artifact.Name,
                executionId);

            return true;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<bool> DeleteContextAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default)
    {
        var cacheKey = GetCacheKey(projectPath, executionId);
        var writeLock = GetWriteLock(cacheKey);

        await writeLock.WaitAsync(ct);
        try
        {
            // Check if context exists
            var context = await GetContextAsync(projectPath, executionId, ct);
            if (context == null)
            {
                return false;
            }

            // Remove from cache
            _contextCache.TryRemove(cacheKey, out _);

            // Delete from disk
            var filePath = GetContextFilePath(projectPath, executionId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            _logger.LogDebug(
                "Deleted context for execution {ExecutionId}",
                executionId);

            return true;
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task PersistContextAsync(
        string projectPath,
        string executionId,
        StoredWorkflowContext context,
        CancellationToken ct)
    {
        var workflowsDir = GetWorkflowsDirectory(projectPath);
        Directory.CreateDirectory(workflowsDir);

        var filePath = GetContextFilePath(projectPath, executionId);
        var json = JsonSerializer.Serialize(context, _jsonOptions);

        await File.WriteAllTextAsync(filePath, json, ct);

        _logger.LogDebug("Persisted context to {FilePath}", filePath);
    }

    private async Task<StoredWorkflowContext?> LoadContextFromDiskAsync(
        string projectPath,
        string executionId,
        CancellationToken ct)
    {
        var filePath = GetContextFilePath(projectPath, executionId);

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var context = JsonSerializer.Deserialize<StoredWorkflowContext>(json, _jsonOptions);

            _logger.LogDebug(
                "Loaded context from disk for execution {ExecutionId}",
                executionId);

            return context;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse context file {FilePath}",
                filePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load context file {FilePath}",
                filePath);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose all write locks
        foreach (var lockItem in _writeLocks.Values)
        {
            lockItem.Dispose();
        }

        _writeLocks.Clear();
        _contextCache.Clear();
    }
}

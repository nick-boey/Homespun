using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Internal model for storing workflows with schema version.
/// </summary>
internal class StoredWorkflow
{
    /// <summary>
    /// Schema version for migration support.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// The workflow data.
    /// </summary>
    [JsonPropertyName("workflow")]
    public required WorkflowDefinition Workflow { get; set; }
}

/// <summary>
/// Project-aware implementation of IWorkflowStorageService.
/// Uses a write-through cache pattern: reads are served from an in-memory cache,
/// while writes update the cache immediately and persist to disk.
/// </summary>
public sealed class WorkflowStorageService : IWorkflowStorageService
{
    private const string WorkflowsDirectoryName = ".workflows";
    private const string WorkflowsFilePrefix = "workflows_";
    private const int CurrentSchemaVersion = 1;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WorkflowDefinition>> _workflowCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _cacheInitialized = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<WorkflowStorageService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public WorkflowStorageService(ILogger<WorkflowStorageService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private SemaphoreSlim GetWriteLock(string projectPath)
    {
        return _writeLocks.GetOrAdd(projectPath, _ => new SemaphoreSlim(1, 1));
    }

    private static string GetWorkflowsDirectory(string projectPath)
    {
        return Path.Combine(projectPath, WorkflowsDirectoryName);
    }

    private static string GenerateWorkflowId(string title, DateTime createdAt)
    {
        var input = $"{title}-{createdAt:O}-{Guid.NewGuid()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..6].ToLowerInvariant();
    }

    private async Task<ConcurrentDictionary<string, WorkflowDefinition>> EnsureCacheLoadedAsync(string projectPath, CancellationToken ct)
    {
        var cache = _workflowCache.GetOrAdd(projectPath, _ => new ConcurrentDictionary<string, WorkflowDefinition>(StringComparer.OrdinalIgnoreCase));

        if (!_cacheInitialized.TryGetValue(projectPath, out var initialized) || !initialized)
        {
            _logger.LogDebug("Cache miss for project {ProjectPath}, loading from disk", projectPath);
            var workflows = await LoadWorkflowsFromDiskAsync(projectPath, ct);
            foreach (var workflow in workflows)
            {
                cache[workflow.Id] = workflow;
            }
            _cacheInitialized[projectPath] = true;

            _logger.LogDebug("Loaded {Count} workflows into cache for project: {ProjectPath}", workflows.Count, projectPath);
        }
        else
        {
            _logger.LogDebug("Cache hit for project {ProjectPath}, returning {Count} cached workflows", projectPath, cache.Count);
        }

        return cache;
    }

    private async Task<List<WorkflowDefinition>> LoadWorkflowsFromDiskAsync(string projectPath, CancellationToken ct)
    {
        var workflows = new List<WorkflowDefinition>();
        var workflowsDir = GetWorkflowsDirectory(projectPath);

        if (!Directory.Exists(workflowsDir))
        {
            return workflows;
        }

        var files = Directory.GetFiles(workflowsDir, $"{WorkflowsFilePrefix}*.jsonl");
        foreach (var file in files)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var stored = JsonSerializer.Deserialize<StoredWorkflow>(line, _jsonOptions);
                        if (stored?.Workflow != null)
                        {
                            workflows.Add(stored.Workflow);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse workflow line in {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read workflows file: {File}", file);
            }
        }

        return workflows;
    }

    private async Task PersistWorkflowsAsync(string projectPath, IEnumerable<WorkflowDefinition> workflows, CancellationToken ct)
    {
        var workflowsDir = GetWorkflowsDirectory(projectPath);
        Directory.CreateDirectory(workflowsDir);

        // Delete existing workflow files
        var existingFiles = Directory.GetFiles(workflowsDir, $"{WorkflowsFilePrefix}*.jsonl");
        foreach (var file in existingFiles)
        {
            File.Delete(file);
        }

        // Generate a hash-based filename
        var workflowList = workflows.ToList();
        var contentHash = GenerateContentHash(workflowList);
        var fileName = $"{WorkflowsFilePrefix}{contentHash}.jsonl";
        var filePath = Path.Combine(workflowsDir, fileName);

        await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        foreach (var workflow in workflowList)
        {
            var stored = new StoredWorkflow
            {
                SchemaVersion = CurrentSchemaVersion,
                Workflow = workflow
            };
            var json = JsonSerializer.Serialize(stored, _jsonOptions);
            await writer.WriteLineAsync(json);
        }

        _logger.LogDebug("Persisted {Count} workflows to {File}", workflowList.Count, filePath);
    }

    private static string GenerateContentHash(IEnumerable<WorkflowDefinition> workflows)
    {
        var ids = string.Join(",", workflows.Select(w => w.Id).OrderBy(id => id));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ids + DateTime.UtcNow.Ticks));
        return Convert.ToHexString(hash)[..6].ToLowerInvariant();
    }

    #region IWorkflowStorageService Implementation

    public async Task<WorkflowDefinition?> GetWorkflowAsync(string projectPath, string workflowId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        return cache.TryGetValue(workflowId, out var workflow) ? workflow : null;
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListWorkflowsAsync(string projectPath, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        return cache.Values.ToList();
    }

    public async Task<WorkflowDefinition> CreateWorkflowAsync(string projectPath, CreateWorkflowParams createParams, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);
        var writeLock = GetWriteLock(projectPath);

        await writeLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var workflowId = GenerateWorkflowId(createParams.Title, now);

            var workflow = new WorkflowDefinition
            {
                Id = workflowId,
                ProjectId = createParams.ProjectId,
                Title = createParams.Title,
                Description = createParams.Description,
                Steps = createParams.Steps ?? [],
                Trigger = createParams.Trigger,
                Settings = createParams.Settings ?? new WorkflowSettings(),
                Enabled = createParams.Enabled,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = createParams.CreatedBy
            };

            // Update cache
            cache[workflowId] = workflow;

            // Persist to disk
            await PersistWorkflowsAsync(projectPath, cache.Values, ct);

            _logger.LogInformation("Created workflow '{WorkflowId}': {Title}", workflowId, createParams.Title);

            return workflow;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<WorkflowDefinition?> UpdateWorkflowAsync(string projectPath, string workflowId, UpdateWorkflowParams updateParams, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        if (!cache.TryGetValue(workflowId, out var existing))
        {
            _logger.LogWarning("Workflow '{WorkflowId}' not found in project '{ProjectPath}'", workflowId, projectPath);
            return null;
        }

        var writeLock = GetWriteLock(projectPath);
        await writeLock.WaitAsync(ct);
        try
        {
            // Create updated workflow
            var updated = new WorkflowDefinition
            {
                Id = existing.Id,
                ProjectId = existing.ProjectId,
                Title = updateParams.Title ?? existing.Title,
                Description = updateParams.Description ?? existing.Description,
                Steps = updateParams.Steps ?? existing.Steps,
                Trigger = updateParams.Trigger ?? existing.Trigger,
                Settings = updateParams.Settings ?? existing.Settings,
                Enabled = updateParams.Enabled ?? existing.Enabled,
                Version = existing.Version + 1,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = existing.CreatedBy
            };

            // Update cache
            cache[workflowId] = updated;

            // Persist to disk
            await PersistWorkflowsAsync(projectPath, cache.Values, ct);

            _logger.LogInformation("Updated workflow '{WorkflowId}' to version {Version}", workflowId, updated.Version);

            return updated;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<bool> DeleteWorkflowAsync(string projectPath, string workflowId, CancellationToken ct = default)
    {
        var cache = await EnsureCacheLoadedAsync(projectPath, ct);

        if (!cache.ContainsKey(workflowId))
        {
            _logger.LogWarning("Workflow '{WorkflowId}' not found in project '{ProjectPath}'", workflowId, projectPath);
            return false;
        }

        var writeLock = GetWriteLock(projectPath);
        await writeLock.WaitAsync(ct);
        try
        {
            // Remove from cache
            if (!cache.TryRemove(workflowId, out _))
            {
                return false;
            }

            // Persist to disk
            await PersistWorkflowsAsync(projectPath, cache.Values, ct);

            _logger.LogInformation("Deleted workflow '{WorkflowId}'", workflowId);

            return true;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task ReloadFromDiskAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogDebug("Reloading workflows from disk for project: {ProjectPath}", projectPath);

        // Clear the cache
        _cacheInitialized.TryRemove(projectPath, out _);
        _workflowCache.TryRemove(projectPath, out _);

        // Force re-read from disk
        await EnsureCacheLoadedAsync(projectPath, ct);

        _logger.LogInformation("Reloaded workflows from disk for project: {ProjectPath}", projectPath);
    }

    #endregion

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
        _workflowCache.Clear();
        _cacheInitialized.Clear();
    }
}

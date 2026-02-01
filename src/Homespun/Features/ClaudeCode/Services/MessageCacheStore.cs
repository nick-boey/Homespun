using System.Collections.Concurrent;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// JSONL-based store for persisting session messages.
/// Thread-safe implementation that supports concurrent message appends.
/// </summary>
/// <remarks>
/// Storage structure:
/// {baseDir}/
///   {projectId}/
///     {sessionId}.jsonl           # Messages, one per line
///     {sessionId}.meta.json       # Session metadata for quick access
///   index.json                    # Global session index
/// </remarks>
public class MessageCacheStore : IMessageCacheStore
{
    private readonly string _baseDir;
    private readonly ILogger<MessageCacheStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private SessionIndex _index;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false // Compact for JSONL
    };

    private static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public MessageCacheStore(string baseDir, ILogger<MessageCacheStore> logger)
    {
        _baseDir = baseDir;
        _logger = logger;
        _index = LoadIndexSync();
    }

    /// <inheritdoc />
    public async Task InitializeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        SessionMode? mode,
        string? model,
        CancellationToken cancellationToken = default)
    {
        var sessionLock = GetSessionLock(sessionId);
        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            var projectDir = Path.Combine(_baseDir, projectId);
            Directory.CreateDirectory(projectDir);

            var now = DateTime.UtcNow;
            var metadata = new SessionMetadataEntry
            {
                SessionId = sessionId,
                EntityId = entityId,
                ProjectId = projectId,
                MessageCount = 0,
                CreatedAt = now,
                LastMessageAt = now,
                Mode = mode,
                Model = model
            };

            // Save metadata file
            var metaPath = GetMetaPath(projectId, sessionId);
            await SaveMetadataAsync(metaPath, metadata, cancellationToken);

            // Update index
            await UpdateIndexAsync(sessionId, projectId, entityId, cancellationToken);

            _logger.LogDebug("Initialized session cache for {SessionId} in project {ProjectId}",
                sessionId, projectId);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task AppendMessageAsync(string sessionId, ClaudeMessage message, CancellationToken cancellationToken = default)
    {
        var entry = GetIndexEntry(sessionId);
        if (entry == null)
        {
            throw new InvalidOperationException(
                $"Session {sessionId} has not been initialized. Call InitializeSessionAsync first.");
        }

        var sessionLock = GetSessionLock(sessionId);
        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            var jsonlPath = GetJsonlPath(entry.ProjectId, sessionId);
            var metaPath = GetMetaPath(entry.ProjectId, sessionId);

            // Append message to JSONL file
            var json = JsonSerializer.Serialize(message, JsonOptions);
            await File.AppendAllTextAsync(jsonlPath, json + Environment.NewLine, cancellationToken);

            // Update metadata
            var metadata = await LoadMetadataAsync(metaPath, cancellationToken);
            if (metadata != null)
            {
                metadata.MessageCount++;
                metadata.LastMessageAt = DateTime.UtcNow;
                await SaveMetadataAsync(metaPath, metadata, cancellationToken);
            }

            _logger.LogDebug("Appended message {MessageId} to session {SessionId}",
                message.Id, sessionId);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClaudeMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var entry = GetIndexEntry(sessionId);
        if (entry == null)
        {
            return Array.Empty<ClaudeMessage>();
        }

        var jsonlPath = GetJsonlPath(entry.ProjectId, sessionId);
        if (!File.Exists(jsonlPath))
        {
            return Array.Empty<ClaudeMessage>();
        }

        var messages = new List<ClaudeMessage>();
        var lines = await File.ReadAllLinesAsync(jsonlPath, cancellationToken);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var message = JsonSerializer.Deserialize<ClaudeMessage>(line, JsonOptions);
                if (message != null)
                {
                    messages.Add(message);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize message in session {SessionId}", sessionId);
            }
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task<SessionCacheSummary?> GetSessionSummaryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var entry = GetIndexEntry(sessionId);
        if (entry == null)
        {
            return null;
        }

        var metaPath = GetMetaPath(entry.ProjectId, sessionId);
        var metadata = await LoadMetadataAsync(metaPath, cancellationToken);
        if (metadata == null)
        {
            return null;
        }

        return new SessionCacheSummary(
            SessionId: metadata.SessionId,
            EntityId: metadata.EntityId,
            ProjectId: metadata.ProjectId,
            MessageCount: metadata.MessageCount,
            CreatedAt: metadata.CreatedAt,
            LastMessageAt: metadata.LastMessageAt,
            Mode: metadata.Mode,
            Model: metadata.Model
        );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionCacheSummary>> ListSessionsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var sessionIds = _index.SessionProjects
            .Where(kvp => kvp.Value == projectId)
            .Select(kvp => kvp.Key)
            .ToList();

        var summaries = new List<SessionCacheSummary>();
        foreach (var sessionId in sessionIds)
        {
            var summary = await GetSessionSummaryAsync(sessionId, cancellationToken);
            if (summary != null)
            {
                summaries.Add(summary);
            }
        }

        // Order by LastMessageAt descending
        return summaries.OrderByDescending(s => s.LastMessageAt).ToList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetSessionIdsForEntityAsync(string projectId, string entityId, CancellationToken cancellationToken = default)
    {
        var sessionIds = _index.EntitySessions
            .Where(kvp => kvp.Key == (projectId, entityId))
            .SelectMany(kvp => kvp.Value)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(sessionIds);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetIndexEntry(sessionId) != null);
    }

    #region Private Methods

    private string GetJsonlPath(string projectId, string sessionId)
        => Path.Combine(_baseDir, projectId, $"{sessionId}.jsonl");

    private string GetMetaPath(string projectId, string sessionId)
        => Path.Combine(_baseDir, projectId, $"{sessionId}.meta.json");

    private string GetIndexPath() => Path.Combine(_baseDir, "index.json");

    private SemaphoreSlim GetSessionLock(string sessionId)
        => _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    private IndexEntry? GetIndexEntry(string sessionId)
    {
        if (_index.SessionProjects.TryGetValue(sessionId, out var projectId))
        {
            var entityId = _index.SessionEntities.GetValueOrDefault(sessionId);
            return new IndexEntry(projectId, entityId ?? string.Empty);
        }
        return null;
    }

    private SessionIndex LoadIndexSync()
    {
        var indexPath = GetIndexPath();
        if (!File.Exists(indexPath))
        {
            return new SessionIndex();
        }

        try
        {
            var json = File.ReadAllText(indexPath);
            return JsonSerializer.Deserialize<SessionIndex>(json, MetaJsonOptions) ?? new SessionIndex();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load session index from {IndexPath}, starting fresh", indexPath);
            return new SessionIndex();
        }
    }

    private async Task UpdateIndexAsync(string sessionId, string projectId, string entityId, CancellationToken cancellationToken)
    {
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            _index.SessionProjects[sessionId] = projectId;
            _index.SessionEntities[sessionId] = entityId;

            var key = (projectId, entityId);
            if (!_index.EntitySessions.TryGetValue(key, out var sessionList))
            {
                sessionList = [];
                _index.EntitySessions[key] = sessionList;
            }
            if (!sessionList.Contains(sessionId))
            {
                sessionList.Add(sessionId);
            }

            await SaveIndexAsync(cancellationToken);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task SaveIndexAsync(CancellationToken cancellationToken)
    {
        var indexPath = GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        var json = JsonSerializer.Serialize(_index, MetaJsonOptions);
        await File.WriteAllTextAsync(indexPath, json, cancellationToken);
    }

    private async Task<SessionMetadataEntry?> LoadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<SessionMetadataEntry>(json, MetaJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load session metadata from {Path}", path);
            return null;
        }
    }

    private async Task SaveMetadataAsync(string path, SessionMetadataEntry metadata, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(metadata, MetaJsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    #endregion

    #region Internal Types

    private record IndexEntry(string ProjectId, string EntityId);

    /// <summary>
    /// Session metadata stored in .meta.json files.
    /// </summary>
    private class SessionMetadataEntry
    {
        public string SessionId { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public int MessageCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastMessageAt { get; set; }
        public SessionMode? Mode { get; set; }
        public string? Model { get; set; }
    }

    /// <summary>
    /// Global index for quick session lookups.
    /// </summary>
    private class SessionIndex
    {
        public Dictionary<string, string> SessionProjects { get; set; } = [];
        public Dictionary<string, string> SessionEntities { get; set; } = [];

        // Composite key workaround for JSON serialization
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<(string ProjectId, string EntityId), List<string>> EntitySessions { get; set; } = [];

        // Serializable version
        public List<EntitySessionMapping> EntitySessionMappings { get; set; } = [];

        public class EntitySessionMapping
        {
            public string ProjectId { get; set; } = string.Empty;
            public string EntityId { get; set; } = string.Empty;
            public List<string> SessionIds { get; set; } = [];
        }
    }

    #endregion
}

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// JSON file-based store for session metadata.
/// Thread-safe implementation that persists data to a JSON file.
/// </summary>
public class SessionMetadataStore : ISessionMetadataStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<SessionMetadataStore> _logger;
    private List<SessionMetadata> _metadata = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SessionMetadataStore(string filePath, ILogger<SessionMetadataStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        LoadSync();
    }

    /// <inheritdoc />
    public Task<SessionMetadata?> GetBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var metadata = _metadata.FirstOrDefault(m => m.SessionId == sessionId);
        return Task.FromResult(metadata);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionMetadata>> GetByEntityIdAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var result = _metadata.Where(m => m.EntityId == entityId).ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<SessionMetadata>>(result);
    }

    /// <inheritdoc />
    public async Task SaveAsync(SessionMetadata metadata, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Remove existing if present (upsert)
            _metadata.RemoveAll(m => m.SessionId == metadata.SessionId);
            _metadata.Add(metadata);
            await SaveInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var removed = _metadata.RemoveAll(m => m.SessionId == sessionId);
            if (removed > 0)
            {
                await SaveInternalAsync(cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionMetadata>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SessionMetadata>>(_metadata.AsReadOnly());
    }

    private void LoadSync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<StoreData>(json, JsonOptions);
                _metadata = data?.Metadata ?? [];
                _logger.LogInformation("Loaded {Count} session metadata records from {FilePath}",
                    _metadata.Count, _filePath);
            }
            else
            {
                _logger.LogDebug("Session metadata file not found at {FilePath}, starting with empty store", _filePath);
                _metadata = [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load session metadata from {FilePath}, starting with empty store", _filePath);
            _metadata = [];
        }
    }

    private async Task SaveInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var data = new StoreData { Metadata = _metadata };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
            _logger.LogDebug("Saved {Count} session metadata records to {FilePath}", _metadata.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save session metadata to {FilePath}", _filePath);
            throw;
        }
    }

    /// <summary>
    /// Internal data structure for JSON serialization.
    /// </summary>
    private class StoreData
    {
        public List<SessionMetadata> Metadata { get; set; } = [];
    }
}

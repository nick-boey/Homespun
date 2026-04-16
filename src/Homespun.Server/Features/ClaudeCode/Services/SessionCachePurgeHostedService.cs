using Microsoft.Extensions.Hosting;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// One-shot startup cleanup for the legacy <c>MessageCacheStore</c> JSONL layout.
///
/// <para>
/// Before <c>a2a-native-messaging</c>, session caches lived at
/// <c>{baseDir}/{projectId}/{sessionId}.jsonl</c> and held <c>ClaudeMessage</c>
/// records. The new A2A-native pipeline writes
/// <c>{baseDir}/{projectId}/{sessionId}.events.jsonl</c> instead. The two formats
/// cannot be cross-read; leaving the old files on disk risks confusing diagnostics
/// and filling disk with unreadable state.
/// </para>
///
/// <para>
/// This service runs once at startup, enumerates legacy <c>*.jsonl</c> files (plus
/// the companion <c>*.meta.json</c> and top-level <c>index.json</c>) under the
/// configured cache directory, and deletes them. Files whose name ends in
/// <c>.events.jsonl</c> are explicitly skipped — those are the new A2A event logs.
/// </para>
///
/// <para>
/// Set <c>HOMESPUN_SKIP_CACHE_PURGE=true</c> to opt out — useful for forensic
/// inspection of pre-upgrade state. The new runtime still will not read those files.
/// </para>
/// </summary>
public sealed class SessionCachePurgeHostedService : IHostedService
{
    private readonly string _baseDir;
    private readonly ILogger<SessionCachePurgeHostedService> _logger;
    private readonly bool _skip;

    public SessionCachePurgeHostedService(
        string baseDir,
        IConfiguration configuration,
        ILogger<SessionCachePurgeHostedService> logger)
    {
        _baseDir = baseDir;
        _logger = logger;
        _skip = string.Equals(
            configuration["HOMESPUN_SKIP_CACHE_PURGE"]
                ?? Environment.GetEnvironmentVariable("HOMESPUN_SKIP_CACHE_PURGE"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_skip)
        {
            _logger.LogInformation(
                "Session cache purge skipped (HOMESPUN_SKIP_CACHE_PURGE=true). Legacy *.jsonl files in {BaseDir} remain on disk but will not be read.",
                _baseDir);
            return Task.CompletedTask;
        }

        if (!Directory.Exists(_baseDir))
        {
            _logger.LogDebug("Session cache purge: base directory {BaseDir} does not exist; nothing to do.", _baseDir);
            return Task.CompletedTask;
        }

        var deleted = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(_baseDir, "*.jsonl", SearchOption.AllDirectories))
            {
                // Skip the new A2A event logs; purge only the pre-upgrade ClaudeMessage JSONL.
                if (file.EndsWith(".events.jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                TryDelete(file, ref deleted);
            }

            foreach (var meta in Directory.EnumerateFiles(_baseDir, "*.meta.json", SearchOption.AllDirectories))
            {
                TryDelete(meta, ref deleted);
            }

            var indexPath = Path.Combine(_baseDir, "index.json");
            if (File.Exists(indexPath))
            {
                TryDelete(indexPath, ref deleted);
            }

            if (deleted > 0)
            {
                _logger.LogWarning(
                    "Session cache purge deleted {Count} legacy ClaudeMessage file(s) under {BaseDir}. In-progress sessions from before the upgrade will need to be restarted.",
                    deleted,
                    _baseDir);
            }
            else
            {
                _logger.LogDebug(
                    "Session cache purge: no legacy ClaudeMessage files found under {BaseDir}.",
                    _baseDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Session cache purge encountered an error enumerating {BaseDir}. Partial purge may have occurred ({Count} files deleted).",
                _baseDir, deleted);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void TryDelete(string path, ref int deleted)
    {
        try
        {
            File.Delete(path);
            deleted++;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Session cache purge failed to delete legacy cache file {Path}. It will be ignored at runtime; delete manually if desired.",
                path);
        }
    }
}

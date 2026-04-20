using System.Collections.Concurrent;
using System.Text.Json;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// JSONL-backed, append-only implementation of <see cref="IA2AEventStore"/>.
///
/// <para>
/// Storage layout:
/// </para>
/// <code>
/// {baseDir}/
///   {projectId}/
///     {sessionId}.events.jsonl     # one A2AEventRecord JSON per line
/// </code>
///
/// <para>
/// Seq allocation uses a per-session <see cref="SemaphoreSlim"/> so concurrent appends to the
/// same session serialize and receive strictly monotonic sequence numbers. Different sessions
/// append in parallel. The current seq counter is cached in memory per session; on first
/// access (either an initial append or a read) the counter is recovered by scanning the
/// existing JSONL for its last line, so a process restart does not reset seq.
/// </para>
///
/// <para>
/// The store intentionally does not expose an "index.json" sidecar — the filesystem layout is
/// the index. A <see cref="ConcurrentDictionary{TKey,TValue}"/> caches the
/// sessionId→projectId mapping discovered via disk scan, so <see cref="ReadAsync"/> can
/// resolve a session without the caller passing a projectId.
/// </para>
/// </summary>
public sealed class A2AEventStore : IA2AEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly string _baseDir;
    private readonly ILogger<A2AEventStore> _logger;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private readonly ConcurrentDictionary<string, long> _sessionMaxSeq = new();
    private readonly ConcurrentDictionary<string, string> _sessionToProject = new();

    public A2AEventStore(string baseDir, ILogger<A2AEventStore> logger)
    {
        _baseDir = baseDir;
        _logger = logger;

        // Ensure the base directory exists so the initial filesystem scan does not throw
        // and subsequent appends can write project subdirectories beneath it.
        Directory.CreateDirectory(_baseDir);

        // Rebuild in-memory indices from disk so a restart continues seq allocation
        // correctly and ReadAsync can find pre-existing sessions without a prior append.
        ScanExistingSessions();
    }

    /// <inheritdoc />
    public async Task<A2AEventRecord> AppendAsync(
        string projectId,
        string sessionId,
        string eventKind,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        var sessionLock = GetSessionLock(sessionId);
        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            // Ensure seq counter is initialized from disk if this is the first access.
            var currentSeq = _sessionMaxSeq.GetOrAdd(sessionId, _ =>
                RecoverMaxSeqFromDisk(projectId, sessionId));
            var nextSeq = currentSeq + 1;

            var record = new A2AEventRecord(
                Seq: nextSeq,
                SessionId: sessionId,
                EventId: Guid.NewGuid().ToString(),
                EventKind: eventKind,
                ReceivedAt: DateTime.UtcNow,
                Payload: payload);

            var jsonlPath = GetJsonlPath(projectId, sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(jsonlPath)!);

            var json = JsonSerializer.Serialize(record, JsonOptions);
            await File.AppendAllTextAsync(jsonlPath, json + Environment.NewLine, cancellationToken);

            _sessionMaxSeq[sessionId] = nextSeq;
            _sessionToProject[sessionId] = projectId;

            _logger.LogDebug(
                "Appended A2A event seq={Seq} eventId={EventId} kind={Kind} to session {SessionId}",
                nextSeq, record.EventId, eventKind, sessionId);

            return record;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<A2AEventRecord>?> ReadAsync(
        string sessionId,
        long? since = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionToProject.TryGetValue(sessionId, out var projectId))
        {
            return null;
        }

        var jsonlPath = GetJsonlPath(projectId, sessionId);
        if (!File.Exists(jsonlPath))
        {
            // Session known to the index but file missing — treat as unknown.
            return null;
        }

        var lowerBound = since ?? 0;
        var events = new List<A2AEventRecord>();
        using var stream = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            A2AEventRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<A2AEventRecord>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to deserialize A2AEventRecord line in session {SessionId}; skipping",
                    sessionId);
                continue;
            }

            if (record is null) continue;
            if (record.Seq <= lowerBound) continue;
            events.Add(record);
        }

        // Events may already be ordered on disk, but tolerate out-of-order lines by sorting.
        events.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        return events;
    }

    private void ScanExistingSessions()
    {
        if (!Directory.Exists(_baseDir)) return;

        foreach (var file in Directory.EnumerateFiles(_baseDir, "*.events.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                var fileName = Path.GetFileName(file);
                // "{sessionId}.events.jsonl"
                if (!fileName.EndsWith(".events.jsonl", StringComparison.Ordinal)) continue;

                var sessionId = fileName[..^".events.jsonl".Length];
                var projectDir = Path.GetDirectoryName(file);
                if (projectDir is null) continue;
                var projectId = Path.GetFileName(projectDir);
                if (string.IsNullOrEmpty(projectId)) continue;

                _sessionToProject[sessionId] = projectId;
                // Seq recovery is deferred until first append/read for the session.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to index pre-existing event log {File}; will not be served until reappended",
                    file);
            }
        }
    }

    private long RecoverMaxSeqFromDisk(string projectId, string sessionId)
    {
        var path = GetJsonlPath(projectId, sessionId);
        if (!File.Exists(path)) return 0;

        long max = 0;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = JsonSerializer.Deserialize<A2AEventRecord>(line, JsonOptions);
                    if (record is null) continue;
                    if (record.Seq > max) max = record.Seq;
                }
                catch (JsonException)
                {
                    // Tolerant of corrupted tail; continue scanning.
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "Failed to recover max seq for session {SessionId} at {Path}; starting from 0",
                sessionId, path);
        }

        return max;
    }

    private string GetJsonlPath(string projectId, string sessionId)
        => Path.Combine(_baseDir, projectId, $"{sessionId}.events.jsonl");

    private SemaphoreSlim GetSessionLock(string sessionId)
        => _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
}

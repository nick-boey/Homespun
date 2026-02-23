using System.Text.Json;
using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for managing issue history, enabling undo/redo functionality.
/// Stores full issue snapshots as timestamped JSONL files in the .fleece/history/ folder.
/// </summary>
public sealed class IssueHistoryService : IIssueHistoryService
{
    private const string HistoryFolderName = "history";
    private const string CurrentStateFileName = "current.json";
    private const string SnapshotExtension = ".jsonl";
    private const string MetadataExtension = ".meta.json";

    private readonly ILogger<IssueHistoryService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public IssueHistoryService(ILogger<IssueHistoryService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Gets the history folder path for a project.
    /// </summary>
    private static string GetHistoryPath(string projectPath) =>
        Path.Combine(projectPath, ".fleece", HistoryFolderName);

    /// <summary>
    /// Gets the current state file path.
    /// </summary>
    private static string GetCurrentStatePath(string projectPath) =>
        Path.Combine(GetHistoryPath(projectPath), CurrentStateFileName);

    /// <summary>
    /// Converts a timestamp to a filesystem-safe filename (replacing : with -).
    /// </summary>
    private static string TimestampToFilename(string timestamp) =>
        timestamp.Replace(":", "-");

    /// <summary>
    /// Converts a filesystem-safe filename back to a timestamp.
    /// </summary>
    private static string FilenameToTimestamp(string filename)
    {
        // Remove extension and convert - back to : in time portion
        var name = Path.GetFileNameWithoutExtension(filename);
        if (name.EndsWith(".meta"))
            name = Path.GetFileNameWithoutExtension(name);

        // Format is: 2026-02-22T10-30-00-000Z -> 2026-02-22T10:30:00.000Z
        // The date part has -, so we only replace in the time part after T
        var tIndex = name.IndexOf('T');
        if (tIndex > 0 && tIndex < name.Length - 1)
        {
            var datePart = name[..tIndex];
            var timePart = name[(tIndex + 1)..];
            // Replace only the first two - in time part (hours:minutes:seconds)
            var parts = timePart.Split('-');
            if (parts.Length >= 3)
            {
                // Reconstruct: HH:MM:SS.sssZ
                return $"{datePart}T{parts[0]}:{parts[1]}:{parts[2]}.{string.Join("", parts.Skip(3))}";
            }
        }
        return name;
    }

    /// <summary>
    /// Gets the snapshot file path for a timestamp.
    /// </summary>
    private static string GetSnapshotPath(string projectPath, string timestamp) =>
        Path.Combine(GetHistoryPath(projectPath), TimestampToFilename(timestamp) + SnapshotExtension);

    /// <summary>
    /// Gets the metadata file path for a timestamp.
    /// </summary>
    private static string GetMetadataPath(string projectPath, string timestamp) =>
        Path.Combine(GetHistoryPath(projectPath), TimestampToFilename(timestamp) + MetadataExtension);

    /// <inheritdoc />
    public async Task RecordSnapshotAsync(
        string projectPath,
        IReadOnlyList<Issue> issues,
        string operationType,
        string? issueId,
        string? description,
        CancellationToken ct = default)
    {
        var historyPath = GetHistoryPath(projectPath);
        Directory.CreateDirectory(historyPath);

        // Check if we need to truncate future history
        var isAtLatest = await IsAtLatestAsync(projectPath, ct);
        if (!isAtLatest)
        {
            await TruncateFutureHistoryAsync(projectPath, ct);
        }

        // Generate timestamp for this snapshot
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        // Write the issues snapshot as JSONL (one JSON object per line)
        var snapshotPath = GetSnapshotPath(projectPath, timestamp);
        await using (var writer = new StreamWriter(snapshotPath, false))
        {
            foreach (var issue in issues)
            {
                var json = JsonSerializer.Serialize(issue, _jsonOptions);
                await writer.WriteLineAsync(json);
            }
        }

        // Write the metadata
        var metadata = new IssueHistoryEntry
        {
            Timestamp = timestamp,
            OperationType = operationType,
            IssueId = issueId,
            Description = description
        };
        var metadataPath = GetMetadataPath(projectPath, timestamp);
        var metadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);
        await File.WriteAllTextAsync(metadataPath, metadataJson, ct);

        // Update current state to point to this snapshot
        var currentStatePath = GetCurrentStatePath(projectPath);
        var currentState = new { timestamp };
        var currentStateJson = JsonSerializer.Serialize(currentState, _jsonOptions);
        await File.WriteAllTextAsync(currentStatePath, currentStateJson, ct);

        _logger.LogDebug(
            "Recorded history snapshot at {Timestamp}: {OperationType} {IssueId} - {Description}",
            timestamp, operationType, issueId, description);

        // Prune old entries if we exceed the limit
        await PruneOldEntriesAsync(projectPath, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Issue>?> UndoAsync(string projectPath, CancellationToken ct = default)
    {
        var state = await GetStateInternalAsync(projectPath, ct);
        if (!state.CanUndo || state.CurrentTimestamp == null)
        {
            _logger.LogDebug("Cannot undo: no previous history entry available");
            return null;
        }

        // Find the previous timestamp
        var timestamps = state.Timestamps;
        var currentIndex = timestamps.IndexOf(state.CurrentTimestamp);
        if (currentIndex <= 0)
        {
            _logger.LogDebug("Cannot undo: already at the beginning of history");
            return null;
        }

        var previousTimestamp = timestamps[currentIndex - 1];

        // Load issues from previous snapshot
        var issues = await LoadSnapshotAsync(projectPath, previousTimestamp, ct);
        if (issues == null)
        {
            _logger.LogWarning("Failed to load snapshot for timestamp {Timestamp}", previousTimestamp);
            return null;
        }

        // Update current state
        await UpdateCurrentTimestampAsync(projectPath, previousTimestamp, ct);

        _logger.LogInformation("Undo to {Timestamp}: loaded {Count} issues", previousTimestamp, issues.Count);
        return issues;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Issue>?> RedoAsync(string projectPath, CancellationToken ct = default)
    {
        var state = await GetStateInternalAsync(projectPath, ct);
        if (!state.CanRedo || state.CurrentTimestamp == null)
        {
            _logger.LogDebug("Cannot redo: no next history entry available");
            return null;
        }

        // Find the next timestamp
        var timestamps = state.Timestamps;
        var currentIndex = timestamps.IndexOf(state.CurrentTimestamp);
        if (currentIndex < 0 || currentIndex >= timestamps.Count - 1)
        {
            _logger.LogDebug("Cannot redo: already at the end of history");
            return null;
        }

        var nextTimestamp = timestamps[currentIndex + 1];

        // Load issues from next snapshot
        var issues = await LoadSnapshotAsync(projectPath, nextTimestamp, ct);
        if (issues == null)
        {
            _logger.LogWarning("Failed to load snapshot for timestamp {Timestamp}", nextTimestamp);
            return null;
        }

        // Update current state
        await UpdateCurrentTimestampAsync(projectPath, nextTimestamp, ct);

        _logger.LogInformation("Redo to {Timestamp}: loaded {Count} issues", nextTimestamp, issues.Count);
        return issues;
    }

    /// <inheritdoc />
    public async Task<IssueHistoryState> GetStateAsync(string projectPath, CancellationToken ct = default)
    {
        var state = await GetStateInternalAsync(projectPath, ct);

        var result = new IssueHistoryState
        {
            CurrentTimestamp = state.CurrentTimestamp,
            CanUndo = state.CanUndo,
            CanRedo = state.CanRedo,
            TotalEntries = state.Timestamps.Count
        };

        // Get descriptions for undo/redo operations
        if (state.CanUndo && state.CurrentTimestamp != null)
        {
            var currentIndex = state.Timestamps.IndexOf(state.CurrentTimestamp);
            if (currentIndex >= 0)
            {
                var metadata = await LoadMetadataAsync(projectPath, state.CurrentTimestamp, ct);
                result.UndoDescription = metadata?.Description ?? metadata?.OperationType;
            }
        }

        if (state.CanRedo && state.CurrentTimestamp != null)
        {
            var currentIndex = state.Timestamps.IndexOf(state.CurrentTimestamp);
            if (currentIndex >= 0 && currentIndex < state.Timestamps.Count - 1)
            {
                var nextTimestamp = state.Timestamps[currentIndex + 1];
                var metadata = await LoadMetadataAsync(projectPath, nextTimestamp, ct);
                result.RedoDescription = metadata?.Description ?? metadata?.OperationType;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> IsAtLatestAsync(string projectPath, CancellationToken ct = default)
    {
        var state = await GetStateInternalAsync(projectPath, ct);
        return !state.CanRedo;
    }

    /// <summary>
    /// Internal state representation with full timestamp list.
    /// </summary>
    private class InternalHistoryState
    {
        public string? CurrentTimestamp { get; init; }
        public List<string> Timestamps { get; init; } = [];
        public bool CanUndo => CurrentTimestamp != null && Timestamps.Count > 0 && Timestamps.IndexOf(CurrentTimestamp) > 0;
        public bool CanRedo => CurrentTimestamp != null && Timestamps.Count > 0 && Timestamps.IndexOf(CurrentTimestamp) < Timestamps.Count - 1;
    }

    /// <summary>
    /// Gets the internal history state with full timestamp list.
    /// </summary>
    private async Task<InternalHistoryState> GetStateInternalAsync(string projectPath, CancellationToken ct)
    {
        var historyPath = GetHistoryPath(projectPath);

        if (!Directory.Exists(historyPath))
        {
            return new InternalHistoryState();
        }

        // Get all snapshot files and extract timestamps
        var snapshotFiles = Directory.GetFiles(historyPath, $"*{SnapshotExtension}")
            .Where(f => !f.EndsWith(MetadataExtension))
            .OrderBy(f => f)
            .ToList();

        var timestamps = snapshotFiles
            .Select(f => FilenameToTimestamp(Path.GetFileName(f)))
            .ToList();

        // Read current timestamp
        string? currentTimestamp = null;
        var currentStatePath = GetCurrentStatePath(projectPath);
        if (File.Exists(currentStatePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(currentStatePath, ct);
                using var doc = JsonDocument.Parse(json);
                currentTimestamp = doc.RootElement.GetProperty("timestamp").GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read current state file");
            }
        }

        // If no current timestamp or it's invalid, use the latest
        if (currentTimestamp == null || !timestamps.Contains(currentTimestamp))
        {
            currentTimestamp = timestamps.LastOrDefault();
        }

        return new InternalHistoryState
        {
            CurrentTimestamp = currentTimestamp,
            Timestamps = timestamps
        };
    }

    /// <summary>
    /// Loads issues from a snapshot file.
    /// </summary>
    private async Task<IReadOnlyList<Issue>?> LoadSnapshotAsync(string projectPath, string timestamp, CancellationToken ct)
    {
        var snapshotPath = GetSnapshotPath(projectPath, timestamp);
        if (!File.Exists(snapshotPath))
        {
            _logger.LogWarning("Snapshot file not found: {Path}", snapshotPath);
            return null;
        }

        try
        {
            var issues = new List<Issue>();
            using var reader = new StreamReader(snapshotPath);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var issue = JsonSerializer.Deserialize<Issue>(line, _jsonOptions);
                if (issue != null)
                {
                    issues.Add(issue);
                }
            }
            return issues;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load snapshot from {Path}", snapshotPath);
            return null;
        }
    }

    /// <summary>
    /// Loads metadata for a history entry.
    /// </summary>
    private async Task<IssueHistoryEntry?> LoadMetadataAsync(string projectPath, string timestamp, CancellationToken ct)
    {
        var metadataPath = GetMetadataPath(projectPath, timestamp);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, ct);
            return JsonSerializer.Deserialize<IssueHistoryEntry>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load metadata from {Path}", metadataPath);
            return null;
        }
    }

    /// <summary>
    /// Updates the current timestamp in the state file.
    /// </summary>
    private async Task UpdateCurrentTimestampAsync(string projectPath, string timestamp, CancellationToken ct)
    {
        var currentStatePath = GetCurrentStatePath(projectPath);
        var currentState = new { timestamp };
        var json = JsonSerializer.Serialize(currentState, _jsonOptions);
        await File.WriteAllTextAsync(currentStatePath, json, ct);
    }

    /// <summary>
    /// Truncates history entries after the current position.
    /// Called when making changes while viewing a historical state.
    /// </summary>
    private async Task TruncateFutureHistoryAsync(string projectPath, CancellationToken ct)
    {
        var state = await GetStateInternalAsync(projectPath, ct);
        if (state.CurrentTimestamp == null || !state.CanRedo)
        {
            return;
        }

        var currentIndex = state.Timestamps.IndexOf(state.CurrentTimestamp);
        if (currentIndex < 0 || currentIndex >= state.Timestamps.Count - 1)
        {
            return;
        }

        // Delete all entries after the current position
        var timestampsToDelete = state.Timestamps.Skip(currentIndex + 1).ToList();
        foreach (var timestamp in timestampsToDelete)
        {
            var snapshotPath = GetSnapshotPath(projectPath, timestamp);
            var metadataPath = GetMetadataPath(projectPath, timestamp);

            try
            {
                if (File.Exists(snapshotPath))
                    File.Delete(snapshotPath);
                if (File.Exists(metadataPath))
                    File.Delete(metadataPath);

                _logger.LogDebug("Deleted future history entry: {Timestamp}", timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete history entry: {Timestamp}", timestamp);
            }
        }

        _logger.LogInformation("Truncated {Count} future history entries", timestampsToDelete.Count);
    }

    /// <summary>
    /// Prunes old history entries when the limit is exceeded.
    /// </summary>
    private async Task PruneOldEntriesAsync(string projectPath, CancellationToken ct)
    {
        var state = await GetStateInternalAsync(projectPath, ct);
        if (state.Timestamps.Count <= IIssueHistoryService.MaxHistoryEntries)
        {
            return;
        }

        var entriesToRemove = state.Timestamps.Count - IIssueHistoryService.MaxHistoryEntries;
        var timestampsToDelete = state.Timestamps.Take(entriesToRemove).ToList();

        foreach (var timestamp in timestampsToDelete)
        {
            var snapshotPath = GetSnapshotPath(projectPath, timestamp);
            var metadataPath = GetMetadataPath(projectPath, timestamp);

            try
            {
                if (File.Exists(snapshotPath))
                    File.Delete(snapshotPath);
                if (File.Exists(metadataPath))
                    File.Delete(metadataPath);

                _logger.LogDebug("Pruned old history entry: {Timestamp}", timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prune history entry: {Timestamp}", timestamp);
            }
        }

        _logger.LogInformation("Pruned {Count} old history entries", entriesToRemove);
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Loads Claude Code sessions from JSONL files for use in mock/demo mode.
/// </summary>
public class JsonlSessionLoader : IJsonlSessionLoader
{
    private readonly ILogger<JsonlSessionLoader> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonlSessionLoader(ILogger<JsonlSessionLoader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<ClaudeMessage>> LoadMessagesAsync(string jsonlPath, CancellationToken cancellationToken = default)
    {
        var messages = new List<ClaudeMessage>();

        if (!File.Exists(jsonlPath))
        {
            _logger.LogWarning("JSONL file not found: {Path}", jsonlPath);
            return messages;
        }

        var lines = await File.ReadAllLinesAsync(jsonlPath, cancellationToken);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var message = JsonSerializer.Deserialize<ClaudeMessage>(line, JsonOptions);
                if (message != null)
                {
                    // Mark as not streaming (critical for loaded sessions)
                    message.IsStreaming = false;
                    foreach (var content in message.Content)
                    {
                        content.IsStreaming = false;
                    }

                    // Filter streaming artifacts (duplicate/empty blocks)
                    FilterDuplicateToolUseBlocks(message);
                    FilterEmptyContentBlocks(message);

                    messages.Add(message);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse message line in {Path}", jsonlPath);
            }
        }

        _logger.LogDebug("Loaded {Count} messages from {Path}", messages.Count, jsonlPath);
        return messages;
    }

    /// <inheritdoc />
    public async Task<ClaudeSession?> LoadSessionFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Directory not found: {Path}", directoryPath);
            return null;
        }

        // Find the first JSONL file in the directory
        var jsonlFiles = Directory.GetFiles(directoryPath, "*.jsonl");
        if (jsonlFiles.Length == 0)
        {
            _logger.LogDebug("No JSONL files found in {Path}", directoryPath);
            return null;
        }

        // Use the first JSONL file
        var jsonlPath = jsonlFiles[0];
        var sessionId = Path.GetFileNameWithoutExtension(jsonlPath);

        // Load messages
        var messages = await LoadMessagesAsync(jsonlPath, cancellationToken);
        if (messages.Count == 0)
        {
            _logger.LogDebug("No messages found in {Path}", jsonlPath);
            return null;
        }

        // Try to load metadata
        var metaPath = Path.Combine(directoryPath, $"{sessionId}.meta.json");
        var metadata = await LoadMetadataAsync(metaPath, cancellationToken);

        // Create the session
        var projectId = metadata?.ProjectId ?? Path.GetFileName(directoryPath);
        var session = new ClaudeSession
        {
            Id = sessionId,
            EntityId = metadata?.EntityId ?? string.Empty,
            ProjectId = projectId,
            WorkingDirectory = directoryPath,
            Mode = metadata?.Mode ?? SessionMode.Build,
            Model = metadata?.Model ?? "sonnet",
            Status = metadata?.Status ?? ClaudeSessionStatus.WaitingForInput,
            CreatedAt = metadata?.CreatedAt ?? messages.First().CreatedAt,
            LastActivityAt = metadata?.LastMessageAt ?? messages.Last().CreatedAt,
            PlanContent = metadata?.PlanContent,
            PlanFilePath = metadata?.PlanFilePath
        };

        // Add messages to session
        foreach (var message in messages)
        {
            session.Messages.Add(message);
        }

        _logger.LogInformation("Loaded session {SessionId} with {MessageCount} messages from {Path}",
            sessionId, messages.Count, directoryPath);

        return session;
    }

    /// <inheritdoc />
    public async Task<List<ClaudeSession>> LoadAllSessionsAsync(string baseDirectory, CancellationToken cancellationToken = default)
    {
        var sessions = new List<ClaudeSession>();

        if (!Directory.Exists(baseDirectory))
        {
            _logger.LogWarning("Base directory not found: {Path}", baseDirectory);
            return sessions;
        }

        // Iterate through project subdirectories
        foreach (var projectDir in Directory.GetDirectories(baseDirectory))
        {
            // Find all JSONL files in this project directory
            var jsonlFiles = Directory.GetFiles(projectDir, "*.jsonl");
            foreach (var jsonlFile in jsonlFiles)
            {
                var sessionId = Path.GetFileNameWithoutExtension(jsonlFile);

                try
                {
                    // Load messages
                    var messages = await LoadMessagesAsync(jsonlFile, cancellationToken);
                    if (messages.Count == 0)
                        continue;

                    // Try to load metadata
                    var metaPath = Path.Combine(projectDir, $"{sessionId}.meta.json");
                    var metadata = await LoadMetadataAsync(metaPath, cancellationToken);

                    // Create session
                    var projectId = metadata?.ProjectId ?? Path.GetFileName(projectDir);
                    var session = new ClaudeSession
                    {
                        Id = sessionId,
                        EntityId = metadata?.EntityId ?? string.Empty,
                        ProjectId = projectId,
                        WorkingDirectory = projectDir,
                        Mode = metadata?.Mode ?? SessionMode.Build,
                        Model = metadata?.Model ?? "sonnet",
                        Status = metadata?.Status ?? ClaudeSessionStatus.WaitingForInput,
                        CreatedAt = metadata?.CreatedAt ?? messages.First().CreatedAt,
                        LastActivityAt = metadata?.LastMessageAt ?? messages.Last().CreatedAt,
                        PlanContent = metadata?.PlanContent,
                        PlanFilePath = metadata?.PlanFilePath
                    };

                    foreach (var message in messages)
                    {
                        session.Messages.Add(message);
                    }

                    sessions.Add(session);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load session from {Path}", jsonlFile);
                }
            }
        }

        _logger.LogInformation("Loaded {Count} sessions from {Path}", sessions.Count, baseDirectory);
        return sessions;
    }

    private async Task<SessionMetadata?> LoadMetadataAsync(string metaPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metaPath, cancellationToken);
            return JsonSerializer.Deserialize<SessionMetadata>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load metadata from {Path}", metaPath);
            return null;
        }
    }

    /// <summary>
    /// Removes duplicate ToolUse blocks that are streaming artifacts.
    /// When streaming, Claude creates blocks with empty input first, then fills them.
    /// The JSONL may contain both the empty placeholders and the filled versions.
    /// Only removes empty blocks when there's a duplicate with the same toolUseId that has content.
    /// </summary>
    private static void FilterDuplicateToolUseBlocks(ClaudeMessage message)
    {
        if (message.Content.Count <= 1) return;

        // Group by toolUseId to find duplicates
        var toolUseGroups = message.Content
            .Where(c => c.Type == ClaudeContentType.ToolUse && !string.IsNullOrEmpty(c.ToolUseId))
            .GroupBy(c => c.ToolUseId)
            .Where(g => g.Count() > 1)
            .ToList();

        // For each group with duplicates, keep only the one with content
        foreach (var group in toolUseGroups)
        {
            var withContent = group.FirstOrDefault(c => !string.IsNullOrEmpty(c.ToolInput));
            if (withContent != null)
            {
                // Remove the empty duplicates, keep the one with content
                foreach (var block in group.Where(c => c != withContent))
                {
                    message.Content.Remove(block);
                }
            }
        }
    }

    /// <summary>
    /// Removes empty text blocks (streaming artifacts).
    /// </summary>
    private static void FilterEmptyContentBlocks(ClaudeMessage message)
    {
        var emptyBlocks = message.Content
            .Where(c => c.Type == ClaudeContentType.Text && string.IsNullOrWhiteSpace(c.Text))
            .ToList();

        foreach (var block in emptyBlocks)
        {
            message.Content.Remove(block);
        }
    }

    /// <summary>
    /// Internal class for deserializing session metadata files.
    /// </summary>
    private class SessionMetadata
    {
        public string SessionId { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public int MessageCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastMessageAt { get; set; }
        public SessionMode? Mode { get; set; }
        public string? Model { get; set; }
        public ClaudeSessionStatus? Status { get; set; }
        public string? PlanContent { get; set; }
        public string? PlanFilePath { get; set; }
    }
}

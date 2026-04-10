using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Fleece.Core.Services;
using FleeceServiceFactory = Fleece.Core.Services.FleeceService;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Helper for loading and saving Fleece issues directly from/to disk.
/// Handles JSONL serialization/deserialization since Fleece.Core v2 internalized
/// the storage types (JsonlSerializer, JsonlStorageService, SchemaValidator).
/// </summary>
internal static class FleeceFileHelper
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    // Keep backward compat alias
    private static JsonSerializerOptions JsonOptions => SerializerOptions;

    /// <summary>
    /// Loads all issues from a project path by reading and parsing JSONL files directly.
    /// Handles deduplication (last occurrence of an ID wins).
    /// </summary>
    public static async Task<IReadOnlyList<Issue>> LoadIssuesAsync(string projectPath, CancellationToken ct = default)
    {
        var fleeceDir = Path.Combine(projectPath, ".fleece");
        if (!Directory.Exists(fleeceDir))
        {
            return [];
        }

        var jsonlFiles = Directory.GetFiles(fleeceDir, "issues_*.jsonl");

        // Also check for the stable consolidated file (issues.jsonl)
        var stableFile = Path.Combine(fleeceDir, "issues.jsonl");
        if (File.Exists(stableFile))
        {
            jsonlFiles = [.. jsonlFiles, stableFile];
        }

        if (jsonlFiles.Length == 0)
        {
            return [];
        }

        // Load all issues, deduplicating by ID (last occurrence wins)
        var issueMap = new Dictionary<string, Issue>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in jsonlFiles.OrderBy(f => f))
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var issue = JsonSerializer.Deserialize<Issue>(line, JsonOptions);
                    if (issue != null && !string.IsNullOrEmpty(issue.Id))
                    {
                        issueMap[issue.Id] = issue;
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }

        return issueMap.Values.ToList();
    }

    /// <summary>
    /// Saves issues to a project path by writing a consolidated JSONL file.
    /// Removes existing issues_*.jsonl files and writes a single new file.
    /// </summary>
    public static async Task SaveIssuesAsync(string projectPath, IEnumerable<Issue> issues, CancellationToken ct = default)
    {
        var fleeceDir = Path.Combine(projectPath, ".fleece");
        if (!Directory.Exists(fleeceDir))
        {
            Directory.CreateDirectory(fleeceDir);
        }

        // Remove existing issue files
        foreach (var file in Directory.GetFiles(fleeceDir, "issues_*.jsonl"))
        {
            File.Delete(file);
        }

        // Write consolidated file
        var lines = issues.Select(issue => JsonSerializer.Serialize(issue, JsonOptions));
        var fileName = $"issues_{Guid.NewGuid().ToString("N")[..6]}.jsonl";
        await File.WriteAllLinesAsync(Path.Combine(fleeceDir, fileName), lines, ct);
    }

    /// <summary>
    /// Creates a Fleece.Core v2 IFleeceService instance for the given project path.
    /// </summary>
    public static global::Fleece.Core.Services.Interfaces.IFleeceService CreateFleeceService(string projectPath)
    {
        var settingsService = new SettingsService(projectPath);
        var gitConfigService = new GitConfigService(settingsService);
        return FleeceServiceFactory.ForFile(projectPath, settingsService, gitConfigService);
    }
}

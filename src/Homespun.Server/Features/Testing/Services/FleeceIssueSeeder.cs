using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Helper service to write Fleece issues as JSONL files.
/// Matches Fleece.Core's serialization format for compatibility with the real FleeceService.
/// </summary>
public sealed class FleeceIssueSeeder
{
    private readonly ILogger<FleeceIssueSeeder> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public FleeceIssueSeeder(ILogger<FleeceIssueSeeder> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }

    /// <summary>
    /// Seeds issues to a project's .fleece directory by writing them as JSONL.
    /// Creates the .fleece directory if it doesn't exist.
    /// </summary>
    /// <param name="projectPath">Path to the project root directory.</param>
    /// <param name="issues">The issues to seed.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SeedIssuesAsync(string projectPath, IReadOnlyList<Issue> issues, CancellationToken ct = default)
    {
        if (issues.Count == 0)
        {
            _logger.LogDebug("No issues to seed for project: {ProjectPath}", projectPath);
            return;
        }

        // Create .fleece directory
        var fleeceDir = Path.Combine(projectPath, ".fleece");
        Directory.CreateDirectory(fleeceDir);

        // Generate a short hash for the filename (matching Fleece.Core convention)
        var hash = Guid.NewGuid().ToString("N")[..6];
        var issuesFilePath = Path.Combine(fleeceDir, $"issues_{hash}.jsonl");

        // Write issues as JSONL (one JSON object per line)
        await using var writer = new StreamWriter(issuesFilePath, false);
        foreach (var issue in issues)
        {
            var json = JsonSerializer.Serialize(issue, _jsonOptions);
            await writer.WriteLineAsync(json);
        }

        _logger.LogDebug(
            "Seeded {Count} issues to {FilePath}",
            issues.Count,
            issuesFilePath);
    }

    /// <summary>
    /// Creates a minimal project structure with .gitignore and README.md files.
    /// This ensures the project directory is valid for FleeceService operations.
    /// </summary>
    /// <param name="projectPath">Path to the project root directory.</param>
    /// <param name="projectName">The display name of the project.</param>
    public void CreateMinimalProjectStructure(string projectPath, string projectName)
    {
        Directory.CreateDirectory(projectPath);

        // Create .gitignore
        var gitignorePath = Path.Combine(projectPath, ".gitignore");
        if (!File.Exists(gitignorePath))
        {
            File.WriteAllText(gitignorePath, """
                # IDE
                .idea/
                .vscode/
                *.swp

                # Build
                bin/
                obj/
                node_modules/

                # OS
                .DS_Store
                Thumbs.db
                """);
        }

        // Create README.md
        var readmePath = Path.Combine(projectPath, "README.md");
        if (!File.Exists(readmePath))
        {
            File.WriteAllText(readmePath, $"""
                # {projectName}

                This is a mock project for testing and demonstration purposes.
                """);
        }

        _logger.LogDebug("Created minimal project structure at {ProjectPath}", projectPath);
    }
}

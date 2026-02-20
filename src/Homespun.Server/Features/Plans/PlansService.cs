using System.Security;
using Homespun.Shared.Models.Plans;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Plans;

/// <summary>
/// Service for listing and reading plan files from Claude Code sessions.
/// </summary>
public class PlansService(ILogger<PlansService> logger) : IPlansService
{
    private const int PreviewLineCount = 3;

    /// <inheritdoc />
    public async Task<List<PlanFileInfo>> ListPlanFilesAsync(string workingDirectory, CancellationToken ct = default)
    {
        var plansPath = GetPlansDirectory(workingDirectory);

        if (!Directory.Exists(plansPath))
        {
            logger.LogDebug("Plans directory does not exist: {PlansPath}", plansPath);
            return [];
        }

        var planFiles = new List<PlanFileInfo>();

        foreach (var filePath in Directory.GetFiles(plansPath, "*.md"))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var fileInfo = new FileInfo(filePath);
                var preview = await GetPreviewAsync(filePath, ct);

                planFiles.Add(new PlanFileInfo
                {
                    FileName = fileInfo.Name,
                    FilePath = filePath,
                    LastModified = fileInfo.LastWriteTime,
                    FileSizeBytes = fileInfo.Length,
                    Preview = preview
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error reading plan file: {FilePath}", filePath);
            }
        }

        // Order by last modified date, newest first
        return planFiles.OrderByDescending(p => p.LastModified).ToList();
    }

    /// <inheritdoc />
    public async Task<string?> GetPlanContentAsync(string workingDirectory, string fileName, CancellationToken ct = default)
    {
        ValidateFileName(fileName);

        var plansPath = GetPlansDirectory(workingDirectory);
        var filePath = Path.Combine(plansPath, fileName);

        // Additional security check: ensure the resolved path is still within the plans directory
        var fullPath = Path.GetFullPath(filePath);
        var fullPlansPath = Path.GetFullPath(plansPath);
        if (!fullPath.StartsWith(fullPlansPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Access denied: path traversal detected for file '{fileName}'");
        }

        if (!File.Exists(filePath))
        {
            logger.LogDebug("Plan file not found: {FilePath}", filePath);
            return null;
        }

        return await File.ReadAllTextAsync(filePath, ct);
    }

    private static string GetPlansDirectory(string workingDirectory)
    {
        return Path.Combine(workingDirectory, ".claude", "plans");
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new SecurityException("File name cannot be empty");
        }

        // Check for any path separator characters
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
        {
            throw new SecurityException($"Invalid file name: '{fileName}' contains path separators or traversal sequences");
        }

        // Check for invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (fileName.Any(c => invalidChars.Contains(c)))
        {
            throw new SecurityException($"Invalid file name: '{fileName}' contains invalid characters");
        }
    }

    private static async Task<string?> GetPreviewAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var lines = new List<string>();
            using var reader = new StreamReader(filePath);

            for (int i = 0; i < PreviewLineCount && !reader.EndOfStream; i++)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line != null)
                {
                    lines.Add(line);
                }
            }

            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }
        catch
        {
            return null;
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Homespun.Features.Commands;
using Homespun.Features.PullRequests.Data;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Search;

/// <summary>
/// Service for retrieving files from a project's git repository.
/// Uses git ls-files to get all tracked files.
/// </summary>
public class ProjectFileService(
    IDataStore dataStore,
    ICommandRunner commandRunner,
    ILogger<ProjectFileService> logger) : IProjectFileService
{
    /// <inheritdoc />
    public async Task<FileListResult> GetFilesAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null)
        {
            logger.LogWarning("Project {ProjectId} not found", projectId);
            throw new KeyNotFoundException($"Project '{projectId}' not found");
        }

        var result = await commandRunner.RunAsync("git", "ls-files", project.LocalPath);

        if (!result.Success)
        {
            logger.LogError("Failed to list files for project {ProjectId}: {Error}", projectId, result.Error);
            throw new InvalidOperationException($"Failed to list files: {result.Error}");
        }

        var files = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(f => f)
            .ToList();

        var hash = ComputeHash(files);

        logger.LogDebug("Listed {FileCount} files for project {ProjectId}, hash: {Hash}",
            files.Count, projectId, hash);

        return new FileListResult(files.AsReadOnly(), hash);
    }

    private static string ComputeHash(IEnumerable<string> sortedFiles)
    {
        var content = string.Join("\n", sortedFiles);
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToBase64String(hashBytes);
    }
}

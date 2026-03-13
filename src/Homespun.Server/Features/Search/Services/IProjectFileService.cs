namespace Homespun.Features.Search;

/// <summary>
/// Service for retrieving files from a project's git repository.
/// </summary>
public interface IProjectFileService
{
    /// <summary>
    /// Gets all tracked files in the project's repository.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>A result containing the sorted file list and a content hash</returns>
    Task<FileListResult> GetFilesAsync(string projectId);
}

/// <summary>
/// Result of fetching files from a repository.
/// </summary>
/// <param name="Files">Sorted list of file paths relative to repository root</param>
/// <param name="Hash">SHA256 hash of the file list for cache invalidation</param>
public record FileListResult(IReadOnlyList<string> Files, string Hash);

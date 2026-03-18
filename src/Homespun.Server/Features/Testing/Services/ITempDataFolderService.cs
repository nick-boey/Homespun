namespace Homespun.Features.Testing.Services;

/// <summary>
/// Interface for managing temporary data folders in mock mode.
/// Creates a temporary folder structure that mirrors production layout,
/// enabling real file-based services to operate on temporary data.
/// </summary>
public interface ITempDataFolderService : IDisposable
{
    /// <summary>
    /// Gets the root path of the temporary data folder.
    /// Example: /tmp/homespun-mock-{guid}/
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Gets the path to the homespun-data.json file.
    /// Example: /tmp/homespun-mock-{guid}/homespun-data.json
    /// </summary>
    string DataFilePath { get; }

    /// <summary>
    /// Gets the path to the sessions directory.
    /// Example: /tmp/homespun-mock-{guid}/sessions/
    /// </summary>
    string SessionsPath { get; }

    /// <summary>
    /// Gets the path for a specific project's directory.
    /// Example: /tmp/homespun-mock-{guid}/projects/{projectId}/
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <returns>The full path to the project directory.</returns>
    string GetProjectPath(string projectId);

    /// <summary>
    /// Ensures all required directories exist.
    /// Called during initialization.
    /// </summary>
    void EnsureDirectoriesExist();
}

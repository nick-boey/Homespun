using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Manages a temporary folder structure for mock mode.
/// Creates a folder structure that mirrors production layout:
///
/// {temp}/homespun-mock-{guid}/
/// ├── homespun-data.json          # Projects, PRs, prompts
/// ├── sessions/                   # Session cache
/// └── projects/
///     └── demo-project/
///         ├── .fleece/
///         │   └── issues_{hash}.jsonl
///         ├── .gitignore
///         └── README.md
/// </summary>
public sealed class TempDataFolderService : ITempDataFolderService
{
    private readonly ILogger<TempDataFolderService> _logger;
    private readonly string _rootPath;
    private bool _disposed;

    public TempDataFolderService(ILogger<TempDataFolderService> logger)
    {
        _logger = logger;
        _rootPath = Path.Combine(Path.GetTempPath(), $"homespun-mock-{Guid.NewGuid():N}");

        _logger.LogInformation("Created temporary data folder at: {RootPath}", _rootPath);
        EnsureDirectoriesExist();
    }

    /// <inheritdoc />
    public string RootPath => _rootPath;

    /// <inheritdoc />
    public string DataFilePath => Path.Combine(_rootPath, "homespun-data.json");

    /// <inheritdoc />
    public string SessionsPath => Path.Combine(_rootPath, "sessions");

    /// <inheritdoc />
    public string GetProjectPath(string projectId)
    {
        return Path.Combine(_rootPath, "projects", projectId);
    }

    /// <inheritdoc />
    public void EnsureDirectoriesExist()
    {
        // Create root directory
        Directory.CreateDirectory(_rootPath);

        // Create sessions directory
        Directory.CreateDirectory(SessionsPath);

        // Create projects directory
        var projectsPath = Path.Combine(_rootPath, "projects");
        Directory.CreateDirectory(projectsPath);

        _logger.LogDebug("Ensured directories exist at {RootPath}", _rootPath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
                _logger.LogInformation("Cleaned up temporary data folder: {RootPath}", _rootPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temporary data folder: {RootPath}", _rootPath);
        }
    }
}

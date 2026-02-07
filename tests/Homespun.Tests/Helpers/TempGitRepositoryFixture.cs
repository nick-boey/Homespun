using System.Diagnostics;

namespace Homespun.Tests.Helpers;

/// <summary>
/// Fixture that creates a temporary git repository for integration testing.
/// Automatically cleans up the repository when disposed.
/// </summary>
/// <remarks>
/// Each fixture creates a unique parent directory to avoid worktree path collisions
/// between test runs. The repository is created inside a "main" subdirectory,
/// and worktrees are created as siblings (following GitWorktreeService convention).
/// This ensures each test run is fully isolated.
/// </remarks>
public class TempGitRepositoryFixture : IDisposable
{
    /// <summary>
    /// The path to the git repository (inside the unique parent directory).
    /// </summary>
    public string RepositoryPath { get; }

    /// <summary>
    /// The unique parent directory containing the repository and its worktrees.
    /// </summary>
    public string ParentPath { get; }

    public string InitialCommitHash { get; private set; } = "";

    private bool _disposed;

    public TempGitRepositoryFixture()
    {
        // Create a unique parent directory per fixture to isolate worktrees
        // The repo is created as "main" subdirectory, and worktrees will be siblings
        // This prevents path collisions between different test runs
        ParentPath = Path.Combine(Path.GetTempPath(), "Homespun_IntegrationTests", Guid.NewGuid().ToString("N"));
        RepositoryPath = Path.Combine(ParentPath, "main");
        Directory.CreateDirectory(RepositoryPath);

        InitializeRepository();
    }

    private void InitializeRepository()
    {
        // Initialize git repo
        RunGit("init");

        // Configure git user for commits
        RunGit("config user.email \"test@example.com\"");
        RunGit("config user.name \"Test User\"");

        // Create an initial commit so we have a valid HEAD
        var readmePath = Path.Combine(RepositoryPath, "README.md");
        File.WriteAllText(readmePath, "# Test Repository\n\nThis is a test repository for integration tests.");

        RunGit("add .");
        RunGit("commit -m \"Initial commit\"");

        // Get the initial commit hash
        InitialCommitHash = RunGit("rev-parse HEAD").Trim();
    }

    /// <summary>
    /// Creates a new branch in the test repository.
    /// </summary>
    public void CreateBranch(string branchName, bool checkout = false)
    {
        RunGit($"branch \"{branchName}\"");
        if (checkout)
        {
            RunGit($"checkout \"{branchName}\"");
        }
    }

    /// <summary>
    /// Creates a file and commits it to the repository.
    /// </summary>
    public void CreateFileAndCommit(string fileName, string content, string commitMessage)
    {
        var filePath = Path.Combine(RepositoryPath, fileName);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, content);
        RunGit($"add \"{fileName}\"");
        RunGit($"commit -m \"{commitMessage}\"");
    }

    /// <summary>
    /// Runs a git command in the test repository.
    /// </summary>
    public string RunGit(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = RepositoryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed: git {arguments}\nError: {error}");
        }

        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (Directory.Exists(RepositoryPath))
            {
                // First, get list of worktrees and clean them up
                CleanupWorktrees();
            }

            // Delete the entire parent directory (includes repo and all worktrees)
            if (Directory.Exists(ParentPath))
            {
                ForceDeleteDirectory(ParentPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Cleans up any clones created during testing.
    /// </summary>
    private void CleanupWorktrees()
    {
        try
        {
            // Clean up .clones directory
            var clonesDir = Path.Combine(ParentPath, ".clones");
            if (Directory.Exists(clonesDir))
            {
                ForceDeleteDirectory(clonesDir);
            }

            // Also clean up legacy .worktrees directory
            var worktreesDir = Path.Combine(ParentPath, ".worktrees");
            if (Directory.Exists(worktreesDir))
            {
                ForceDeleteDirectory(worktreesDir);
            }
        }
        catch
        {
            // Best effort - clone cleanup is not critical
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }
}

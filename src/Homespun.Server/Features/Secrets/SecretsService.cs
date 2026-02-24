using System.Text.RegularExpressions;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Secrets;

namespace Homespun.Features.Secrets;

/// <summary>
/// Service for managing project secrets stored in secrets.env files.
/// </summary>
public partial class SecretsService(
    IProjectService projectService,
    ILogger<SecretsService> logger) : ISecretsService
{
    private const string SecretsFileName = "secrets.env";

    // Valid environment variable name: starts with letter or underscore, followed by letters, digits, or underscores
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidEnvVarNameRegex();

    public async Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string projectId)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project {ProjectId} not found when getting secrets", projectId);
            return [];
        }

        var secretsPath = GetSecretsFilePath(project.LocalPath);
        if (!File.Exists(secretsPath))
        {
            return [];
        }

        var secrets = new List<SecretInfo>();
        var fileInfo = new FileInfo(secretsPath);

        foreach (var line in await File.ReadAllLinesAsync(secretsPath))
        {
            var parsed = ParseEnvLine(line);
            if (parsed.HasValue)
            {
                secrets.Add(new SecretInfo
                {
                    Name = parsed.Value.Name,
                    LastModified = fileInfo.LastWriteTimeUtc
                });
            }
        }

        return secrets;
    }

    public async Task<bool> SetSecretAsync(string projectId, string name, string value)
    {
        ValidateSecretName(name);

        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project {ProjectId} not found when setting secret", projectId);
            return false;
        }

        var secretsPath = GetSecretsFilePath(project.LocalPath);
        var lines = new List<string>();
        var found = false;

        if (File.Exists(secretsPath))
        {
            foreach (var line in await File.ReadAllLinesAsync(secretsPath))
            {
                var parsed = ParseEnvLine(line);
                if (parsed.HasValue && parsed.Value.Name == name)
                {
                    lines.Add($"{name}={value}");
                    found = true;
                }
                else
                {
                    lines.Add(line);
                }
            }
        }

        if (!found)
        {
            lines.Add($"{name}={value}");
        }

        // Ensure directory exists
        var directory = Path.GetDirectoryName(secretsPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllLinesAsync(secretsPath, lines);
        logger.LogInformation("Secret {SecretName} set for project {ProjectId}", name, projectId);

        return true;
    }

    public async Task<bool> DeleteSecretAsync(string projectId, string name)
    {
        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project {ProjectId} not found when deleting secret", projectId);
            return false;
        }

        var secretsPath = GetSecretsFilePath(project.LocalPath);
        if (!File.Exists(secretsPath))
        {
            return false;
        }

        var lines = new List<string>();
        var found = false;

        foreach (var line in await File.ReadAllLinesAsync(secretsPath))
        {
            var parsed = ParseEnvLine(line);
            if (parsed.HasValue && parsed.Value.Name == name)
            {
                found = true;
                // Skip this line (delete it)
            }
            else
            {
                lines.Add(line);
            }
        }

        if (!found)
        {
            return false;
        }

        await File.WriteAllLinesAsync(secretsPath, lines);
        logger.LogInformation("Secret {SecretName} deleted from project {ProjectId}", name, projectId);

        return true;
    }

    public Task<Dictionary<string, string>> GetSecretsForInjectionAsync(string projectPath)
    {
        var secrets = new Dictionary<string, string>();

        // Try to find secrets.env in project path or parent (for branch paths)
        var secretsPath = FindSecretsFile(projectPath);
        if (secretsPath == null || !File.Exists(secretsPath))
        {
            return Task.FromResult(secrets);
        }

        foreach (var line in File.ReadAllLines(secretsPath))
        {
            var parsed = ParseEnvLine(line);
            if (parsed.HasValue)
            {
                secrets[parsed.Value.Name] = parsed.Value.Value;
            }
        }

        logger.LogDebug("Loaded {Count} secrets from {Path}", secrets.Count, secretsPath);
        return Task.FromResult(secrets);
    }

    public async Task<Dictionary<string, string>> GetSecretsForInjectionByProjectIdAsync(string projectId)
    {
        var secrets = new Dictionary<string, string>();

        var project = await projectService.GetByIdAsync(projectId);
        if (project == null)
        {
            logger.LogWarning("Project {ProjectId} not found when getting secrets for injection", projectId);
            return secrets;
        }

        var secretsPath = GetSecretsFilePath(project.LocalPath);
        if (!File.Exists(secretsPath))
        {
            return secrets;
        }

        foreach (var line in await File.ReadAllLinesAsync(secretsPath))
        {
            var parsed = ParseEnvLine(line);
            if (parsed.HasValue)
            {
                secrets[parsed.Value.Name] = parsed.Value.Value;
            }
        }

        logger.LogDebug("Loaded {Count} secrets from {Path} for project {ProjectId}", secrets.Count, secretsPath, projectId);
        return secrets;
    }

    /// <summary>
    /// Gets the path to the secrets.env file for a project.
    /// The secrets file is stored at the project root (parent of LocalPath which is the branch folder).
    /// </summary>
    private static string GetSecretsFilePath(string localPath)
    {
        // LocalPath is like /projects/myproject/main (branch folder)
        // secrets.env goes in /projects/myproject/
        var projectRoot = Path.GetDirectoryName(localPath);
        return string.IsNullOrEmpty(projectRoot)
            ? Path.Combine(localPath, SecretsFileName)
            : Path.Combine(projectRoot, SecretsFileName);
    }

    /// <summary>
    /// Finds the secrets.env file starting from the given path.
    /// Checks the path itself and parent directory.
    /// </summary>
    private static string? FindSecretsFile(string path)
    {
        // Check if secrets.env is directly in path
        var directPath = Path.Combine(path, SecretsFileName);
        if (File.Exists(directPath))
        {
            return directPath;
        }

        // Check parent directory (for branch paths)
        var parentPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parentPath))
        {
            var parentSecretsPath = Path.Combine(parentPath, SecretsFileName);
            if (File.Exists(parentSecretsPath))
            {
                return parentSecretsPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a line from an .env file.
    /// </summary>
    private static (string Name, string Value)? ParseEnvLine(string line)
    {
        // Skip empty lines and comments
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
        {
            return null;
        }

        // Find the first = sign
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return null;
        }

        var name = trimmed[..equalsIndex].Trim();
        var value = trimmed[(equalsIndex + 1)..];

        // Validate name
        if (!ValidEnvVarNameRegex().IsMatch(name))
        {
            return null;
        }

        return (name, value);
    }

    /// <summary>
    /// Validates a secret name is a valid environment variable name.
    /// </summary>
    private static void ValidateSecretName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Secret name cannot be empty.", nameof(name));
        }

        if (!ValidEnvVarNameRegex().IsMatch(name))
        {
            throw new ArgumentException(
                "Secret name must be a valid environment variable name: start with a letter or underscore, followed by letters, digits, or underscores only.",
                nameof(name));
        }
    }
}

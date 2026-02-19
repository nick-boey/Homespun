using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Secrets;
using Homespun.Shared.Models.Secrets;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of ISecretsService that stores secrets in memory.
/// </summary>
public partial class MockSecretsService : ISecretsService
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<MockSecretsService> _logger;

    // In-memory storage: projectId -> (secretName -> secretValue)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _secrets = new();

    // Track when secrets were last modified per project
    private readonly ConcurrentDictionary<string, DateTime> _lastModified = new();

    // Valid environment variable name: starts with letter or underscore, followed by letters, digits, or underscores
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidEnvVarNameRegex();

    public MockSecretsService(IDataStore dataStore, ILogger<MockSecretsService> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    public Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string projectId)
    {
        _logger.LogDebug("[Mock] GetSecrets for project {ProjectId}", projectId);

        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("[Mock] Project {ProjectId} not found when getting secrets", projectId);
            return Task.FromResult<IReadOnlyList<SecretInfo>>([]);
        }

        if (!_secrets.TryGetValue(projectId, out var projectSecrets))
        {
            return Task.FromResult<IReadOnlyList<SecretInfo>>([]);
        }

        var lastModified = _lastModified.GetValueOrDefault(projectId, DateTime.UtcNow);
        var secrets = projectSecrets.Keys
            .Select(name => new SecretInfo
            {
                Name = name,
                LastModified = lastModified
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SecretInfo>>(secrets);
    }

    public Task<bool> SetSecretAsync(string projectId, string name, string value)
    {
        _logger.LogDebug("[Mock] SetSecret {SecretName} for project {ProjectId}", name, projectId);

        ValidateSecretName(name);

        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("[Mock] Project {ProjectId} not found when setting secret", projectId);
            return Task.FromResult(false);
        }

        var projectSecrets = _secrets.GetOrAdd(projectId, _ => new ConcurrentDictionary<string, string>());
        projectSecrets[name] = value;
        _lastModified[projectId] = DateTime.UtcNow;

        _logger.LogInformation("[Mock] Secret {SecretName} set for project {ProjectId}", name, projectId);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteSecretAsync(string projectId, string name)
    {
        _logger.LogDebug("[Mock] DeleteSecret {SecretName} for project {ProjectId}", name, projectId);

        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("[Mock] Project {ProjectId} not found when deleting secret", projectId);
            return Task.FromResult(false);
        }

        if (!_secrets.TryGetValue(projectId, out var projectSecrets))
        {
            return Task.FromResult(false);
        }

        var removed = projectSecrets.TryRemove(name, out _);
        if (removed)
        {
            _lastModified[projectId] = DateTime.UtcNow;
            _logger.LogInformation("[Mock] Secret {SecretName} deleted from project {ProjectId}", name, projectId);
        }

        return Task.FromResult(removed);
    }

    public Task<Dictionary<string, string>> GetSecretsForInjectionAsync(string projectPath)
    {
        _logger.LogDebug("[Mock] GetSecretsForInjection for path {ProjectPath}", projectPath);

        // In mock mode, try to find a project by matching the path
        // Mock projects have paths like /mock/projects/{name}
        var project = _dataStore.Projects.FirstOrDefault(p =>
            projectPath.StartsWith(p.LocalPath, StringComparison.OrdinalIgnoreCase) ||
            p.LocalPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase));

        if (project == null)
        {
            _logger.LogDebug("[Mock] No project found for path {ProjectPath}", projectPath);
            return Task.FromResult(new Dictionary<string, string>());
        }

        if (!_secrets.TryGetValue(project.Id, out var projectSecrets))
        {
            return Task.FromResult(new Dictionary<string, string>());
        }

        var secrets = new Dictionary<string, string>(projectSecrets);
        _logger.LogDebug("[Mock] Loaded {Count} secrets for injection from project {ProjectId}", secrets.Count, project.Id);

        return Task.FromResult(secrets);
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

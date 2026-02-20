using Homespun.Shared.Models.Secrets;

namespace Homespun.Features.Secrets;

/// <summary>
/// Service for managing project secrets (environment variables for worker containers).
/// </summary>
public interface ISecretsService
{
    /// <summary>
    /// Gets all secrets for a project (names only, never values).
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <returns>List of secret information.</returns>
    Task<IReadOnlyList<SecretInfo>> GetSecretsAsync(string projectId);

    /// <summary>
    /// Creates or updates a secret for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="name">Secret name (must be valid env var name).</param>
    /// <param name="value">Secret value.</param>
    /// <returns>True if successful, false if project not found.</returns>
    Task<bool> SetSecretAsync(string projectId, string name, string value);

    /// <summary>
    /// Deletes a secret from a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="name">Secret name.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteSecretAsync(string projectId, string name);

    /// <summary>
    /// Gets all secrets as key-value pairs for container injection.
    /// This method takes a project path instead of ID for use during container startup.
    /// </summary>
    /// <param name="projectPath">Path to project folder (can be branch path, will resolve to project root).</param>
    /// <returns>Dictionary of secret name to value pairs.</returns>
    Task<Dictionary<string, string>> GetSecretsForInjectionAsync(string projectPath);
}

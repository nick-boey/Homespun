using Homespun.Shared.Models.Secrets;

namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating or updating a secret.
/// </summary>
public class CreateSecretRequest
{
    /// <summary>
    /// The secret name (must be valid environment variable name).
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The secret value.
    /// </summary>
    public required string Value { get; set; }
}

/// <summary>
/// Request model for updating an existing secret's value.
/// </summary>
public class UpdateSecretRequest
{
    /// <summary>
    /// The new secret value.
    /// </summary>
    public required string Value { get; set; }
}

/// <summary>
/// Response model for listing project secrets.
/// </summary>
public class SecretsListResponse
{
    /// <summary>
    /// List of secret information (names only, never values).
    /// </summary>
    public required IReadOnlyList<SecretInfo> Secrets { get; init; }
}

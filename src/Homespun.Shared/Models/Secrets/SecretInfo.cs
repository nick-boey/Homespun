namespace Homespun.Shared.Models.Secrets;

/// <summary>
/// Information about a project secret (name only, never the value).
/// </summary>
public record SecretInfo
{
    /// <summary>
    /// The secret name (environment variable name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When the secret was last modified.
    /// </summary>
    public DateTime? LastModified { get; init; }
}

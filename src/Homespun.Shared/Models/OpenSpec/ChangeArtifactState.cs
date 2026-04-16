namespace Homespun.Shared.Models.OpenSpec;

/// <summary>
/// The output of <c>openspec status --change &lt;name&gt; --json</c>.
/// </summary>
public class ChangeArtifactState
{
    /// <summary>
    /// The change directory name (e.g. <c>add-openspec-integration</c>).
    /// </summary>
    public required string ChangeName { get; init; }

    /// <summary>
    /// The schema in use (e.g. <c>spec-driven</c>).
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// True when every artifact required before apply is <c>done</c>.
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Artifact ids whose <c>status</c> must be <c>done</c> before the change can be applied.
    /// </summary>
    public List<string> ApplyRequires { get; init; } = new();

    /// <summary>
    /// Per-artifact status information.
    /// </summary>
    public List<ChangeArtifact> Artifacts { get; init; } = new();
}

/// <summary>
/// Per-artifact status as reported by <c>openspec status</c>.
/// </summary>
public class ChangeArtifact
{
    public required string Id { get; init; }
    public required string OutputPath { get; init; }
    /// <summary>
    /// One of <c>done</c>, <c>pending</c>, or a schema-specific status.
    /// </summary>
    public required string Status { get; init; }
}

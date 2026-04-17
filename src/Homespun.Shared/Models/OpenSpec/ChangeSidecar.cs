namespace Homespun.Shared.Models.OpenSpec;

/// <summary>
/// Metadata sidecar stored at <c>openspec/changes/&lt;name&gt;/.homespun.yaml</c>.
/// Provides the link between an OpenSpec change directory and the Fleece issue that owns it.
/// </summary>
public class ChangeSidecar
{
    /// <summary>
    /// The Fleece issue ID that owns this change.
    /// </summary>
    public required string FleeceId { get; init; }

    /// <summary>
    /// Indicates who created the sidecar. Expected values: <c>server</c> or <c>agent</c>.
    /// </summary>
    public required string CreatedBy { get; init; }
}

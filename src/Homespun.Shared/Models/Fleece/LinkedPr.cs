namespace Homespun.Shared.Models.Fleece;

/// <summary>
/// Represents a PR linked to an issue. Returned by
/// <c>GET /api/projects/{projectId}/linked-prs</c> as the value of a
/// <c>Dictionary&lt;issueId, LinkedPr&gt;</c>.
/// </summary>
public class LinkedPr
{
    public int Number { get; set; }
    public string? Url { get; set; }
    public string Status { get; set; } = "";
}

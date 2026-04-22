namespace Homespun.Features.Gitgraph.Snapshots;

/// <summary>
/// Configuration for the Delta-3 SignalR patch-push path. Bound from the
/// <c>TaskGraphSnapshot:PatchPush</c> configuration section.
/// </summary>
public sealed class TaskGraphPatchPushOptions
{
    public const string SectionName = "TaskGraphSnapshot:PatchPush";

    /// <summary>
    /// When <c>true</c>, <c>BroadcastIssueFieldsPatched</c> emits the
    /// dedicated <c>IssueFieldsPatched</c> SignalR event so clients can apply
    /// the patch via <c>queryClient.setQueryData</c> with no HTTP refetch.
    /// When <c>false</c>, the helper falls back to the Delta-2 behaviour and
    /// emits <c>IssuesChanged</c> instead (clients invalidate + refetch).
    /// Snapshot patching on the server runs in both cases.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

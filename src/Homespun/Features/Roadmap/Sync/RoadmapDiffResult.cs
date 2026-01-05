namespace Homespun.Features.Roadmap.Sync;

/// <summary>
/// Result of comparing ROADMAP.local.json with the main branch version.
/// </summary>
public class RoadmapDiffResult
{
    /// <summary>
    /// Whether there are any differences between local and main.
    /// </summary>
    public bool HasChanges { get; init; }

    /// <summary>
    /// Last updated timestamp of the local ROADMAP.local.json.
    /// </summary>
    public DateTime? LocalLastUpdated { get; init; }

    /// <summary>
    /// Last updated timestamp of the main branch ROADMAP.json.
    /// </summary>
    public DateTime? MainLastUpdated { get; init; }

    /// <summary>
    /// IDs of changes that exist in local but not in main.
    /// </summary>
    public List<string> AddedChanges { get; init; } = [];

    /// <summary>
    /// IDs of changes that exist in main but not in local.
    /// </summary>
    public List<string> RemovedChanges { get; init; } = [];

    /// <summary>
    /// IDs of changes that exist in both but have different content.
    /// </summary>
    public List<string> ModifiedChanges { get; init; } = [];

    /// <summary>
    /// Whether the main branch has a ROADMAP.json file.
    /// </summary>
    public bool MainHasRoadmap { get; init; }

    /// <summary>
    /// Whether the local ROADMAP.local.json file exists.
    /// </summary>
    public bool LocalExists { get; init; }

    /// <summary>
    /// Creates a result indicating no changes.
    /// </summary>
    public static RoadmapDiffResult NoChanges(DateTime? localLastUpdated = null, DateTime? mainLastUpdated = null)
    {
        return new RoadmapDiffResult
        {
            HasChanges = false,
            LocalLastUpdated = localLastUpdated,
            MainLastUpdated = mainLastUpdated,
            LocalExists = true,
            MainHasRoadmap = true
        };
    }

    /// <summary>
    /// Creates a result indicating no local file exists.
    /// </summary>
    public static RoadmapDiffResult NoLocalFile()
    {
        return new RoadmapDiffResult
        {
            HasChanges = false,
            LocalExists = false,
            MainHasRoadmap = true
        };
    }
}

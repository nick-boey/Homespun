using System.Text.Json.Serialization;
using Homespun.Features.PullRequests;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Represents the ROADMAP.json file structure containing future planned changes.
/// Uses a flat list with parent references (DAG structure) for dependencies.
/// </summary>
public class Roadmap
{
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("lastUpdated")]
    public DateTime? LastUpdated { get; set; }

    [JsonPropertyName("changes")]
    public List<FutureChange> Changes { get; set; } = [];

    /// <summary>
    /// Gets all changes with their calculated time values based on dependency depth.
    /// Depth is calculated from the longest parent chain.
    /// </summary>
    public List<(FutureChange Change, int Time, int Depth)> GetAllChangesWithTime()
    {
        // Build a lookup for fast parent resolution
        var changeLookup = Changes.ToDictionary(c => c.Id, c => c);
        var depthCache = new Dictionary<string, int>();

        var result = new List<(FutureChange, int, int)>();

        foreach (var change in Changes)
        {
            var depth = CalculateDepth(change, changeLookup, depthCache);
            var time = PullRequestTimeCalculator.CalculateTimeForFutureChange(depth);
            result.Add((change, time, depth));
        }

        return result;
    }

    /// <summary>
    /// Calculates the depth of a change based on its longest parent chain.
    /// A change with no parents has depth 0.
    /// A change with parents has depth = max(parent depths) + 1.
    /// </summary>
    private static int CalculateDepth(
        FutureChange change,
        Dictionary<string, FutureChange> lookup,
        Dictionary<string, int> cache)
    {
        // Check cache first
        if (cache.TryGetValue(change.Id, out var cachedDepth))
        {
            return cachedDepth;
        }

        // No parents = root level = depth 0
        if (change.Parents.Count == 0)
        {
            cache[change.Id] = 0;
            return 0;
        }

        // Calculate max parent depth
        var maxParentDepth = 0;
        foreach (var parentId in change.Parents)
        {
            if (lookup.TryGetValue(parentId, out var parent))
            {
                var parentDepth = CalculateDepth(parent, lookup, cache);
                maxParentDepth = Math.Max(maxParentDepth, parentDepth);
            }
            // If parent not found, treat as if it's at depth -1 (external dependency)
            // This means this change would be at depth 0
        }

        var depth = maxParentDepth + 1;
        cache[change.Id] = depth;
        return depth;
    }
}

using Fleece.Core.Models;

namespace Homespun.Features.Fleece.Services;

/// <inheritdoc cref="IIssueAncestorTraversalService"/>
public class IssueAncestorTraversalService : IIssueAncestorTraversalService
{
    public IReadOnlyCollection<Issue> CollectVisible(
        IReadOnlyList<Issue> issues,
        IReadOnlySet<string> seedIds)
    {
        if (issues.Count == 0 || seedIds.Count == 0)
        {
            return Array.Empty<Issue>();
        }

        var byId = new Dictionary<string, Issue>(issues.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues)
        {
            byId[issue.Id] = issue;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var seed in seedIds)
        {
            if (byId.ContainsKey(seed))
            {
                queue.Enqueue(seed);
            }
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!visited.Add(id))
            {
                continue;
            }

            if (!byId.TryGetValue(id, out var issue))
            {
                continue;
            }

            foreach (var parentRef in issue.ParentIssues)
            {
                if (string.IsNullOrEmpty(parentRef.ParentIssue))
                {
                    continue;
                }
                if (!visited.Contains(parentRef.ParentIssue))
                {
                    queue.Enqueue(parentRef.ParentIssue);
                }
            }
        }

        var result = new List<Issue>(visited.Count);
        foreach (var id in visited)
        {
            if (byId.TryGetValue(id, out var issue))
            {
                result.Add(issue);
            }
        }
        return result;
    }
}

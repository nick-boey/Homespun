using System.Text.Json;
using System.Text.RegularExpressions;
using Fleece.Core.Models;
using Homespun.Server.Features.Commands.Services;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Server.Features.Fleece.Services;

public interface IFleeceIssueDiffService
{
    /// <summary>
    /// Gets issue diffs between working tree and HEAD.
    /// </summary>
    Task<List<IssueDiff>> GetIssueDiffsAsync(string workingDirectory, CancellationToken cancellationToken = default);
}

public class FleeceIssueDiffService : IFleeceIssueDiffService
{
    private readonly ICommandExecutor _commandExecutor;
    private readonly ILogger<FleeceIssueDiffService> _logger;
    private static readonly Regex IssueFileRegex = new(@"\.fleece/issues/([a-zA-Z0-9]+)\.json$", RegexOptions.Compiled);

    public FleeceIssueDiffService(ICommandExecutor commandExecutor, ILogger<FleeceIssueDiffService> logger)
    {
        _commandExecutor = commandExecutor;
        _logger = logger;
    }

    public async Task<List<IssueDiff>> GetIssueDiffsAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        var diffs = new List<IssueDiff>();

        // Get list of changed files in .fleece/issues/
        var diffResult = await _commandExecutor.ExecuteCommandAsync(
            "git diff --name-status HEAD -- .fleece/issues/",
            workingDirectory,
            cancellationToken);

        if (!diffResult.Success || string.IsNullOrEmpty(diffResult.Output))
        {
            return diffs;
        }

        var lines = diffResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2)
                continue;

            var status = parts[0];
            var filePath = parts[1];

            var match = IssueFileRegex.Match(filePath);
            if (!match.Success)
                continue;

            var issueId = match.Groups[1].Value;

            try
            {
                var diff = status switch
                {
                    "A" => await CreateAddedDiff(issueId, filePath, workingDirectory, cancellationToken),
                    "M" => await CreateModifiedDiff(issueId, filePath, workingDirectory, cancellationToken),
                    "D" => await CreateDeletedDiff(issueId, filePath, workingDirectory, cancellationToken),
                    _ => null
                };

                if (diff != null)
                {
                    diffs.Add(diff);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse issue diff for {IssueId}", issueId);
            }
        }

        return diffs;
    }

    private async Task<IssueDiff?> CreateAddedDiff(string issueId, string filePath, string workingDirectory, CancellationToken cancellationToken)
    {
        var modifiedIssue = await ReadIssueFromWorkingTree(filePath, workingDirectory, cancellationToken);
        if (modifiedIssue == null)
            return null;

        return new IssueDiff
        {
            IssueId = issueId,
            ChangeType = IssueChangeType.Created,
            OriginalIssue = null,
            ModifiedIssue = modifiedIssue,
            ChangedFields = []
        };
    }

    private async Task<IssueDiff?> CreateModifiedDiff(string issueId, string filePath, string workingDirectory, CancellationToken cancellationToken)
    {
        var originalIssue = await ReadIssueFromHead(filePath, workingDirectory, cancellationToken);
        var modifiedIssue = await ReadIssueFromWorkingTree(filePath, workingDirectory, cancellationToken);

        if (originalIssue == null || modifiedIssue == null)
            return null;

        var changedFields = GetChangedFields(originalIssue, modifiedIssue);

        return new IssueDiff
        {
            IssueId = issueId,
            ChangeType = IssueChangeType.Updated,
            OriginalIssue = originalIssue,
            ModifiedIssue = modifiedIssue,
            ChangedFields = changedFields
        };
    }

    private async Task<IssueDiff?> CreateDeletedDiff(string issueId, string filePath, string workingDirectory, CancellationToken cancellationToken)
    {
        var originalIssue = await ReadIssueFromHead(filePath, workingDirectory, cancellationToken);
        if (originalIssue == null)
            return null;

        return new IssueDiff
        {
            IssueId = issueId,
            ChangeType = IssueChangeType.Deleted,
            OriginalIssue = originalIssue,
            ModifiedIssue = null,
            ChangedFields = []
        };
    }

    private async Task<Issue?> ReadIssueFromWorkingTree(string filePath, string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await _commandExecutor.ExecuteCommandAsync(
            $"cat {filePath}",
            workingDirectory,
            cancellationToken);

        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Issue>(result.Output);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse issue from working tree: {FilePath}", filePath);
            return null;
        }
    }

    private async Task<Issue?> ReadIssueFromHead(string filePath, string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await _commandExecutor.ExecuteCommandAsync(
            $"git show HEAD:{filePath}",
            workingDirectory,
            cancellationToken);

        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Issue>(result.Output);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse issue from HEAD: {FilePath}", filePath);
            return null;
        }
    }

    private List<string> GetChangedFields(Issue original, Issue modified)
    {
        var changedFields = new List<string>();

        if (original.Title != modified.Title)
            changedFields.Add("Title");

        if (original.Status != modified.Status)
            changedFields.Add("Status");

        if (original.Type != modified.Type)
            changedFields.Add("Type");

        if (original.Description != modified.Description)
            changedFields.Add("Description");

        if (original.Priority != modified.Priority)
            changedFields.Add("Priority");

        if (original.LinkedPR != modified.LinkedPR)
            changedFields.Add("LinkedPR");

        if (!Enumerable.SequenceEqual(original.LinkedIssues.OrderBy(x => x), modified.LinkedIssues.OrderBy(x => x)))
            changedFields.Add("LinkedIssues");

        if (!Enumerable.SequenceEqual(original.Tags.OrderBy(x => x), modified.Tags.OrderBy(x => x)))
            changedFields.Add("Tags");

        if (original.AssignedTo != modified.AssignedTo)
            changedFields.Add("AssignedTo");

        if (original.ExecutionMode != modified.ExecutionMode)
            changedFields.Add("ExecutionMode");

        // Compare parent issues
        var originalParents = original.ParentIssues.OrderBy(p => p.ParentIssue).ThenBy(p => p.SortOrder).ToList();
        var modifiedParents = modified.ParentIssues.OrderBy(p => p.ParentIssue).ThenBy(p => p.SortOrder).ToList();

        if (originalParents.Count != modifiedParents.Count ||
            !originalParents.Zip(modifiedParents, (o, m) => o.ParentIssue == m.ParentIssue && o.SortOrder == m.SortOrder).All(x => x))
        {
            changedFields.Add("ParentIssues");
        }

        return changedFields;
    }
}
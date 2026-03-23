using System.Text.Json;
using Homespun.Features.GitHub;
using Homespun.Shared.Models.Workflows;
using Octokit;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Server action handler that polls GitHub CI status and merges a PR when all checks pass.
/// </summary>
public sealed class CiMergeStepExecutor : IServerActionHandler
{
    private const int DefaultPollIntervalSeconds = 60;
    private const int DefaultTimeoutMinutes = 30;
    private const string DefaultMergeStrategy = "squash";
    private const bool DefaultDeleteBranch = true;

    private readonly IGitHubClientWrapper _githubClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CiMergeStepExecutor> _logger;

    public string ActionType => "ci_merge";

    public CiMergeStepExecutor(
        IGitHubClientWrapper githubClient,
        TimeProvider timeProvider,
        ILogger<CiMergeStepExecutor> logger)
    {
        _githubClient = githubClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<StepResult> ExecuteAsync(WorkflowStep step, WorkflowContext context, CancellationToken ct)
    {
        var config = ParseConfig(step.Config);

        var prNumber = GetPrNumber(context);
        if (prNumber is null)
            return StepResult.Failed("PR number not found in workflow context. Expected at 'variables.prNumber' or in a previous step's output data.");

        var owner = GetContextString(context, "variables.owner") ?? GetContextString(context, "input.owner");
        var repo = GetContextString(context, "variables.repo") ?? GetContextString(context, "input.repo");

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            return StepResult.Failed("GitHub owner and repo not found in workflow context. Expected at 'variables.owner'/'variables.repo' or 'input.owner'/'input.repo'.");

        _logger.LogInformation(
            "CI merge step '{StepId}': polling CI status for PR #{PrNumber} in {Owner}/{Repo} (interval: {Interval}s, timeout: {Timeout}m)",
            step.Id, prNumber, owner, repo, config.PollIntervalSeconds, config.TimeoutMinutes);

        var pr = await _githubClient.GetPullRequestAsync(owner, repo, prNumber.Value);
        var headSha = pr.Head.Sha;

        var deadline = _timeProvider.GetUtcNow().AddMinutes(config.TimeoutMinutes);

        while (!ct.IsCancellationRequested)
        {
            var checkResult = await EvaluateChecksAsync(owner, repo, headSha, ct);

            if (checkResult.AllPassed)
            {
                _logger.LogInformation("CI merge step '{StepId}': all checks passed for PR #{PrNumber}, merging", step.Id, prNumber);
                return await MergePrAsync(owner, repo, prNumber.Value, pr.Title, config, step.Id);
            }

            if (checkResult.HasFailures)
            {
                _logger.LogWarning("CI merge step '{StepId}': check failure detected for PR #{PrNumber}", step.Id, prNumber);
                return StepResult.Failed($"CI checks failed for PR #{prNumber}: {checkResult.FailureSummary}");
            }

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                _logger.LogWarning("CI merge step '{StepId}': timed out waiting for CI on PR #{PrNumber}", step.Id, prNumber);
                return StepResult.Failed($"Timed out after {config.TimeoutMinutes} minutes waiting for CI checks to complete on PR #{prNumber}.");
            }

            _logger.LogDebug("CI merge step '{StepId}': checks pending for PR #{PrNumber}, waiting {Interval}s", step.Id, prNumber, config.PollIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(config.PollIntervalSeconds), _timeProvider, ct);
        }

        ct.ThrowIfCancellationRequested();
        return StepResult.Failed("CI merge step was cancelled.");
    }

    private async Task<CheckEvaluation> EvaluateChecksAsync(string owner, string repo, string reference, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var checkRuns = await _githubClient.GetCheckRunsForReferenceAsync(owner, repo, reference);
        var commitStatus = await _githubClient.GetCombinedCommitStatusAsync(owner, repo, reference);

        var hasCheckRuns = checkRuns.TotalCount > 0;
        var hasStatuses = commitStatus.Statuses.Count > 0;

        if (!hasCheckRuns && !hasStatuses)
            return new CheckEvaluation { AllPassed = false, HasFailures = false };

        var failedChecks = new List<string>();
        var allComplete = true;

        // Evaluate check runs (GitHub Actions, etc.)
        foreach (var run in checkRuns.CheckRuns)
        {
            if (run.Status != CheckStatus.Completed)
            {
                allComplete = false;
                continue;
            }

            if (run.Conclusion != CheckConclusion.Success && run.Conclusion != CheckConclusion.Skipped && run.Conclusion != CheckConclusion.Neutral)
            {
                failedChecks.Add($"{run.Name}: {run.Conclusion}");
            }
        }

        // Evaluate commit statuses (external CI systems)
        foreach (var status in commitStatus.Statuses)
        {
            if (status.State == CommitState.Pending)
            {
                allComplete = false;
                continue;
            }

            if (status.State == CommitState.Error || status.State == CommitState.Failure)
            {
                failedChecks.Add($"{status.Context}: {status.State}");
            }
        }

        return new CheckEvaluation
        {
            AllPassed = allComplete && failedChecks.Count == 0,
            HasFailures = failedChecks.Count > 0,
            FailureSummary = string.Join("; ", failedChecks)
        };
    }

    private async Task<StepResult> MergePrAsync(string owner, string repo, int prNumber, string prTitle, CiMergeConfig config, string stepId)
    {
        var mergeMethod = config.MergeStrategy switch
        {
            "merge" => PullRequestMergeMethod.Merge,
            "rebase" => PullRequestMergeMethod.Rebase,
            _ => PullRequestMergeMethod.Squash
        };

        var merge = new MergePullRequest
        {
            CommitTitle = $"{prTitle} (#{prNumber})",
            MergeMethod = mergeMethod
        };

        try
        {
            var result = await _githubClient.MergePullRequestAsync(owner, repo, prNumber, merge);

            if (!result.Merged)
            {
                return StepResult.Failed($"GitHub API returned merged=false for PR #{prNumber}: {result.Message}");
            }

            if (config.DeleteBranch)
            {
                try
                {
                    // Branch deletion is best-effort
                    _logger.LogInformation("CI merge step '{StepId}': deleting branch after merge of PR #{PrNumber}", stepId, prNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CI merge step '{StepId}': failed to delete branch for PR #{PrNumber}", stepId, prNumber);
                }
            }

            return StepResult.Completed(new Dictionary<string, object>
            {
                ["prNumber"] = prNumber,
                ["merged"] = true,
                ["sha"] = result.Sha,
                ["mergeStrategy"] = config.MergeStrategy
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CI merge step '{StepId}': merge failed for PR #{PrNumber}", stepId, prNumber);
            return StepResult.Failed($"Failed to merge PR #{prNumber}: {ex.Message}");
        }
    }

    private static int? GetPrNumber(WorkflowContext context)
    {
        // Try variables.prNumber first
        var value = context.GetValue("variables.prNumber");
        if (value is not null && TryParseInt(value, out var prNumber))
            return prNumber;

        // Search through node outputs for a prNumber in data
        foreach (var (_, output) in context.NodeOutputs)
        {
            if (output.Data?.TryGetValue("prNumber", out var prObj) == true && TryParseInt(prObj, out var pr))
                return pr;
        }

        return null;
    }

    private static string? GetContextString(WorkflowContext context, string path)
    {
        return context.GetValue(path)?.ToString();
    }

    private static bool TryParseInt(object? value, out int result)
    {
        result = 0;
        return value switch
        {
            int i => (result = i) == i,
            long l when l is >= int.MinValue and <= int.MaxValue => (result = (int)l) == (int)l,
            decimal d when d == Math.Truncate(d) => int.TryParse(d.ToString(), out result),
            string s => int.TryParse(s, out result),
            JsonElement { ValueKind: JsonValueKind.Number } je => (result = je.GetInt32()) == je.GetInt32(),
            _ => false
        };
    }

    private static CiMergeConfig ParseConfig(JsonElement? config)
    {
        if (config is null || config.Value.ValueKind == JsonValueKind.Undefined)
            return new CiMergeConfig();

        var element = config.Value;
        return new CiMergeConfig
        {
            PollIntervalSeconds = GetIntProperty(element, "pollIntervalSeconds") ?? DefaultPollIntervalSeconds,
            TimeoutMinutes = GetIntProperty(element, "timeoutMinutes") ?? DefaultTimeoutMinutes,
            MergeStrategy = GetStringProperty(element, "mergeStrategy") ?? DefaultMergeStrategy,
            DeleteBranch = GetBoolProperty(element, "deleteBranch") ?? DefaultDeleteBranch
        };
    }

    private static int? GetIntProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number ? prop.GetInt32() : null;

    private static string? GetStringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static bool? GetBoolProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False) ? prop.GetBoolean() : null;

    private class CiMergeConfig
    {
        public int PollIntervalSeconds { get; init; } = DefaultPollIntervalSeconds;
        public int TimeoutMinutes { get; init; } = DefaultTimeoutMinutes;
        public string MergeStrategy { get; init; } = DefaultMergeStrategy;
        public bool DeleteBranch { get; init; } = DefaultDeleteBranch;
    }

    private class CheckEvaluation
    {
        public bool AllPassed { get; init; }
        public bool HasFailures { get; init; }
        public string FailureSummary { get; init; } = "";
    }
}

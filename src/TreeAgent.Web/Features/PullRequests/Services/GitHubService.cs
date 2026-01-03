using Microsoft.EntityFrameworkCore;
using Octokit;
using TreeAgent.Web.Data;
using TreeAgent.Web.Data.Entities;
using TreeAgent.Web.Services;

namespace TreeAgent.Web.Features.PullRequests.Services;

public class GitHubService : IGitHubService
{
    private readonly TreeAgentDbContext _db;
    private readonly ICommandRunner _commandRunner;
    private readonly IConfiguration _configuration;
    private readonly IGitHubClientWrapper _githubClient;

    public GitHubService(
        TreeAgentDbContext db,
        ICommandRunner commandRunner,
        IConfiguration configuration,
        IGitHubClientWrapper githubClient)
    {
        _db = db;
        _commandRunner = commandRunner;
        _configuration = configuration;
        _githubClient = githubClient;
    }

    private string? GetGitHubToken()
    {
        return _configuration["GITHUB_TOKEN"] ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    public async Task<bool> IsConfiguredAsync(string projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return false;

        return !string.IsNullOrEmpty(project.GitHubOwner) &&
               !string.IsNullOrEmpty(project.GitHubRepo) &&
               !string.IsNullOrEmpty(GetGitHubToken());
    }

    public async Task<List<GitHubPullRequest>> GetOpenPullRequestsAsync(string projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            return [];
        }

        ConfigureClient();

        try
        {
            var request = new PullRequestRequest
            {
                State = ItemStateFilter.Open
            };

            var prs = await _githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);
            return prs.Select(MapPullRequest).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<List<GitHubPullRequest>> GetClosedPullRequestsAsync(string projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            return [];
        }

        ConfigureClient();

        try
        {
            var request = new PullRequestRequest
            {
                State = ItemStateFilter.Closed
            };

            var prs = await _githubClient.GetPullRequestsAsync(project.GitHubOwner, project.GitHubRepo, request);
            return prs.Select(MapPullRequest).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<GitHubPullRequest?> GetPullRequestAsync(string projectId, int prNumber)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            return null;
        }

        ConfigureClient();

        try
        {
            var pr = await _githubClient.GetPullRequestAsync(project.GitHubOwner, project.GitHubRepo, prNumber);
            return MapPullRequest(pr);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<GitHubPullRequest?> CreatePullRequestAsync(string projectId, string featureId)
    {
        var feature = await _db.Features
            .Include(f => f.Project)
            .FirstOrDefaultAsync(f => f.Id == featureId);

        if (feature == null) return null;

        var project = feature.Project;
        if (string.IsNullOrEmpty(project.GitHubOwner) ||
            string.IsNullOrEmpty(project.GitHubRepo) ||
            string.IsNullOrEmpty(feature.BranchName))
        {
            return null;
        }

        // First push the branch
        var pushed = await PushBranchAsync(projectId, feature.BranchName);
        if (!pushed) return null;

        ConfigureClient();

        try
        {
            var newPr = new NewPullRequest(
                feature.Title,
                feature.BranchName,
                project.DefaultBranch)
            {
                Body = feature.Description
            };

            var pr = await _githubClient.CreatePullRequestAsync(project.GitHubOwner, project.GitHubRepo, newPr);

            // Update feature with PR number
            feature.GitHubPRNumber = pr.Number;
            feature.Status = FeatureStatus.ReadyForReview;
            feature.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return MapPullRequest(pr);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> PushBranchAsync(string projectId, string branchName)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return false;

        var workingDir = project.LocalPath;

        // Push the branch to origin
        var result = await _commandRunner.RunAsync("git", $"push -u origin \"{branchName}\"", workingDir);
        return result.Success;
    }

    public async Task<SyncResult> SyncPullRequestsAsync(string projectId)
    {
        var result = new SyncResult();

        var project = await _db.Projects.FindAsync(projectId);
        if (project == null || string.IsNullOrEmpty(project.GitHubOwner) || string.IsNullOrEmpty(project.GitHubRepo))
        {
            result.Errors.Add("Project not found or GitHub not configured");
            return result;
        }

        // Fetch all PRs
        var openPrs = await GetOpenPullRequestsAsync(projectId);
        var closedPrs = await GetClosedPullRequestsAsync(projectId);
        var allPrs = openPrs.Concat(closedPrs).ToList();

        // Get existing features with PR numbers
        var existingFeatures = await _db.Features
            .Where(f => f.ProjectId == projectId && f.GitHubPRNumber != null)
            .ToListAsync();

        var existingPrNumbers = existingFeatures
            .Where(f => f.GitHubPRNumber.HasValue)
            .Select(f => f.GitHubPRNumber!.Value)
            .ToHashSet();

        foreach (var pr in allPrs)
        {
            try
            {
                if (existingPrNumbers.Contains(pr.Number))
                {
                    // Update existing feature
                    var feature = existingFeatures.First(f => f.GitHubPRNumber == pr.Number);
                    var newStatus = MapPrStateToFeatureStatus(pr);

                    if (feature.Status != newStatus)
                    {
                        feature.Status = newStatus;
                        feature.UpdatedAt = DateTime.UtcNow;
                        result.Updated++;
                    }
                }
                else
                {
                    // Import as new feature
                    var feature = new Feature
                    {
                        ProjectId = projectId,
                        Title = pr.Title,
                        Description = pr.Body,
                        BranchName = pr.BranchName,
                        GitHubPRNumber = pr.Number,
                        Status = MapPrStateToFeatureStatus(pr),
                        CreatedAt = pr.CreatedAt
                    };

                    _db.Features.Add(feature);
                    result.Imported++;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"PR #{pr.Number}: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<bool> LinkPullRequestAsync(string featureId, int prNumber)
    {
        var feature = await _db.Features.FindAsync(featureId);
        if (feature == null) return false;

        feature.GitHubPRNumber = prNumber;
        feature.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return true;
    }

    private void ConfigureClient()
    {
        var token = GetGitHubToken();
        if (_githubClient is GitHubClientWrapper wrapper)
        {
            wrapper.SetToken(token);
        }
    }

    private static GitHubPullRequest MapPullRequest(PullRequest pr)
    {
        return new GitHubPullRequest
        {
            Number = pr.Number,
            Title = pr.Title,
            Body = pr.Body,
            State = pr.State.StringValue,
            Merged = pr.Merged,
            BranchName = pr.Head.Ref,
            HtmlUrl = pr.HtmlUrl,
            CreatedAt = pr.CreatedAt.UtcDateTime,
            MergedAt = pr.MergedAt?.UtcDateTime,
            ClosedAt = pr.ClosedAt?.UtcDateTime
        };
    }

    private static FeatureStatus MapPrStateToFeatureStatus(GitHubPullRequest pr)
    {
        if (pr.Merged)
        {
            return FeatureStatus.Merged;
        }

        if (pr.State == "closed")
        {
            return FeatureStatus.Cancelled;
        }

        return FeatureStatus.ReadyForReview;
    }
}

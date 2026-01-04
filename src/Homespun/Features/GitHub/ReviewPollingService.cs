using Homespun.Features.OpenCode.Hubs;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Homespun.Features.GitHub;

/// <summary>
/// Background service that polls GitHub for PR reviews.
/// </summary>
public class ReviewPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly ReviewPollingOptions _options;
    private readonly ILogger<ReviewPollingService> _logger;

    public ReviewPollingService(
        IServiceScopeFactory scopeFactory,
        IHubContext<AgentHub> hubContext,
        IOptions<ReviewPollingOptions> options,
        ILogger<ReviewPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Review polling service started with {IntervalSeconds}s interval", _options.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollReviewsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during review polling");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Review polling service stopped");
    }

    private async Task PollReviewsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dataStore = scope.ServiceProvider.GetRequiredService<IDataStore>();
        var gitHubService = scope.ServiceProvider.GetRequiredService<IGitHubService>();

        // Get all projects
        var projects = dataStore.Projects;

        foreach (var project in projects)
        {
            try
            {
                await PollProjectReviewsAsync(project, dataStore, gitHubService, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling reviews for project {ProjectId}", project.Id);
            }
        }
    }

    private async Task PollProjectReviewsAsync(
        Project project,
        IDataStore dataStore,
        IGitHubService gitHubService,
        CancellationToken ct)
    {
        // Only poll PRs that are in progress or awaiting review
        var pullRequests = dataStore.GetPullRequestsByProject(project.Id)
            .Where(pr => pr.GitHubPRNumber.HasValue &&
                         (pr.Status == OpenPullRequestStatus.InDevelopment ||
                          pr.Status == OpenPullRequestStatus.ReadyForReview ||
                          pr.Status == OpenPullRequestStatus.HasReviewComments))
            .ToList();

        foreach (var pr in pullRequests)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var reviews = await gitHubService.GetPullRequestReviewsAsync(project.Id, pr.GitHubPRNumber!.Value);
                
                // Check if there are new reviews that need attention
                var previousStatus = pr.Status;
                var needsUpdate = false;

                if (reviews.NeedsAction && pr.Status != OpenPullRequestStatus.HasReviewComments)
                {
                    pr.Status = OpenPullRequestStatus.HasReviewComments;
                    needsUpdate = true;
                }
                else if (reviews.IsApproved && pr.Status == OpenPullRequestStatus.ReadyForReview)
                {
                    // PR is approved - could auto-merge or notify
                    _logger.LogInformation("PR #{PrNumber} is approved", pr.GitHubPRNumber);
                }

                if (needsUpdate)
                {
                    pr.UpdatedAt = DateTime.UtcNow;
                    await dataStore.UpdatePullRequestAsync(pr);
                    
                    // Broadcast status change
                    await _hubContext.Clients.All.SendAsync(
                        "PullRequestReviewsUpdated",
                        project.Id,
                        pr.Id,
                        pr.GitHubPRNumber,
                        reviews,
                        ct);

                    _logger.LogInformation(
                        "PR #{PrNumber} status changed from {PreviousStatus} to {NewStatus} due to reviews",
                        pr.GitHubPRNumber, previousStatus, pr.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling reviews for PR #{PrNumber}", pr.GitHubPRNumber);
            }
        }
    }
}

/// <summary>
/// Configuration options for review polling.
/// </summary>
public class ReviewPollingOptions
{
    public const string SectionName = "ReviewPolling";

    /// <summary>
    /// Interval between polls in seconds. Default: 60.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Whether to enable automatic response to review comments via agent.
    /// </summary>
    public bool AutoRespondEnabled { get; set; } = false;
}

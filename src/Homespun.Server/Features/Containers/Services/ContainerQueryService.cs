using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Containers;
using Homespun.Shared.Models.Projects;

namespace Homespun.Features.Containers.Services;

/// <summary>
/// Service for querying and managing worker containers with enriched project/issue information.
/// </summary>
public class ContainerQueryService : IContainerQueryService
{
    private const string IssueContainerPrefix = "homespun-issue-";

    private readonly IAgentExecutionService _agentExecutionService;
    private readonly IProjectService _projectService;
    private readonly IFleeceService _fleeceService;
    private readonly IClaudeSessionService _sessionService;
    private readonly ILogger<ContainerQueryService> _logger;

    public ContainerQueryService(
        IAgentExecutionService agentExecutionService,
        IProjectService projectService,
        IFleeceService fleeceService,
        IClaudeSessionService sessionService,
        ILogger<ContainerQueryService> logger)
    {
        _agentExecutionService = agentExecutionService;
        _projectService = projectService;
        _fleeceService = fleeceService;
        _sessionService = sessionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkerContainerDto>> GetAllContainersAsync(CancellationToken cancellationToken = default)
    {
        var containers = await _agentExecutionService.ListContainersAsync(cancellationToken);
        var result = new List<WorkerContainerDto>();

        foreach (var container in containers)
        {
            var dto = await EnrichContainerAsync(container, cancellationToken);
            result.Add(dto);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> StopContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping container {ContainerId}", containerId);
        return await _agentExecutionService.StopContainerByIdAsync(containerId, cancellationToken);
    }

    /// <summary>
    /// Tries to parse a container name in the format "homespun-issue-{projectId}-{issueId}".
    /// The issue ID is expected to be the last segment (6 characters), and the project ID
    /// is everything between the prefix and the issue ID.
    /// </summary>
    /// <param name="containerName">The container name to parse.</param>
    /// <param name="projectId">The extracted project ID, or null if parsing failed.</param>
    /// <param name="issueId">The extracted issue ID, or null if parsing failed.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParseIssueContainerName(string containerName, out string? projectId, out string? issueId)
    {
        projectId = null;
        issueId = null;

        if (string.IsNullOrEmpty(containerName))
            return false;

        if (!containerName.StartsWith(IssueContainerPrefix, StringComparison.Ordinal))
            return false;

        // Remove the prefix to get "{projectId}-{issueId}"
        var remainder = containerName[IssueContainerPrefix.Length..];

        // Find the last hyphen - the issue ID is the last segment (typically 6 chars)
        var lastHyphenIndex = remainder.LastIndexOf('-');
        if (lastHyphenIndex <= 0 || lastHyphenIndex >= remainder.Length - 1)
            return false;

        projectId = remainder[..lastHyphenIndex];
        issueId = remainder[(lastHyphenIndex + 1)..];

        return !string.IsNullOrEmpty(projectId) && !string.IsNullOrEmpty(issueId);
    }

    /// <summary>
    /// Enriches container info with project and issue details.
    /// Uses ProjectId from ContainerInfo if available, otherwise parses from container name.
    /// </summary>
    private async Task<WorkerContainerDto> EnrichContainerAsync(
        ContainerInfo container,
        CancellationToken cancellationToken)
    {
        string? projectId = null;
        string? projectName = null;
        string? issueTitle = null;
        string? issueId = container.IssueId;

        // Strategy 1: Use ProjectId directly from ContainerInfo if available
        if (!string.IsNullOrEmpty(container.ProjectId))
        {
            projectId = container.ProjectId;
        }
        // Strategy 2: Parse from container name as fallback
        else if (TryParseIssueContainerName(container.ContainerName, out var parsedProjectId, out var parsedIssueId))
        {
            projectId = parsedProjectId;
            // Also use parsed issue ID if not already set
            if (string.IsNullOrEmpty(issueId))
            {
                issueId = parsedIssueId;
            }
        }

        // Look up project details by ID
        if (!string.IsNullOrEmpty(projectId))
        {
            var project = await _projectService.GetByIdAsync(projectId);
            if (project != null)
            {
                projectName = project.Name;

                // If we have an issue ID, look up the issue title
                if (!string.IsNullOrEmpty(issueId))
                {
                    try
                    {
                        var issue = await _fleeceService.GetIssueAsync(project.LocalPath ?? "", issueId, cancellationToken);
                        issueTitle = issue?.Title;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not fetch issue {IssueId} for container {ContainerId}",
                            issueId, container.ContainerId);
                    }
                }
            }
            else
            {
                // Project not found - reset projectId to null so UI shows "Unknown Project"
                projectId = null;
            }
        }

        return new WorkerContainerDto
        {
            ContainerId = container.ContainerId,
            ContainerName = container.ContainerName,
            WorkingDirectory = container.WorkingDirectory,
            ProjectId = projectId,
            ProjectName = projectName,
            IssueId = issueId,
            IssueTitle = issueTitle,
            ActiveSessionId = GetHomespunSessionId(issueId),
            SessionStatus = container.State?.SessionStatus ?? Homespun.Shared.Models.Sessions.ClaudeSessionStatus.Stopped,
            LastActivityAt = container.State?.LastActivityAt,
            CreatedAt = container.CreatedAt,
            HasPendingQuestion = container.State?.HasPendingQuestion ?? false,
            HasPendingPlanApproval = container.State?.HasPendingPlanApproval ?? false
        };
    }

    /// <summary>
    /// Gets the Homespun session ID for an entity (issue) ID.
    /// </summary>
    private string? GetHomespunSessionId(string? issueId)
    {
        if (string.IsNullOrEmpty(issueId))
            return null;

        var session = _sessionService.GetSessionByEntityId(issueId);
        return session?.Id;
    }
}

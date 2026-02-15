using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Containers;

namespace Homespun.Features.Containers.Services;

/// <summary>
/// Service for querying and managing worker containers with enriched project/issue information.
/// </summary>
public class ContainerQueryService : IContainerQueryService
{
    private readonly IAgentExecutionService _agentExecutionService;
    private readonly IProjectService _projectService;
    private readonly IFleeceService _fleeceService;
    private readonly ILogger<ContainerQueryService> _logger;

    public ContainerQueryService(
        IAgentExecutionService agentExecutionService,
        IProjectService projectService,
        IFleeceService fleeceService,
        ILogger<ContainerQueryService> logger)
    {
        _agentExecutionService = agentExecutionService;
        _projectService = projectService;
        _fleeceService = fleeceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkerContainerDto>> GetAllContainersAsync(CancellationToken cancellationToken = default)
    {
        var containers = await _agentExecutionService.ListContainersAsync(cancellationToken);
        var result = new List<WorkerContainerDto>();
        var projects = await _projectService.GetAllAsync();

        foreach (var container in containers)
        {
            var dto = await EnrichContainerAsync(container, projects, cancellationToken);
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
    /// Enriches container info with project and issue details.
    /// </summary>
    private async Task<WorkerContainerDto> EnrichContainerAsync(
        ContainerInfo container,
        List<Project> projects,
        CancellationToken cancellationToken)
    {
        string? projectId = null;
        string? projectName = null;
        string? issueTitle = null;

        // Try to find the project based on working directory
        var project = projects.FirstOrDefault(p => 
            container.WorkingDirectory.StartsWith(p.LocalPath ?? "", StringComparison.OrdinalIgnoreCase));

        if (project != null)
        {
            projectId = project.Id;
            projectName = project.Name;

            // If we have an issue ID, look up the issue title
            if (!string.IsNullOrEmpty(container.IssueId))
            {
                try
                {
                    var issue = await _fleeceService.GetIssueAsync(project.LocalPath ?? "", container.IssueId, cancellationToken);
                    issueTitle = issue?.Title;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not fetch issue {IssueId} for container {ContainerId}",
                        container.IssueId, container.ContainerId);
                }
            }
        }

        return new WorkerContainerDto
        {
            ContainerId = container.ContainerId,
            ContainerName = container.ContainerName,
            WorkingDirectory = container.WorkingDirectory,
            ProjectId = projectId,
            ProjectName = projectName,
            IssueId = container.IssueId,
            IssueTitle = issueTitle,
            ActiveSessionId = container.State?.ActiveSessionId,
            SessionStatus = container.State?.SessionStatus ?? Homespun.Shared.Models.Sessions.ClaudeSessionStatus.Stopped,
            LastActivityAt = container.State?.LastActivityAt,
            CreatedAt = container.CreatedAt,
            HasPendingQuestion = container.State?.HasPendingQuestion ?? false,
            HasPendingPlanApproval = container.State?.HasPendingPlanApproval ?? false
        };
    }
}

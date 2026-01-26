using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;

namespace Homespun.Features.Navigation;

/// <summary>
/// Service for managing breadcrumb navigation.
/// Resolves project and issue names from their IDs to build readable breadcrumb chains.
/// </summary>
public class BreadcrumbService : IBreadcrumbService
{
    private readonly IProjectService _projectService;
    private readonly IFleeceService _fleeceService;
    private readonly List<BreadcrumbItem> _breadcrumbs = [];

    /// <summary>
    /// Mapping of page names to their base URLs for standalone pages.
    /// </summary>
    private static readonly Dictionary<string, string> StandalonePageUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Projects", "/projects" },
        { "Settings", "/settings" },
        { "Agents", "/agents" }
    };

    public BreadcrumbService(IProjectService projectService, IFleeceService fleeceService)
    {
        _projectService = projectService;
        _fleeceService = fleeceService;
    }

    /// <inheritdoc />
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs => _breadcrumbs.AsReadOnly();

    /// <inheritdoc />
    public event Action? OnBreadcrumbsChanged;

    /// <inheritdoc />
    public async Task SetContextAsync(BreadcrumbContext context)
    {
        _breadcrumbs.Clear();

        // If we have a project ID, this is a project-related page
        if (!string.IsNullOrEmpty(context.ProjectId))
        {
            await BuildProjectBreadcrumbsAsync(context);
        }
        // If we only have a page name, this is a standalone page
        else if (!string.IsNullOrEmpty(context.PageName))
        {
            BuildStandalonePageBreadcrumbs(context.PageName);
        }

        OnBreadcrumbsChanged?.Invoke();
    }

    /// <inheritdoc />
    public void ClearContext()
    {
        _breadcrumbs.Clear();
        OnBreadcrumbsChanged?.Invoke();
    }

    private async Task BuildProjectBreadcrumbsAsync(BreadcrumbContext context)
    {
        // Always start with Projects
        _breadcrumbs.Add(new BreadcrumbItem("Projects", "/projects"));

        // Get project name
        var project = await _projectService.GetByIdAsync(context.ProjectId!);
        var projectName = project?.Name ?? context.ProjectId!;
        var projectUrl = $"/projects/{context.ProjectId}";

        // If we have an issue, the project is a link
        if (!string.IsNullOrEmpty(context.IssueId))
        {
            _breadcrumbs.Add(new BreadcrumbItem(projectName, projectUrl));
            await BuildIssueBreadcrumbsAsync(context, project?.LocalPath);
        }
        // If we have a page name (Edit, Create, etc.), project is a link
        else if (!string.IsNullOrEmpty(context.PageName))
        {
            _breadcrumbs.Add(new BreadcrumbItem(projectName, projectUrl));
            _breadcrumbs.Add(new BreadcrumbItem(context.PageName, null)); // Last item has no URL
        }
        // Project detail page - project is the last item
        else
        {
            _breadcrumbs.Add(new BreadcrumbItem(projectName, projectUrl));
        }
    }

    private async Task BuildIssueBreadcrumbsAsync(BreadcrumbContext context, string? projectPath)
    {
        // Add Issues label
        _breadcrumbs.Add(new BreadcrumbItem("Issues", null));

        // Get issue title
        string issueTitle = context.IssueId!;
        if (!string.IsNullOrEmpty(projectPath))
        {
            var issue = await _fleeceService.GetIssueAsync(projectPath, context.IssueId!);
            if (issue != null)
            {
                issueTitle = issue.Title;
            }
        }

        // If we have a page name, issue is a link
        if (!string.IsNullOrEmpty(context.PageName))
        {
            var issueUrl = $"/projects/{context.ProjectId}/issues/{context.IssueId}";
            _breadcrumbs.Add(new BreadcrumbItem(issueTitle, issueUrl));
            _breadcrumbs.Add(new BreadcrumbItem(context.PageName, null));
        }
        // Issue detail page - issue is the last item
        else
        {
            _breadcrumbs.Add(new BreadcrumbItem(issueTitle, null));
        }
    }

    private void BuildStandalonePageBreadcrumbs(string pageName)
    {
        var url = StandalonePageUrls.TryGetValue(pageName, out var pageUrl)
            ? pageUrl
            : $"/{pageName.ToLowerInvariant()}";

        _breadcrumbs.Add(new BreadcrumbItem(pageName, url));
    }
}

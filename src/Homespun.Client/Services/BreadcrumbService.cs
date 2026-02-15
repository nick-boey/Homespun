namespace Homespun.Client.Services;

public record BreadcrumbItem(string Title, string? Url);

public record BreadcrumbContext
{
    public string? ProjectId { get; init; }
    public string? IssueId { get; init; }
    public string? SessionId { get; init; }
    public string? PageName { get; init; }
}

public interface IBreadcrumbService
{
    IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; }
    event Action? OnBreadcrumbsChanged;
    Task SetContextAsync(BreadcrumbContext context);
    void ClearContext();
}

public class BreadcrumbService(HttpProjectApiService projectApi, HttpIssueApiService issueApi) : IBreadcrumbService
{
    private readonly List<BreadcrumbItem> _breadcrumbs = [];

    private static readonly Dictionary<string, string> StandalonePageUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Projects", "/projects" },
        { "Settings", "/settings" },
        { "Agents", "/agents" },
        { "Sessions", "/sessions" },
        { "Containers", "/containers" }
    };

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs => _breadcrumbs.AsReadOnly();
    public event Action? OnBreadcrumbsChanged;

    public async Task SetContextAsync(BreadcrumbContext context)
    {
        _breadcrumbs.Clear();

        if (!string.IsNullOrEmpty(context.ProjectId))
        {
            await BuildProjectBreadcrumbsAsync(context);
        }
        else if (!string.IsNullOrEmpty(context.SessionId))
        {
            BuildSessionBreadcrumbs(context);
        }
        else if (!string.IsNullOrEmpty(context.PageName))
        {
            BuildStandalonePageBreadcrumbs(context.PageName);
        }

        OnBreadcrumbsChanged?.Invoke();
    }

    public void ClearContext()
    {
        _breadcrumbs.Clear();
        OnBreadcrumbsChanged?.Invoke();
    }

    private async Task BuildProjectBreadcrumbsAsync(BreadcrumbContext context)
    {
        _breadcrumbs.Add(new BreadcrumbItem("Projects", "/projects"));

        var project = await projectApi.GetProjectAsync(context.ProjectId!);
        var projectName = project?.Name ?? context.ProjectId!;
        var projectUrl = $"/projects/{context.ProjectId}";

        if (!string.IsNullOrEmpty(context.IssueId))
        {
            _breadcrumbs.Add(new BreadcrumbItem(projectName, projectUrl));
            await BuildIssueBreadcrumbsAsync(context);
        }
        else if (!string.IsNullOrEmpty(context.PageName))
        {
            _breadcrumbs.Add(new BreadcrumbItem(projectName, projectUrl));
            _breadcrumbs.Add(new BreadcrumbItem(context.PageName, null));
        }
        else
        {
            _breadcrumbs.Add(new BreadcrumbItem(projectName, projectUrl));
        }
    }

    private async Task BuildIssueBreadcrumbsAsync(BreadcrumbContext context)
    {
        _breadcrumbs.Add(new BreadcrumbItem("Issues", null));

        var issueTitle = context.IssueId!;
        var issue = await issueApi.GetIssueAsync(context.IssueId!, context.ProjectId!);
        if (issue != null)
        {
            issueTitle = issue.Title;
        }

        if (!string.IsNullOrEmpty(context.PageName))
        {
            var issueUrl = $"/projects/{context.ProjectId}/issues/{context.IssueId}";
            _breadcrumbs.Add(new BreadcrumbItem(issueTitle, issueUrl));
            _breadcrumbs.Add(new BreadcrumbItem(context.PageName, null));
        }
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

    private void BuildSessionBreadcrumbs(BreadcrumbContext context)
    {
        _breadcrumbs.Add(new BreadcrumbItem("Sessions", "/sessions"));

        var displayId = context.SessionId!.Length > 8
            ? context.SessionId![..8] + "..."
            : context.SessionId!;

        if (!string.IsNullOrEmpty(context.PageName))
        {
            _breadcrumbs.Add(new BreadcrumbItem(displayId, $"/session/{context.SessionId}"));
            _breadcrumbs.Add(new BreadcrumbItem(context.PageName, null));
        }
        else
        {
            _breadcrumbs.Add(new BreadcrumbItem(displayId, null));
        }
    }
}

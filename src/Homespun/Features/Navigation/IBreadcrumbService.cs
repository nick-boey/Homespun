namespace Homespun.Features.Navigation;

/// <summary>
/// Service for managing breadcrumb navigation.
/// Pages set their context, and the service resolves entity names and builds breadcrumb chains.
/// </summary>
public interface IBreadcrumbService
{
    /// <summary>
    /// The current breadcrumb chain.
    /// </summary>
    IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; }

    /// <summary>
    /// Event fired when breadcrumbs change.
    /// </summary>
    event Action? OnBreadcrumbsChanged;

    /// <summary>
    /// Sets the breadcrumb context. The service will resolve entity names and build the breadcrumb chain.
    /// </summary>
    /// <param name="context">The context containing IDs and page information.</param>
    Task SetContextAsync(BreadcrumbContext context);

    /// <summary>
    /// Clears all breadcrumbs.
    /// </summary>
    void ClearContext();
}

/// <summary>
/// A single breadcrumb item in the navigation chain.
/// </summary>
/// <param name="Title">The display title for this breadcrumb.</param>
/// <param name="Url">The navigation URL, or null if this is the current/last item.</param>
public record BreadcrumbItem(string Title, string? Url);

/// <summary>
/// Context information for building breadcrumbs.
/// Pages provide the IDs they have, and the service resolves the names.
/// </summary>
public record BreadcrumbContext
{
    /// <summary>
    /// The project ID, if on a project-related page.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// The issue ID, if on an issue-related page.
    /// </summary>
    public string? IssueId { get; init; }

    /// <summary>
    /// The session ID, if on a session-related page.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// The page name (e.g., "Edit", "Create", "Settings").
    /// For standalone pages, this is the only breadcrumb shown.
    /// For nested pages, this appears as the last item.
    /// </summary>
    public string? PageName { get; init; }
}

namespace Homespun.Client.Features.Navigation.Models;

/// <summary>
/// Determines how navigation content is rendered.
/// </summary>
public enum NavRenderMode
{
    /// <summary>
    /// Renders using BbSidebar components (for use within BbSidebarProvider context).
    /// </summary>
    Sidebar,

    /// <summary>
    /// Renders using semantic HTML with Tailwind styling (for use within BbResponsiveNavContent).
    /// </summary>
    Responsive
}

using System.Text.RegularExpressions;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// Middleware that rewrites requests from OpenCode pages based on the Referer header.
///
/// OpenCode's client uses window.location.origin for API calls, stripping the /agent/{port}/ path.
/// This middleware detects requests from OpenCode pages (via Referer header) and prepends
/// the correct /agent/{port}/ prefix so YARP can proxy them correctly.
///
/// This is a "Referer-based routing" approach - if the request comes from an OpenCode page
/// (Referer contains /agent/{port}/), we route it to OpenCode. This is future-proof and
/// handles any OpenCode endpoints without needing to maintain a whitelist.
/// </summary>
public partial class AgentRefererRewriteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AgentRefererRewriteMiddleware> _logger;

    public AgentRefererRewriteMiddleware(RequestDelegate next, ILogger<AgentRefererRewriteMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip if already has /agent/{port}/ prefix
        if (path.StartsWith("/agent/"))
        {
            await _next(context);
            return;
        }

        // Skip Blazor/framework paths - these are never OpenCode requests
        if (IsBlazorPath(path))
        {
            await _next(context);
            return;
        }

        // Check if request is from an OpenCode page (Referer contains /agent/{port}/)
        var referer = context.Request.Headers.Referer.FirstOrDefault();
        if (string.IsNullOrEmpty(referer))
        {
            await _next(context);
            return;
        }

        var match = AgentPortPattern().Match(referer);
        if (!match.Success)
        {
            await _next(context);
            return;
        }

        // Rewrite ALL requests from OpenCode pages
        var port = match.Groups[1].Value;
        var newPath = $"/agent/{port}{path}";

        _logger.LogInformation("Rewriting OpenCode request {OriginalPath} to {NewPath} based on Referer",
            path, newPath);

        context.Request.Path = newPath;
        await _next(context);
    }

    /// <summary>
    /// Paths that are definitely Blazor/ASP.NET and should never be rewritten.
    /// </summary>
    private static bool IsBlazorPath(string path)
    {
        return path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);
    }

    // Match /agent/{port}/ in Referer URL
    [GeneratedRegex(@"/agent/(\d+)/")]
    private static partial Regex AgentPortPattern();
}

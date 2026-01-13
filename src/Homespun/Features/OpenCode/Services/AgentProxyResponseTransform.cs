using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Homespun.Features.OpenCode.Services;

/// <summary>
/// YARP transform provider that rewrites absolute paths in HTML responses from OpenCode.
/// This ensures assets load correctly when accessed through the /agent/{port}/ proxy path.
/// Also handles request transforms to ensure the ?url= parameter is an absolute URL.
/// </summary>
public partial class AgentProxyResponseTransform : ITransformProvider
{
    private readonly ILogger<AgentProxyResponseTransform> _logger;

    public AgentProxyResponseTransform(ILogger<AgentProxyResponseTransform> logger)
    {
        _logger = logger;
    }

    public void ValidateRoute(TransformRouteValidationContext context)
    {
        // No validation needed
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
        // No validation needed
    }

    public void Apply(TransformBuilderContext context)
    {
        // Only apply to agent routes (e.g., "agent-4096")
        if (context.Route.RouteId?.StartsWith("agent-") != true)
        {
            return;
        }

        var portStr = context.Route.RouteId.Substring(6);
        if (!int.TryParse(portStr, out var port))
        {
            return;
        }

        var basePath = $"/agent/{port}";
        _logger.LogDebug("Applying transforms for route {RouteId} with basePath {BasePath}",
            context.Route.RouteId, basePath);

        // Add request transform to handle ?url= parameter
        // OpenCode's normalizeServerUrl expects an absolute URL (http://...) or a hostname.
        // If ?url= is a relative path like /agent/4096, it becomes http:///agent/4096 (malformed).
        // This transform marks the request for redirect if needed.
        context.AddRequestTransform(transformContext =>
        {
            var request = transformContext.HttpContext.Request;
            var urlParam = request.Query["url"].FirstOrDefault();

            // Only redirect for HTML page requests (not API calls or assets)
            // Check if this looks like the initial page load (has session in path, no API prefix)
            var path = request.Path.Value ?? "";
            var isPageRequest = path.Contains("/session/") && !path.Contains("/api/");

            // If ?url= is missing or is a relative path, mark for redirect
            if (isPageRequest && (string.IsNullOrEmpty(urlParam) || urlParam.StartsWith("/")))
            {
                var scheme = request.Scheme;
                var host = request.Host.ToString();
                var absoluteBaseUrl = $"{scheme}://{host}{basePath}";

                // Build the redirect URL with the absolute ?url= parameter
                var queryString = HttpUtility.ParseQueryString(request.QueryString.Value ?? "");
                queryString["url"] = absoluteBaseUrl;

                var redirectUrl = $"{request.Path}?{queryString}";

                _logger.LogDebug("Will redirect to add absolute ?url= parameter: {RedirectUrl}", redirectUrl);

                // Store redirect URL in HttpContext.Items for the response transform to use
                transformContext.HttpContext.Items["AgentProxyRedirectUrl"] = redirectUrl;
            }

            return ValueTask.CompletedTask;
        });

        context.AddResponseTransform(async transformContext =>
        {
            var httpContext = transformContext.HttpContext;

            // Check if we need to redirect instead of proxying the response
            if (httpContext.Items.TryGetValue("AgentProxyRedirectUrl", out var redirectUrlObj) &&
                redirectUrlObj is string redirectUrl)
            {
                // Send redirect response instead of the proxied response
                httpContext.Response.StatusCode = 302;
                httpContext.Response.Headers.Location = redirectUrl;
                transformContext.SuppressResponseBody = true;
                return;
            }

            var response = transformContext.ProxyResponse;
            if (response == null)
            {
                return;
            }

            // Only transform HTML responses
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType != "text/html")
            {
                return;
            }

            // Read the original response body
            var originalBody = await response.Content.ReadAsStringAsync();

            // Rewrite absolute paths to include the proxy base path
            var transformedBody = RewriteAbsolutePaths(originalBody, basePath);

            // Write the transformed response
            var bytes = Encoding.UTF8.GetBytes(transformedBody);
            transformContext.HttpContext.Response.ContentLength = bytes.Length;
            transformContext.HttpContext.Response.ContentType = "text/html; charset=utf-8";
            await transformContext.HttpContext.Response.Body.WriteAsync(bytes);

            // Suppress the default body copy since we've written our own
            transformContext.SuppressResponseBody = true;
        });
    }

    /// <summary>
    /// Rewrites absolute paths in HTML content to include the proxy base path.
    /// </summary>
    private static string RewriteAbsolutePaths(string html, string basePath)
    {
        // Rewrite src="/..." attributes (scripts, images, etc.)
        html = SrcHrefPattern().Replace(html, $"$1=\"{basePath}$2");

        // Rewrite url(/...) in inline styles
        html = UrlPattern().Replace(html, $"url($1{basePath}$2");

        // Rewrite action="/..." in forms
        html = ActionPattern().Replace(html, $"action=\"{basePath}$1");

        return html;
    }

    // Regex patterns for rewriting URLs
    // Match src="/..." or href="/..." (but not src="//..." which is protocol-relative)
    [GeneratedRegex(@"(src|href)=""(/(?!/)[^""]*)")]
    private static partial Regex SrcHrefPattern();

    // Match url(/...) or url('/...') or url("/...")
    [GeneratedRegex(@"url\((['""]?)(/(?!/)[^)""']*)")]
    private static partial Regex UrlPattern();

    // Match action="/..."
    [GeneratedRegex(@"action=""(/(?!/)[^""]*)")]
    private static partial Regex ActionPattern();
}

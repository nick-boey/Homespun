using Aspire.Hosting.ApplicationModel;

namespace Homespun.AppHost.Tests;

/// <summary>
/// Aspire 13 keeps mutating resource annotations on background pipeline tasks
/// after BuildAsync returns, so a plain LINQ traversal over
/// <see cref="IResource.Annotations"/> can throw
/// "Collection was modified; enumeration operation may not execute."
/// This helper retries the snapshot until it observes a stable read.
/// </summary>
internal static class AnnotationSnapshot
{
    public static IReadOnlyList<object> Of(IResource resource)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                return resource.Annotations.Cast<object>().ToArray();
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(25);
            }
        }
        return resource.Annotations.Cast<object>().ToArray();
    }
}

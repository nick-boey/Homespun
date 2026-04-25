namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// DI registration for <see cref="IPerSessionEventStream"/>.
///
/// <para>
/// The service MUST be registered as a <b>singleton</b>: it holds a
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/> of
/// per-session readers, and a transient registration would give every consumer its
/// own isolated dictionary — two consumers would not see each other's
/// <c>StartAsync</c> calls, and <c>SubscribeTurnAsync</c> would throw
/// "no reader running" on the second consumer.
/// </para>
///
/// <para>
/// We use a <i>named</i> <see cref="HttpClient"/> rather than
/// <c>AddHttpClient&lt;TI, TImpl&gt;</c> (which is implicitly Transient) so
/// <see cref="IHttpClientFactory"/> can manage handler lifetime while the service
/// itself stays a clean singleton. The client is configured with
/// <see cref="Timeout.InfiniteTimeSpan"/> because the SSE connection is intentionally
/// open for the session's entire duration.
/// </para>
/// </summary>
public static class PerSessionEventStreamServiceCollectionExtensions
{
    public static IServiceCollection AddPerSessionEventStream(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(PerSessionEventStream), c =>
        {
            c.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<IPerSessionEventStream>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new PerSessionEventStream(
                sp.GetRequiredService<ISessionEventIngestor>(),
                factory.CreateClient(nameof(PerSessionEventStream)),
                sp.GetRequiredService<ILogger<PerSessionEventStream>>());
        });
        return services;
    }
}

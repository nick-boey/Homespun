namespace Homespun.Features.Observability;

/// <summary>
/// Destinations the OTLP proxy fans accepted requests out to. Bound from the
/// <c>OtlpFanout</c> configuration section.
///
/// The Aspire dashboard leg is NOT represented here — it resolves at runtime
/// from the <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> / <c>OTEL_EXPORTER_OTLP_HEADERS</c>
/// env vars that Aspire injects. Absent env = no Aspire leg (the prod case).
/// </summary>
public sealed class OtlpFanoutOptions
{
    public const string SectionName = "OtlpFanout";

    /// <summary>
    /// Base URL of the Seq OTLP ingest endpoint (e.g.
    /// <c>http://seq:5341/ingest/otlp</c>). Empty or null skips the Seq leg.
    /// </summary>
    public string? SeqBaseUrl { get; set; }

    /// <summary>
    /// Optional <c>X-Seq-ApiKey</c> attached to every Seq-bound request when set.
    /// </summary>
    public string? SeqApiKey { get; set; }
}

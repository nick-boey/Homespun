namespace Homespun.Features.Observability;

/// <summary>
/// Redaction rules applied by <see cref="OtlpScrubber"/> before an OTLP request
/// is fanned out to downstream sinks. Bound from the <c>OtlpScrubber</c>
/// configuration section when present; otherwise defaults apply.
/// </summary>
public sealed class OtlpScrubberOptions
{
    public const string SectionName = "OtlpScrubber";

    /// <summary>
    /// Attribute keys whose name contains any of these substrings (case-insensitive)
    /// have their string value replaced with <c>[REDACTED]</c>. Other value kinds
    /// on the same attribute are cleared. Defaults cover common secret-bearing
    /// header and credential attribute names; append further substrings to tighten.
    /// </summary>
    public List<string> SecretSubstrings { get; set; } = new()
    {
        "token",
        "secret",
        "password",
        "authorization",
        "credential",
    };
}

using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Homespun.Features.Observability;

/// <summary>
/// Mutates an incoming OTLP request in place before it is dispatched to
/// downstream sinks. Enforces the <c>SessionEventLog:ContentPreviewChars</c>
/// gate and redacts attribute values whose key names a secret.
/// </summary>
public interface IOtlpScrubber
{
    void Scrub(ExportLogsServiceRequest req);
    void Scrub(ExportTraceServiceRequest req);
}

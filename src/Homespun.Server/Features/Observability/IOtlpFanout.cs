using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Homespun.Features.Observability;

/// <summary>
/// Dispatches a scrubbed OTLP request to every configured downstream sink in
/// parallel. Implementations SHALL swallow upstream failures: the controller
/// always returns 202 to the caller regardless of sink availability.
/// </summary>
public interface IOtlpFanout
{
    Task LogsAsync(ExportLogsServiceRequest req, CancellationToken ct);
    Task TracesAsync(ExportTraceServiceRequest req, CancellationToken ct);
}

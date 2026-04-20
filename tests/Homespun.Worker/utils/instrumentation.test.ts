import { describe, it, expect, afterEach, vi } from 'vitest';

/**
 * These tests exercise `src/instrumentation.ts` as a side-effectful module.
 *
 * The module calls `sdk.start()` synchronously on import and installs
 * SIGTERM/SIGINT handlers; we isolate each test with `vi.resetModules()` so
 * repeated imports re-run the bootstrap with a fresh env under
 * `vi.stubEnv(...)`.
 */
describe('worker OTel bootstrap (src/instrumentation.ts)', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('does not throw when OTLP_PROXY_URL is unset', async () => {
    vi.stubEnv('OTLP_PROXY_URL', '');
    // If the SDK bootstrap blew up on missing URL, this import would throw.
    await expect(import('#src/instrumentation.js')).resolves.toBeTruthy();
  });

  it('creates exporters pointed at `${OTLP_PROXY_URL}/logs` and /traces when set', async () => {
    vi.stubEnv('OTLP_PROXY_URL', 'http://host.docker.internal:5101/api/otlp/v1');

    const traceExporterModule = await import(
      '@opentelemetry/exporter-trace-otlp-proto'
    );
    const logExporterModule = await import(
      '@opentelemetry/exporter-logs-otlp-proto'
    );

    const traceSpy = vi.spyOn(traceExporterModule, 'OTLPTraceExporter');
    const logSpy = vi.spyOn(logExporterModule, 'OTLPLogExporter');

    vi.resetModules();
    await import('#src/instrumentation.js');

    expect(traceSpy).toHaveBeenCalled();
    expect(logSpy).toHaveBeenCalled();

    const traceArgs = traceSpy.mock.calls[0]?.[0] as { url?: string } | undefined;
    const logArgs = logSpy.mock.calls[0]?.[0] as { url?: string } | undefined;
    expect(traceArgs?.url).toBe(
      'http://host.docker.internal:5101/api/otlp/v1/traces',
    );
    expect(logArgs?.url).toBe(
      'http://host.docker.internal:5101/api/otlp/v1/logs',
    );
  });
});

/**
 * OpenTelemetry SDK bootstrap for the Homespun Worker.
 *
 * This module MUST be imported as the first non-shebang statement of
 * `src/index.ts` so `@opentelemetry/auto-instrumentations-node` can patch
 * Hono, http, undici, etc. before any other module binds the un-patched
 * versions.
 *
 * Exports to Seq via the server's OTLP proxy at `${OTLP_PROXY_URL}/logs`
 * and `${OTLP_PROXY_URL}/traces`. When `OTLP_PROXY_URL` is unset, the SDK
 * still boots — exporters log their delivery failure and are otherwise
 * silent. This lets the worker run under `tsx` without a live server.
 *
 * Resource attributes pull per-session context from the environment:
 * `HOMESPUN_SESSION_ID`, `HOMESPUN_ISSUE_ID`, `HOMESPUN_PROJECT_NAME`,
 * `HOMESPUN_AGENT_MODE`. These are injected by the server's
 * `DockerAgentExecutionService` when spawning worker containers.
 *
 * Do NOT enable `@opentelemetry/instrumentation-fs` — worker fs churn is
 * high (clone operations, tasks.md reads) and the resulting span volume
 * would drown useful signal.
 */
import { NodeSDK } from '@opentelemetry/sdk-node';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-http';
import {
  BatchLogRecordProcessor,
  ConsoleLogRecordExporter,
  SimpleLogRecordProcessor,
  type LogRecordProcessor,
} from '@opentelemetry/sdk-logs';
import { resourceFromAttributes } from '@opentelemetry/resources';
import {
  ATTR_SERVICE_NAME,
  ATTR_SERVICE_VERSION,
} from '@opentelemetry/semantic-conventions';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';

const OTLP_PROXY_URL = process.env.OTLP_PROXY_URL?.trim() ?? '';
const SERVICE_NAME = process.env.OTEL_SERVICE_NAME || 'homespun.worker';
const SERVICE_VERSION = process.env.OTEL_SERVICE_VERSION || '1.0.0';
const DEPLOYMENT_ENV =
  process.env.DEPLOYMENT_ENVIRONMENT ||
  process.env.NODE_ENV ||
  'development';

// Build per-container identity attributes. Omit unset keys so Seq does not
// surface empty-string noise.
function buildResourceAttributes(): Record<string, string> {
  const attrs: Record<string, string> = {
    [ATTR_SERVICE_NAME]: SERVICE_NAME,
    [ATTR_SERVICE_VERSION]: SERVICE_VERSION,
    'deployment.environment': DEPLOYMENT_ENV,
  };

  const sessionId = process.env.HOMESPUN_SESSION_ID;
  if (sessionId) attrs['homespun.session.id'] = sessionId;

  const issueId = process.env.HOMESPUN_ISSUE_ID;
  if (issueId) attrs['homespun.issue.id'] = issueId;

  const projectName = process.env.HOMESPUN_PROJECT_NAME;
  if (projectName) attrs['homespun.project.name'] = projectName;

  const agentMode = process.env.HOMESPUN_AGENT_MODE;
  if (agentMode) attrs['homespun.agent.mode'] = agentMode;

  return attrs;
}

const resource = resourceFromAttributes(buildResourceAttributes());

// Exporters target the server's OTLP HTTP proxy. The proxy takes binary
// protobuf on the wire — `@opentelemetry/exporter-*-otlp-http` defaults to
// protobuf.
const tracesUrl = OTLP_PROXY_URL ? `${OTLP_PROXY_URL}/traces` : undefined;
const logsUrl = OTLP_PROXY_URL ? `${OTLP_PROXY_URL}/logs` : undefined;

const traceExporter = new OTLPTraceExporter(
  tracesUrl ? { url: tracesUrl } : {},
);
const logExporter = new OTLPLogExporter(logsUrl ? { url: logsUrl } : {});

// `scheduledDelayMillis: 1000` — a sibling container killed with SIGTERM
// followed by a 3s grace period has at least one batch in-flight before the
// kernel reaps the process. See `DockerAgentExecutionService.StopContainerAsync`.
const logRecordProcessors: LogRecordProcessor[] = [
  new BatchLogRecordProcessor(logExporter, { scheduledDelayMillis: 1000 }),
];

// Dev-only console exporter for local diagnostics — attaches a second log
// processor that writes every record through `console.dir` so operators can
// verify OTel emission without spinning up the server proxy.
if (process.env.DEBUG_OTEL_CONSOLE === 'true') {
  logRecordProcessors.push(
    new SimpleLogRecordProcessor(new ConsoleLogRecordExporter()),
  );
}

export const sdk = new NodeSDK({
  resource,
  traceExporter,
  logRecordProcessors,
  instrumentations: [
    getNodeAutoInstrumentations({
      // fs churn in the worker (clone reads, tasks.md polls) produces log
      // volume that would drown useful signal. Keep disabled.
      '@opentelemetry/instrumentation-fs': { enabled: false },
      // dns/net auto-instrumentation is equally chatty and adds no value
      // above what http already reports.
      '@opentelemetry/instrumentation-net': { enabled: false },
      '@opentelemetry/instrumentation-dns': { enabled: false },
    }),
  ],
});

sdk.start();

// Flush batched logs/spans on graceful shutdown so the final record from a
// container about to die is not lost. Sibling containers receive SIGTERM via
// `docker stop --time 3` (see DockerAgentExecutionService.StopContainerAsync).
async function shutdownTelemetry(): Promise<void> {
  try {
    await sdk.shutdown();
  } catch {
    // swallow — shutdown is best-effort during process exit.
  }
}

process.on('SIGTERM', () => {
  void shutdownTelemetry().finally(() => process.exit(0));
});
process.on('SIGINT', () => {
  void shutdownTelemetry().finally(() => process.exit(0));
});

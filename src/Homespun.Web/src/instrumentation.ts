/**
 * OpenTelemetry bootstrap for the React client.
 *
 * MUST be imported first in `main.tsx` — before any other module that caches
 * the global fetch / XMLHttpRequest. `FetchInstrumentation` and
 * `XMLHttpRequestInstrumentation` patch the globals on registration; any
 * module that captured a reference beforehand will bypass propagation.
 *
 * Sinks: traces + logs are POSTed to the server OTLP proxy at
 * `/api/otlp/v1/traces` and `/api/otlp/v1/logs`. The proxy is part of the
 * server-otlp-proxy change. Client metrics are out of scope — the browser has
 * no useful metric signal the server needs today.
 *
 * StackContextManager (the web SDK default) is used instead of
 * `@opentelemetry/context-zone`. This saves ~80 KB by avoiding `zone.js`.
 * Trade-off: context is not propagated automatically across microtasks /
 * Promise chains that cross React state boundaries. Code that needs to
 * correlate an async continuation with the originating span should wrap the
 * continuation with `context.with(trace.setSpan(context.active(), span), ...)`.
 */

import { context, trace, propagation } from '@opentelemetry/api'
import { logs } from '@opentelemetry/api-logs'
import {
  CompositePropagator,
  W3CBaggagePropagator,
  W3CTraceContextPropagator,
} from '@opentelemetry/core'
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-proto'
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto'
import { registerInstrumentations } from '@opentelemetry/instrumentation'
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch'
import { XMLHttpRequestInstrumentation } from '@opentelemetry/instrumentation-xml-http-request'
import { resourceFromAttributes } from '@opentelemetry/resources'
import { BatchLogRecordProcessor, LoggerProvider } from '@opentelemetry/sdk-logs'
import { BatchSpanProcessor, WebTracerProvider } from '@opentelemetry/sdk-trace-web'
import {
  ATTR_SERVICE_NAME,
  ATTR_SERVICE_VERSION,
} from '@opentelemetry/semantic-conventions'

const TRACES_URL = '/api/otlp/v1/traces'
const LOGS_URL = '/api/otlp/v1/logs'

// Regex used to exclude the exporter's own POSTs from being traced — without
// this we would emit a span for every export batch and quickly loop on
// export-of-export spans.
const IGNORE_OTLP_URLS = [/\/api\/otlp\/v1\//]

const resource = resourceFromAttributes({
  [ATTR_SERVICE_NAME]: 'homespun.web',
  [ATTR_SERVICE_VERSION]: import.meta.env.VITE_APP_VERSION ?? 'dev',
  'deployment.environment': import.meta.env.MODE ?? 'development',
})

// -------- Tracer provider --------

const traceExporter = new OTLPTraceExporter({ url: TRACES_URL })
const tracerProvider = new WebTracerProvider({
  resource,
  spanProcessors: [new BatchSpanProcessor(traceExporter)],
})

tracerProvider.register({
  propagator: new CompositePropagator({
    propagators: [new W3CTraceContextPropagator(), new W3CBaggagePropagator()],
  }),
})

// -------- Logger provider --------

const logExporter = new OTLPLogExporter({ url: LOGS_URL })
const loggerProvider = new LoggerProvider({
  resource,
  processors: [new BatchLogRecordProcessor(logExporter)],
})

logs.setGlobalLoggerProvider(loggerProvider)

// -------- Instrumentations --------

registerInstrumentations({
  instrumentations: [
    new FetchInstrumentation({
      ignoreUrls: IGNORE_OTLP_URLS,
    }),
    new XMLHttpRequestInstrumentation({
      ignoreUrls: IGNORE_OTLP_URLS,
    }),
  ],
  tracerProvider,
  loggerProvider,
})

// -------- Flush on page hide --------

if (typeof window !== 'undefined') {
  window.addEventListener('pagehide', () => {
    // Best-effort — the page may be suspended before these resolve. The
    // browser will keep keepalive fetches alive for a few seconds which is
    // usually enough for the OTLP batch to land.
    void tracerProvider.forceFlush().catch(() => {})
    void loggerProvider.forceFlush().catch(() => {})
  })
}

// Re-export the instrumented API surface so call sites can `import { tracer,
// logger } from '@/instrumentation'` rather than juggling per-module names.
export const tracer = trace.getTracer('homespun.web')
export const logger = logs.getLogger('homespun.web')

// Re-export context/trace so consumers can opt-in to manual propagation
// without importing `@opentelemetry/api` directly in dozens of places.
export { context, trace, propagation }
export { tracerProvider, loggerProvider }

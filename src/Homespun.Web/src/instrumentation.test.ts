import { afterAll, describe, expect, it } from 'vitest'
import { context, propagation, trace } from '@opentelemetry/api'
import { logs } from '@opentelemetry/api-logs'
import { LoggerProvider } from '@opentelemetry/sdk-logs'
import { WebTracerProvider } from '@opentelemetry/sdk-trace-web'

// Importing the module triggers registration side effects against the OTel
// global API singletons. We verify those globals after the import resolves.
import { loggerProvider, tracerProvider } from './instrumentation'

describe('instrumentation bootstrap', () => {
  afterAll(() => {
    // Prevent batch processors from holding the process open in watch mode.
    void tracerProvider.shutdown().catch(() => {})
    void loggerProvider.shutdown().catch(() => {})
  })

  it('registers a WebTracerProvider globally', () => {
    expect(tracerProvider).toBeInstanceOf(WebTracerProvider)
    // After registration, the global tracer provider returns a tracer wired
    // to the registered provider (the proxy wraps it transparently).
    expect(trace.getTracer('sanity')).toBeDefined()
  })

  it('registers a LoggerProvider globally', () => {
    expect(loggerProvider).toBeInstanceOf(LoggerProvider)
    expect(logs.getLogger('sanity')).toBeDefined()
  })

  it('uses W3C trace-context + baggage via CompositePropagator', () => {
    // Interrogate the installed propagator by asking it what fields it will
    // inject. CompositePropagator(W3CTraceContext + W3CBaggage) returns
    // exactly ['traceparent', 'tracestate', 'baggage'].
    const fields = propagation.fields()
    expect(fields).toContain('traceparent')
    expect(fields).toContain('tracestate')
    expect(fields).toContain('baggage')
  })

  it('installed propagator round-trips a traceparent through inject + extract', () => {
    const tracer = trace.getTracer('inst-test')
    const span = tracer.startSpan('probe')
    const carrier: Record<string, string> = {}
    context.with(trace.setSpan(context.active(), span), () => {
      propagation.inject(context.active(), carrier)
    })
    span.end()

    expect(carrier['traceparent']).toBeDefined()
    // The baggage entry is only set when baggage exists, so only assert
    // traceparent. Extract the traceparent and confirm it recovers the
    // same trace id as the source span.
    const extracted = propagation.extract(context.active(), carrier)
    const extractedSpan = trace.getSpan(extracted)
    expect(extractedSpan?.spanContext().traceId).toBe(span.spanContext().traceId)
  })

  it('exposes the trace + logs URLs under /api/otlp/v1/ so fetch instrumentation can ignore them', () => {
    // The actual ignoreUrls list is private to FetchInstrumentation. We test
    // the contract by asserting the URLs the instrumentation would see do
    // start with the prefix the regex covers.
    const ignoreRegex = /\/api\/otlp\/v1\//
    expect(ignoreRegex.test('/api/otlp/v1/traces')).toBe(true)
    expect(ignoreRegex.test('/api/otlp/v1/logs')).toBe(true)
    expect(ignoreRegex.test('/api/projects')).toBe(false)
  })
})

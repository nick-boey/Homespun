import { beforeAll, describe, expect, it } from 'vitest';
import { Hono } from 'hono';
import {
  SpanKind,
  context,
  propagation,
  trace,
} from '@opentelemetry/api';
import { W3CTraceContextPropagator } from '@opentelemetry/core';
import {
  BasicTracerProvider,
  InMemorySpanExporter,
  SimpleSpanProcessor,
  type ReadableSpan,
} from '@opentelemetry/sdk-trace-base';
import { AsyncHooksContextManager } from '@opentelemetry/context-async-hooks';
import { traceparentMiddleware } from '#src/utils/traceparent-middleware.js';

// Tests depend on a real TracerProvider + propagator being registered so
// `tracer.startSpan()` returns a recording span and the propagator extracts
// W3C traceparent headers. In production these are configured by
// `src/instrumentation.ts` via `@opentelemetry/sdk-node`.
const exporter = new InMemorySpanExporter();
let provider: BasicTracerProvider;

beforeAll(() => {
  propagation.setGlobalPropagator(new W3CTraceContextPropagator());
  const ctxManager = new AsyncHooksContextManager();
  ctxManager.enable();
  context.setGlobalContextManager(ctxManager);
  provider = new BasicTracerProvider({
    spanProcessors: [new SimpleSpanProcessor(exporter)],
  });
  trace.setGlobalTracerProvider(provider);
});

function finishedSpans(): ReadableSpan[] {
  return exporter.getFinishedSpans();
}

describe('traceparentMiddleware', () => {
  it('runs downstream handlers under the extracted trace context', async () => {
    exporter.reset();
    const traceparent =
      '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01';
    let observedTraceId: string | null = null;

    const app = new Hono();
    app.use('*', traceparentMiddleware);
    app.get('/echo', (c) => {
      observedTraceId =
        trace.getSpan(context.active())?.spanContext().traceId ?? null;
      return c.text('ok');
    });

    const res = await app.request('http://localhost/echo', {
      headers: { traceparent },
    });

    expect(res.status).toBe(200);
    expect(observedTraceId).toBe('4bf92f3577b34da6a3ce929d0e0e4736');

    const spans = finishedSpans();
    expect(spans).toHaveLength(1);
    expect(spans[0].kind).toBe(SpanKind.SERVER);
    expect(spans[0].parentSpanContext?.spanId).toBe('00f067aa0ba902b7');
    expect(spans[0].attributes['http.request.method']).toBe('GET');
    expect(spans[0].attributes['url.path']).toBe('/echo');
    expect(spans[0].attributes['http.response.status_code']).toBe(200);
  });

  it('starts a fresh trace when no traceparent header is present', async () => {
    exporter.reset();
    let observedTraceId: string | null = null;

    const app = new Hono();
    app.use('*', traceparentMiddleware);
    app.get('/no-parent', (c) => {
      observedTraceId =
        trace.getSpan(context.active())?.spanContext().traceId ?? null;
      return c.text('ok');
    });

    const res = await app.request('http://localhost/no-parent');

    expect(res.status).toBe(200);
    expect(observedTraceId).toMatch(/^[0-9a-f]{32}$/);
    expect(observedTraceId).not.toBe('00000000000000000000000000000000');

    const spans = finishedSpans();
    expect(spans).toHaveLength(1);
    expect(spans[0].parentSpanContext).toBeUndefined();
  });
});

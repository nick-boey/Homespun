import type { Context, Next } from 'hono';
import {
  SpanKind,
  SpanStatusCode,
  context,
  propagation,
  trace,
} from '@opentelemetry/api';

/**
 * Hono middleware that reinstates W3C trace propagation for inbound HTTP
 * requests.
 *
 * `@opentelemetry/instrumentation-http` does not reliably create a SERVER
 * span for requests handled by `@hono/node-server` (observed on 0.215.0 /
 * hono 4.7 — incoming spans never emit and `traceparent` is never extracted
 * so every worker-side span becomes its own root trace). This middleware
 * restores propagation explicitly:
 *
 *   1. Extract W3C `traceparent` / `tracestate` from the Fetch-API `Headers`
 *      on the raw request via the global propagator.
 *   2. Start a server-kind span parented to the extracted context and make
 *      it current for the downstream handler chain.
 *   3. Tag the span with method / path / status and set ERROR status on
 *      5xx responses or thrown exceptions.
 *
 * Mount it FIRST so every downstream route runs under the extracted trace.
 */
const tracer = trace.getTracer('homespun.worker.http');

const headersGetter = {
  get(carrier: Headers, key: string): string | undefined {
    return carrier.get(key) ?? undefined;
  },
  keys(carrier: Headers): string[] {
    return Array.from(carrier.keys());
  },
};

export async function traceparentMiddleware(
  c: Context,
  next: Next,
): Promise<void> {
  const extracted = propagation.extract(
    context.active(),
    c.req.raw.headers,
    headersGetter,
  );

  const method = c.req.method;
  const pathname = new URL(c.req.url).pathname;

  const span = tracer.startSpan(
    `${method} ${pathname}`,
    {
      kind: SpanKind.SERVER,
      attributes: {
        'http.request.method': method,
        'url.path': pathname,
        'url.scheme': c.req.raw.url.startsWith('https') ? 'https' : 'http',
      },
    },
    extracted,
  );

  const ctxWithSpan = trace.setSpan(extracted, span);

  try {
    await context.with(ctxWithSpan, next);
    const status = c.res.status;
    span.setAttribute('http.response.status_code', status);
    if (status >= 500) {
      span.setStatus({ code: SpanStatusCode.ERROR });
    }
  } catch (err) {
    const e = err as Error;
    span.recordException(e);
    span.setStatus({ code: SpanStatusCode.ERROR, message: e.message });
    throw err;
  } finally {
    span.end();
  }
}

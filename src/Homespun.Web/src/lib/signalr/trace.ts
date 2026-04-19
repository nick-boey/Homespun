/**
 * SignalR trace-context propagation helpers.
 *
 * SignalR's WebSocket transport does not give clients a way to attach per-
 * invocation headers, so we propagate W3C trace-context out-of-band. Two
 * directions, two mechanisms:
 *
 *  - **Client → Server**: the client wraps each invoke through
 *    {@link traceInvoke}, which starts a client span and prepends the
 *    encoded `traceparent` string as the first wire argument. A server-side
 *    `TraceparentHubFilter` extracts arg0, rebuilds the parent context, and
 *    starts a server span on our `Homespun.Signalr` ActivitySource.
 *  - **Server → Client**: {@link SessionEventEnvelope} carries an optional
 *    `traceparent` field populated from the server's `Activity.Current`.
 *    Clients run their reducer inside {@link withExtractedContext} so the
 *    resulting `homespun.envelope.rx` span parents to the server's ingest.
 */

import {
  context,
  defaultTextMapGetter,
  defaultTextMapSetter,
  propagation,
  SpanKind,
  SpanStatusCode,
  trace,
  type Span,
} from '@opentelemetry/api'

const TRACER_NAME = 'homespun.web.signalr'

const HEADER_TRACEPARENT = 'traceparent'
const HEADER_TRACESTATE = 'tracestate'

/**
 * Captures the currently-active OTel context and returns the W3C
 * `traceparent` string for it. Returns `undefined` when no active trace
 * context exists — callers should then omit the first arg entirely (though
 * `traceInvoke` always passes at least an empty string so the server can
 * assume a consistent argument shape).
 */
export function injectTraceparent(): string | undefined {
  const carrier: Record<string, string> = {}
  propagation.inject(context.active(), carrier, defaultTextMapSetter)
  return carrier[HEADER_TRACEPARENT]
}

/**
 * Minimal shape needed from an envelope-like payload to extract trace
 * context. Accepts either the DTO with `traceparent`/`tracestate` fields
 * (current) or a generic carrier.
 */
export interface TraceCarrier {
  traceparent?: string | null
  tracestate?: string | null
}

/**
 * Runs `fn` under the trace context extracted from `envelope.traceparent`.
 * When no traceparent is present, runs `fn` under the currently-active
 * context — i.e. this is a no-op degradation path, not an error.
 */
export function withExtractedContext<T>(envelope: TraceCarrier | null | undefined, fn: () => T): T {
  const traceparent = envelope?.traceparent ?? undefined
  if (!traceparent) {
    return fn()
  }

  const carrier: Record<string, string> = {
    [HEADER_TRACEPARENT]: traceparent,
  }
  if (envelope?.tracestate) {
    carrier[HEADER_TRACESTATE] = envelope.tracestate
  }

  const extracted = propagation.extract(context.active(), carrier, defaultTextMapGetter)
  return context.with(extracted, fn)
}

/**
 * Generic invoke signature used by both `@microsoft/signalr` and our
 * typed-method wrappers. `...args: unknown[]` so we can prepend the
 * traceparent without fighting the variadic generic of `invoke<T>`.
 */
export type HubInvoke = <T = unknown>(methodName: string, ...args: unknown[]) => Promise<T>

/**
 * Wraps a SignalR invoke-like function so every call:
 *  1. Starts a client span `signalr.invoke.<method>` with kind `CLIENT`.
 *  2. Injects the active context into a `traceparent` string.
 *  3. Prepends that string as the first wire argument.
 *  4. Closes the span on completion, recording any exception.
 *
 * The caller passes the logical method name (`JoinSession`) and the
 * arguments the server method expects after the traceparent. The wire
 * invocation order is `[traceparent, ...args]`.
 */
export async function traceInvoke<T>(
  invoke: HubInvoke,
  methodName: string,
  ...args: unknown[]
): Promise<T> {
  const tracer = trace.getTracer(TRACER_NAME)
  return await tracer.startActiveSpan(
    `signalr.invoke.${methodName}`,
    { kind: SpanKind.CLIENT },
    async (span: Span) => {
      try {
        // injectTraceparent() pulls from `context.active()`, which
        // `startActiveSpan` has already set to include this span — so the
        // emitted traceparent references the span we just created.
        const traceparent = injectTraceparent() ?? ''
        const result = await invoke<T>(methodName, traceparent, ...args)
        span.setStatus({ code: SpanStatusCode.OK })
        return result
      } catch (err) {
        const error = err instanceof Error ? err : new Error(String(err))
        span.recordException(error)
        span.setStatus({ code: SpanStatusCode.ERROR, message: error.message })
        throw err
      } finally {
        span.end()
      }
    }
  )
}

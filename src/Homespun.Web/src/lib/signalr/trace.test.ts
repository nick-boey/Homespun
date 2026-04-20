import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { context, propagation, ROOT_CONTEXT, trace, type Tracer } from '@opentelemetry/api'
import { InMemorySpanExporter, SimpleSpanProcessor } from '@opentelemetry/sdk-trace-base'
import { StackContextManager, WebTracerProvider } from '@opentelemetry/sdk-trace-web'
import {
  CompositePropagator,
  W3CBaggagePropagator,
  W3CTraceContextPropagator,
} from '@opentelemetry/core'

import {
  injectTraceparent,
  traceInvoke,
  withExtractedContext,
  type HubInvoke,
  type TraceCarrier,
} from './trace'

describe('signalr/trace helpers', () => {
  let exporter: InMemorySpanExporter
  let provider: WebTracerProvider
  let contextManager: StackContextManager
  let tracer: Tracer

  beforeEach(() => {
    exporter = new InMemorySpanExporter()
    provider = new WebTracerProvider({
      spanProcessors: [new SimpleSpanProcessor(exporter)],
    })
    contextManager = new StackContextManager()
    contextManager.enable()
    context.setGlobalContextManager(contextManager)
    trace.setGlobalTracerProvider(provider)
    propagation.setGlobalPropagator(
      new CompositePropagator({
        propagators: [new W3CTraceContextPropagator(), new W3CBaggagePropagator()],
      })
    )
    tracer = trace.getTracer('signalr-trace-test')
  })

  afterEach(async () => {
    await provider.shutdown()
    exporter.reset()
    contextManager.disable()
    context.disable()
    trace.disable()
    propagation.disable()
    // Reset global context back to ROOT to avoid leaking between tests.
    context.with(ROOT_CONTEXT, () => {})
  })

  describe('injectTraceparent', () => {
    it('returns undefined when no active context', () => {
      expect(injectTraceparent()).toBeUndefined()
    })

    it('returns a traceparent string referencing the active span', () => {
      const span = tracer.startSpan('outer')
      const captured = context.with(trace.setSpan(context.active(), span), () =>
        injectTraceparent()
      )
      span.end()

      expect(captured).toBeDefined()
      const spanContext = span.spanContext()
      expect(captured).toContain(spanContext.traceId)
      expect(captured).toContain(spanContext.spanId)
    })
  })

  describe('withExtractedContext', () => {
    it('runs fn under the currently-active context when envelope has no traceparent', () => {
      const span = tracer.startSpan('caller')
      const activeTraceId = context.with(trace.setSpan(context.active(), span), () =>
        withExtractedContext(null, () => trace.getSpan(context.active())?.spanContext().traceId)
      )
      span.end()

      expect(activeTraceId).toBe(span.spanContext().traceId)
    })

    it('sets Activity.Current to the extracted context for the duration of fn', () => {
      const parent = tracer.startSpan('upstream')
      const carrier: Record<string, string> = {}
      context.with(trace.setSpan(context.active(), parent), () => {
        propagation.inject(context.active(), carrier)
      })
      parent.end()

      const envelope: TraceCarrier = { traceparent: carrier['traceparent'] }

      let capturedTraceId: string | undefined
      withExtractedContext(envelope, () => {
        capturedTraceId = trace.getSpan(context.active())?.spanContext().traceId
      })

      expect(capturedTraceId).toBe(parent.spanContext().traceId)
      // Outside the callback, the extracted context is no longer active.
      expect(trace.getSpan(context.active())).toBeUndefined()
    })
  })

  describe('traceInvoke', () => {
    it('creates a client span and prepends traceparent as the first argument', async () => {
      const invoke = vi.fn().mockResolvedValue('ok')
      const result = await traceInvoke<string>(
        invoke as unknown as HubInvoke,
        'JoinSession',
        'session-123',
        true
      )

      expect(result).toBe('ok')
      expect(invoke).toHaveBeenCalledTimes(1)

      const [methodName, firstArg, ...rest] = invoke.mock.calls[0] as [string, string, ...unknown[]]
      expect(methodName).toBe('JoinSession')
      expect(firstArg).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$/)
      expect(rest).toEqual(['session-123', true])

      const spans = exporter.getFinishedSpans()
      expect(spans).toHaveLength(1)
      expect(spans[0].name).toBe('signalr.invoke.JoinSession')
      // Span kind 2 === CLIENT
      expect(spans[0].kind).toBe(2)
      // Prepended traceparent references the span that owns the invoke.
      expect(firstArg).toContain(spans[0].spanContext().traceId)
      expect(firstArg).toContain(spans[0].spanContext().spanId)
    })

    it('records the exception and rethrows when the invoke rejects', async () => {
      const boom = new Error('boom')
      const invoke = vi.fn().mockRejectedValue(boom)

      await expect(
        traceInvoke(invoke as unknown as HubInvoke, 'SendMessage', 's1', 'hi')
      ).rejects.toBe(boom)

      const spans = exporter.getFinishedSpans()
      expect(spans).toHaveLength(1)
      expect(spans[0].status.code).toBe(2) // ERROR
      expect(spans[0].events.some((e) => e.name === 'exception')).toBe(true)
    })
  })
})

import { afterEach, beforeEach, vi } from 'vitest';
import {
  BasicTracerProvider,
  InMemorySpanExporter,
  SimpleSpanProcessor,
  type ReadableSpan,
} from '@opentelemetry/sdk-trace-base';
import { trace } from '@opentelemetry/api';
import { emitAndFormatSSE, formatSSE, streamSessionEvents } from '#src/services/sse-writer.js';
import { createMockSessionManager } from '../helpers/mock-session-manager.js';
import {
  createAssistantMessage,
  createResultMessage,
  createSystemMessage,
} from '../helpers/test-fixtures.js';
import { collectAsyncGenerator } from '../helpers/async-helpers.js';
import { parseSSEEvents } from '../helpers/sse-helpers.js';

describe('formatSSE', () => {
  it('produces event: name\\ndata: {...}\\n\\n format', () => {
    const result = formatSSE('test_event', { key: 'value' });

    expect(result).toBe('event: test_event\ndata: {"key":"value"}\n\n');
  });

  it('JSON.stringifies data', () => {
    const result = formatSSE('msg', { count: 42, nested: { a: true } });
    const parsed = parseSSEEvents(result);

    expect(parsed).toHaveLength(1);
    expect(parsed[0].event).toBe('msg');
    expect(parsed[0].data).toEqual({ count: 42, nested: { a: true } });
  });
});

describe('streamSessionEvents (A2A Protocol)', () => {
  // A2A Protocol: errors are emitted as status-update events with state 'failed'
  it('yields A2A status-update with failed state for non-existent session', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue(undefined);

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 'bad-id'));

    expect(chunks).toHaveLength(1);
    const events = parseSSEEvents(chunks[0]);
    expect(events[0].event).toBe('status-update');
    expect(events[0].data.kind).toBe('status-update');
    expect(events[0].data.status.state).toBe('failed');
    expect(events[0].data.final).toBe(true);
  });

  // A2A Protocol: session start emits 'task' event with state 'submitted'
  it('first yields A2A task event with submitted state', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({
      id: 'sess-1',
      conversationId: 'conv-1',
    });
    sm.stream.mockReturnValue((async function* () {})());

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 'sess-1'));

    expect(chunks.length).toBeGreaterThanOrEqual(1);
    const events = parseSSEEvents(chunks[0]);
    expect(events[0].event).toBe('task');
    expect(events[0].data.kind).toBe('task');
    expect(events[0].data.id).toBe('sess-1');
    expect(events[0].data.contextId).toBe('conv-1');
    expect(events[0].data.status.state).toBe('submitted');
  });

  // A2A Protocol: emits task -> status-update(working) -> messages -> status-update(completed)
  it('yields A2A events for SDK messages', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's1', conversationId: 's1' });

    const assistantMsg = createAssistantMessage();
    const resultMsg = createResultMessage();

    sm.stream.mockReturnValue(
      (async function* () {
        yield assistantMsg;
        yield resultMsg;
      })(),
    );

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 's1'));
    const allText = chunks.join('');
    const events = parseSSEEvents(allText);

    const eventNames = events.map((e) => e.event);
    // A2A event sequence: task, status-update(working), message, status-update(completed)
    expect(eventNames).toContain('task');
    expect(eventNames).toContain('status-update');
    expect(eventNames).toContain('message');

    // First event is task with submitted state
    expect(events[0].event).toBe('task');
    expect(events[0].data.status.state).toBe('submitted');

    // Second event is status-update with working state
    expect(events[1].event).toBe('status-update');
    expect(events[1].data.status.state).toBe('working');

    // Last event should be status-update with completed state
    const lastEvent = events[events.length - 1];
    expect(lastEvent.event).toBe('status-update');
    expect(lastEvent.data.status.state).toBe('completed');
    expect(lastEvent.data.final).toBe(true);
  });

  // A2A Protocol: result message triggers status-update with final=true
  it('stops after result message (emits final status-update)', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's1', conversationId: 's1' });

    const resultMsg = createResultMessage();
    const afterResult = createAssistantMessage({ type: 'assistant' as any });

    let yieldedAfter = false;
    sm.stream.mockReturnValue(
      (async function* () {
        yield resultMsg;
        yieldedAfter = true;
        yield afterResult;
      })(),
    );

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 's1'));
    const allText = chunks.join('');
    const events = parseSSEEvents(allText);

    // Last event should be status-update with final=true
    const statusUpdates = events.filter((e) => e.event === 'status-update');
    const finalUpdate = statusUpdates[statusUpdates.length - 1];
    expect(finalUpdate.data.final).toBe(true);
    expect(finalUpdate.data.status.state).toMatch(/completed|failed/);
  });

  // SDK query iterators stay open across turns; post-result task_notification
  // / task_started / task_updated messages MUST keep flowing to the consumer.
  // Closing the SSE generator on `result` orphans them in OutputChannel until
  // the user's next prompt drains it, which is the bug this test guards.
  it('keeps emitting after a result so post-result task_notification reaches the consumer', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's1', conversationId: 's1' });

    const assistantMsg = createAssistantMessage();
    const resultMsg = createResultMessage();
    const taskNotification = createSystemMessage({
      subtype: 'task_notification',
    } as any);

    sm.stream.mockReturnValue(
      (async function* () {
        yield assistantMsg;
        yield resultMsg;
        yield taskNotification;
      })(),
    );

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 's1'));
    const allText = chunks.join('');
    const events = parseSSEEvents(allText);

    const completed = events.filter(
      (e) => e.event === 'status-update' && (e.data as any).status?.state === 'completed',
    );
    expect(completed).toHaveLength(1);

    // assistant text + post-result task_notification system message
    const messages = events.filter((e) => e.event === 'message');
    expect(messages.length).toBeGreaterThanOrEqual(2);
  });

  // A2A Protocol: errors are emitted as status-update with state 'failed'
  it('yields A2A status-update with failed state when stream throws', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's1', conversationId: 's1' });
    sm.stream.mockReturnValue(
      (async function* () {
        throw new Error('SDK crashed');
      })(),
    );

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 's1'));
    const allText = chunks.join('');
    const events = parseSSEEvents(allText);

    const errorEvent = events.find(
      (e) => e.event === 'status-update' && e.data.status?.state === 'failed',
    );
    expect(errorEvent).toBeDefined();
    expect(errorEvent!.data.final).toBe(true);
  });
});

describe('emitAndFormatSSE — homespun.a2a.emit span', () => {
  let exporter: InMemorySpanExporter;
  let provider: BasicTracerProvider;

  beforeEach(() => {
    exporter = new InMemorySpanExporter();
    provider = new BasicTracerProvider({
      spanProcessors: [new SimpleSpanProcessor(exporter)],
    });
    trace.setGlobalTracerProvider(provider);
  });

  afterEach(async () => {
    await provider.shutdown();
    exporter.reset();
    trace.disable();
  });

  function findEmitSpan(): ReadableSpan | undefined {
    return exporter.getFinishedSpans().find((s) => s.name === 'homespun.a2a.emit');
  }

  it('emits one homespun.a2a.emit span per call with correlation attrs', () => {
    emitAndFormatSSE('sess-A', 'message', {
      kind: 'message',
      messageId: 'm-1',
      contextId: 'sess-A',
      taskId: 't-1',
      parts: [{ kind: 'text', text: 'hi' }],
    });

    const span = findEmitSpan();
    expect(span).toBeDefined();
    expect(span!.attributes['homespun.session.id']).toBe('sess-A');
    expect(span!.attributes['homespun.a2a.kind']).toBe('message');
    expect(span!.attributes['homespun.message.id']).toBe('m-1');
    expect(span!.attributes['homespun.task.id']).toBe('t-1');
  });

  it('falls back to caller-supplied sessionId when payload contextId is absent', () => {
    emitAndFormatSSE('sess-B', 'status-update', {
      kind: 'status-update',
      taskId: 't-2',
      status: { state: 'working', timestamp: '2026-04-20T00:00:00Z' },
    });

    const span = findEmitSpan();
    expect(span!.attributes['homespun.session.id']).toBe('sess-B');
    expect(span!.attributes['homespun.status.timestamp']).toBe('2026-04-20T00:00:00Z');
  });

  it('omits content preview by default in non-production env', () => {
    // gateContentPreview returns undefined for events that aren't "message" kind,
    // and gates by CONTENT_PREVIEW_CHARS for message kinds. Without the env set,
    // tests run with NODE_ENV=test → non-production default of 80 chars.
    emitAndFormatSSE('sess-C', 'task', {
      kind: 'task',
      id: 't-3',
      contextId: 'sess-C',
    });

    const span = findEmitSpan();
    expect(span!.attributes['homespun.content.preview']).toBeUndefined();
  });
});

// FI-2: control-event paths in sse-writer.streamSessionEvents — the
// `status_resumed` branch (lines 215-223 in sse-writer.ts) and the canonical
// `question_pending` / `plan_pending` translator path (lines 225-232).
describe('streamSessionEvents — control events (FI-2)', () => {
  it('emits a working status-update for status_resumed control events', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's-resume', conversationId: 's-resume' });

    sm.stream.mockReturnValue(
      (async function* () {
        yield { type: 'status_resumed', data: {} };
      })(),
    );

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 's-resume'));
    const events = parseSSEEvents(chunks.join(''));

    const states = events
      .filter((e) => e.event === 'status-update')
      .map((e) => e.data.status.state);

    expect(states).toContain('working');
    // Two working entries are expected — one from the initial task→working
    // transition and one from the status_resumed branch — both surface as
    // non-final status updates.
    expect(states.filter((s: string) => s === 'working').length).toBeGreaterThanOrEqual(2);
  });

  it('translates question_pending into an input-required status-update', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's-q', conversationId: 's-q' });

    sm.stream.mockReturnValue(
      (async function* () {
        yield {
          type: 'question_pending',
          data: {
            questions: [
              {
                question: 'Which?',
                header: 'Pick',
                options: [{ label: 'a' }, { label: 'b' }],
                multiSelect: false,
              },
            ],
          },
        };
      })(),
    );

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 's-q'));
    const events = parseSSEEvents(chunks.join(''));
    const inputRequired = events.find(
      (e) => e.event === 'status-update' && e.data.status.state === 'input-required',
    );
    expect(inputRequired).toBeDefined();
    expect(inputRequired!.data.metadata?.inputType).toBe('question');
  });
});

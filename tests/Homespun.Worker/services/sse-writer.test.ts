import { vi } from 'vitest';
import { formatSSE, streamSessionEvents } from '#src/services/sse-writer.js';
import { createMockSessionManager } from '../helpers/mock-session-manager.js';
import { createAssistantMessage, createResultMessage } from '../helpers/test-fixtures.js';
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

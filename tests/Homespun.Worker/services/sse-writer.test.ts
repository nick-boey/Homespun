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

describe('streamSessionEvents', () => {
  it('yields SSE error with SESSION_NOT_FOUND for non-existent session', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue(undefined);

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 'bad-id'));

    expect(chunks).toHaveLength(1);
    const events = parseSSEEvents(chunks[0]);
    expect(events[0].event).toBe('error');
    expect(events[0].data).toMatchObject({
      code: 'SESSION_NOT_FOUND',
      sessionId: 'bad-id',
    });
  });

  it('first yields session_started event with sessionId and conversationId', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({
      id: 'sess-1',
      conversationId: 'conv-1',
    });
    sm.stream.mockReturnValue((async function* () {})());

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 'sess-1'));

    expect(chunks.length).toBeGreaterThanOrEqual(1);
    const events = parseSSEEvents(chunks[0]);
    expect(events[0].event).toBe('session_started');
    expect(events[0].data).toEqual({
      sessionId: 'sess-1',
      conversationId: 'conv-1',
    });
  });

  it('yields each SDK message with msg.type as event name', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's1', conversationId: undefined });

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
    expect(eventNames).toContain('session_started');
    expect(eventNames).toContain('assistant');
    expect(eventNames).toContain('result');
  });

  it('stops after result message type', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's1', conversationId: undefined });

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

    const eventNames = events.map((e) => e.event);
    expect(eventNames).toContain('result');
    // The generator returns after result, so no assistant event after
    expect(eventNames.filter((n) => n === 'assistant')).toHaveLength(0);
  });

  it('yields error SSE with AGENT_ERROR code when stream throws', async () => {
    const sm = createMockSessionManager();
    sm.get.mockReturnValue({ id: 's1', conversationId: undefined });
    sm.stream.mockReturnValue(
      (async function* () {
        throw new Error('SDK crashed');
      })(),
    );

    const chunks = await collectAsyncGenerator(streamSessionEvents(sm as any, 's1'));
    const allText = chunks.join('');
    const events = parseSSEEvents(allText);

    const errorEvent = events.find((e) => e.event === 'error');
    expect(errorEvent).toBeDefined();
    expect(errorEvent!.data).toMatchObject({
      code: 'AGENT_ERROR',
      message: 'SDK crashed',
      sessionId: 's1',
    });
  });
});

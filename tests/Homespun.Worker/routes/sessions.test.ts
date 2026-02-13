import { vi } from 'vitest';

vi.mock('#src/services/session-discovery.js', () => ({
  discoverSessions: vi.fn().mockResolvedValue([]),
}));

import { createSessionsRoute } from '#src/routes/sessions.js';
import { discoverSessions } from '#src/services/session-discovery.js';
import { createMockSessionManager } from '../helpers/mock-session-manager.js';
import { parseSSEEvents } from '../helpers/sse-helpers.js';

const mockDiscoverSessions = vi.mocked(discoverSessions);

function createApp() {
  const sm = createMockSessionManager();
  const app = createSessionsRoute(sm as any);
  return { sm, app };
}

describe('GET /sessions', () => {
  it('returns active and discovered sessions', async () => {
    const { sm, app } = createApp();
    sm.list.mockReturnValue([{ sessionId: 's1', status: 'idle' }]);
    mockDiscoverSessions.mockResolvedValue([
      { sessionId: 'd1', filePath: '/path', lastModified: '2025-01-01' },
    ]);

    const res = await app.request('/');
    const body = await res.json();

    expect(body.sessions).toHaveLength(1);
    expect(body.discoveredSessions).toHaveLength(1);
  });

  it('returns empty arrays when no sessions', async () => {
    const { sm, app } = createApp();
    sm.list.mockReturnValue([]);
    mockDiscoverSessions.mockResolvedValue([]);

    const res = await app.request('/');
    const body = await res.json();

    expect(body.sessions).toEqual([]);
    expect(body.discoveredSessions).toEqual([]);
  });
});

describe('POST /sessions', () => {
  it('returns SSE stream response', async () => {
    const { sm, app } = createApp();
    sm.create.mockResolvedValue({ id: 'new-session' });
    sm.get.mockReturnValue({ id: 'new-session', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    const res = await app.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        prompt: 'Hello',
        model: 'claude-sonnet-4-20250514',
        mode: 'Plan',
      }),
    });

    expect(res.status).toBe(200);
    const text = await res.text();
    const events = parseSSEEvents(text);
    expect(events.some((e) => e.event === 'session_started')).toBe(true);
  });

  it('returns STARTUP_ERROR event on create failure', async () => {
    const { sm, app } = createApp();
    sm.create.mockRejectedValue(new Error('SDK init failed'));

    const res = await app.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        prompt: 'Hello',
        model: 'claude-sonnet-4-20250514',
        mode: 'Plan',
      }),
    });

    const text = await res.text();
    const events = parseSSEEvents(text);
    const errorEvent = events.find((e) => e.event === 'error');
    expect(errorEvent).toBeDefined();
    expect(errorEvent!.data).toMatchObject({
      code: 'STARTUP_ERROR',
      message: 'SDK init failed',
    });
  });
});

describe('POST /sessions/:id/message', () => {
  it('calls send() and streams events', async () => {
    const { sm, app } = createApp();
    sm.send.mockResolvedValue(undefined);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    const res = await app.request('/sess-1/message', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Follow up' }),
    });

    expect(res.status).toBe(200);
    expect(sm.send).toHaveBeenCalledWith('sess-1', 'Follow up', undefined, undefined);
  });

  it('passes permissionMode to send()', async () => {
    const { sm, app } = createApp();
    sm.send.mockResolvedValue(undefined);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    await app.request('/sess-1/message', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Follow up', permissionMode: 'BypassPermissions' }),
    });

    expect(sm.send).toHaveBeenCalledWith('sess-1', 'Follow up', undefined, 'BypassPermissions');
  });

  it('passes undefined permissionMode when not provided', async () => {
    const { sm, app } = createApp();
    sm.send.mockResolvedValue(undefined);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    await app.request('/sess-1/message', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Follow up' }),
    });

    expect(sm.send).toHaveBeenCalledWith('sess-1', 'Follow up', undefined, undefined);
  });

  it('returns MESSAGE_ERROR event on send failure', async () => {
    const { sm, app } = createApp();
    sm.send.mockRejectedValue(new Error('Session lost'));

    const res = await app.request('/sess-1/message', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Follow up' }),
    });

    const text = await res.text();
    const events = parseSSEEvents(text);
    const errorEvent = events.find((e) => e.event === 'error');
    expect(errorEvent!.data).toMatchObject({
      code: 'MESSAGE_ERROR',
    });
  });
});

describe('POST /sessions/:id/answer', () => {
  it('resolves pending question when one exists', async () => {
    const { sm, app } = createApp();
    sm.resolvePendingQuestion.mockReturnValue(true);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    const res = await app.request('/sess-1/answer', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        answers: {
          'Which framework?': 'React',
          'Include tests?': 'Yes',
        },
      }),
    });

    expect(res.status).toBe(200);
    expect(sm.resolvePendingQuestion).toHaveBeenCalledWith('sess-1', {
      'Which framework?': 'React',
      'Include tests?': 'Yes',
    });
    // Should not send as message when pending question was resolved
    expect(sm.send).not.toHaveBeenCalled();
  });

  it('falls back to message format when no pending question', async () => {
    const { sm, app } = createApp();
    sm.resolvePendingQuestion.mockReturnValue(false);
    sm.send.mockResolvedValue(undefined);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    await app.request('/sess-1/answer', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        answers: {
          'Which framework?': 'React',
          'Include tests?': 'Yes',
        },
      }),
    });

    const sentMessage = sm.send.mock.calls[0][1] as string;
    expect(sentMessage).toContain("I've answered your questions:");
    expect(sentMessage).toContain('**Which framework?**');
    expect(sentMessage).toContain('My answer: React');
    expect(sentMessage).toContain('**Include tests?**');
    expect(sentMessage).toContain('My answer: Yes');
    expect(sentMessage).toContain('Please continue with the task based on my answers above.');
  });

  it('returns ANSWER_ERROR event on send failure in fallback mode', async () => {
    const { sm, app } = createApp();
    sm.resolvePendingQuestion.mockReturnValue(false);
    sm.send.mockRejectedValue(new Error('Send failed'));

    const res = await app.request('/sess-1/answer', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ answers: { Q: 'A' } }),
    });

    const text = await res.text();
    const events = parseSSEEvents(text);
    const errorEvent = events.find((e) => e.event === 'error');
    expect(errorEvent!.data).toMatchObject({
      code: 'ANSWER_ERROR',
    });
  });
});

describe('POST /sessions/:id/approve-plan', () => {
  it('resolves pending plan approval when approved', async () => {
    const { sm, app } = createApp();
    sm.resolvePendingPlanApproval.mockReturnValue(true);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    const res = await app.request('/sess-1/approve-plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved: true }),
    });

    expect(res.status).toBe(200);
    expect(sm.resolvePendingPlanApproval).toHaveBeenCalledWith('sess-1', true);
  });

  it('resolves pending plan approval when rejected', async () => {
    const { sm, app } = createApp();
    sm.resolvePendingPlanApproval.mockReturnValue(true);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    const res = await app.request('/sess-1/approve-plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved: false }),
    });

    expect(res.status).toBe(200);
    expect(sm.resolvePendingPlanApproval).toHaveBeenCalledWith('sess-1', false);
  });

  it('returns 400 when no pending plan approval', async () => {
    const { sm, app } = createApp();
    sm.resolvePendingPlanApproval.mockReturnValue(false);

    const res = await app.request('/sess-1/approve-plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved: true }),
    });

    expect(res.status).toBe(400);
    const body = await res.json();
    expect(body.error).toBe('No pending plan approval for session');
  });
});

describe('POST /sessions/:id/interrupt', () => {
  it('closes session and returns ok for existing session', async () => {
    const { sm, app } = createApp();
    sm.get.mockReturnValue({ id: 'sess-1' });
    sm.close.mockResolvedValue(undefined);

    const res = await app.request('/sess-1/interrupt', { method: 'POST' });
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body).toEqual({ ok: true });
    expect(sm.close).toHaveBeenCalledWith('sess-1');
  });

  it('returns 404 for non-existent session', async () => {
    const { sm, app } = createApp();
    sm.get.mockReturnValue(undefined);

    const res = await app.request('/bad-id/interrupt', { method: 'POST' });

    expect(res.status).toBe(404);
  });
});

describe('GET /sessions/:id', () => {
  it('returns session info for existing session', async () => {
    const { sm, app } = createApp();
    sm.get.mockReturnValue({
      id: 'sess-1',
      conversationId: 'conv-1',
      mode: 'Plan',
      model: 'claude-sonnet-4-20250514',
      status: 'idle',
      createdAt: new Date('2025-01-01'),
      lastActivityAt: new Date('2025-01-02'),
    });

    const res = await app.request('/sess-1');
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body.sessionId).toBe('sess-1');
    expect(body.mode).toBe('Plan');
  });

  it('returns 404 for non-existent session', async () => {
    const { sm, app } = createApp();
    sm.get.mockReturnValue(undefined);

    const res = await app.request('/bad-id');

    expect(res.status).toBe(404);
  });
});

describe('DELETE /sessions/:id', () => {
  it('closes session and returns ok', async () => {
    const { sm, app } = createApp();
    sm.get.mockReturnValue({ id: 'sess-1' });
    sm.close.mockResolvedValue(undefined);

    const res = await app.request('/sess-1', { method: 'DELETE' });
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body.ok).toBe(true);
    expect(sm.close).toHaveBeenCalledWith('sess-1');
  });

  it('returns 404 for non-existent session', async () => {
    const { sm, app } = createApp();
    sm.get.mockReturnValue(undefined);

    const res = await app.request('/bad-id', { method: 'DELETE' });

    expect(res.status).toBe(404);
  });
});

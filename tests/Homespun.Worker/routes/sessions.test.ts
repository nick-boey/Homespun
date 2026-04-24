import { vi } from 'vitest';

vi.mock('#src/services/session-discovery.js', () => ({
  discoverSessions: vi.fn().mockResolvedValue([]),
}));

import { createSessionsRoute } from '#src/routes/sessions.js';
import { discoverSessions } from '#src/services/session-discovery.js';
import { createMockSessionManager } from '../helpers/mock-session-manager.js';
import { parseSSEEvents } from '../helpers/sse-helpers.js';
import {
  createAssistantMessage,
  createResultMessage,
  createSystemMessage,
} from '../helpers/test-fixtures.js';

const mockDiscoverSessions = vi.mocked(discoverSessions);

function createApp() {
  const sm = createMockSessionManager();
  const app = createSessionsRoute(sm as any);
  return { sm, app };
}

describe('GET /sessions/active', () => {
  it('returns hasActiveSession: false when no sessions', async () => {
    const { sm, app } = createApp();
    sm.list.mockReturnValue([]);

    const res = await app.request('/active');
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body).toEqual({ hasActiveSession: false });
  });

  it('returns hasActiveSession: false when all sessions are closed', async () => {
    const { sm, app } = createApp();
    sm.list.mockReturnValue([{ sessionId: 's1', status: 'closed' }]);

    const res = await app.request('/active');
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body).toEqual({ hasActiveSession: false });
  });

  it('returns active session details when idle session exists', async () => {
    const { sm, app } = createApp();
    sm.list.mockReturnValue([
      { sessionId: 's1', status: 'idle', lastActivityAt: '2025-01-01T00:00:00Z' },
    ]);
    sm.hasPending.mockImplementation(() => false);

    const res = await app.request('/active');
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body).toEqual({
      hasActiveSession: true,
      sessionId: 's1',
      status: 'idle',
      hasPendingQuestion: false,
      hasPendingPlanApproval: false,
      lastActivityAt: '2025-01-01T00:00:00Z',
    });
  });

  it('returns active session details when streaming session exists', async () => {
    const { sm, app } = createApp();
    sm.list.mockReturnValue([
      { sessionId: 's1', status: 'streaming', lastActivityAt: '2025-01-01T00:00:00Z' },
    ]);
    sm.hasPending.mockImplementation(() => false);

    const res = await app.request('/active');
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body.hasActiveSession).toBe(true);
    expect(body.status).toBe('streaming');
  });

  it('includes pending question flag when question is pending', async () => {
    const { sm, app } = createApp();
    sm.list.mockReturnValue([
      { sessionId: 's1', status: 'idle', lastActivityAt: '2025-01-01T00:00:00Z' },
    ]);
    sm.hasPending.mockImplementation((_: string, kind: string) => kind === 'question');

    const res = await app.request('/active');
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body.hasPendingQuestion).toBe(true);
    expect(body.hasPendingPlanApproval).toBe(false);
  });

  it('includes pending plan approval flag when plan is pending', async () => {
    const { sm, app } = createApp();
    sm.list.mockReturnValue([
      { sessionId: 's1', status: 'idle', lastActivityAt: '2025-01-01T00:00:00Z' },
    ]);
    sm.hasPending.mockImplementation((_: string, kind: string) => kind === 'plan');

    const res = await app.request('/active');
    const body = await res.json();

    expect(res.status).toBe(200);
    expect(body.hasPendingQuestion).toBe(false);
    expect(body.hasPendingPlanApproval).toBe(true);
  });
});

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
  it('returns JSON body with sessionId and conversationId', async () => {
    const { sm, app } = createApp();
    sm.create.mockResolvedValue({ id: 'new-session', conversationId: 'c1' });

    const res = await app.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        prompt: 'Hello',
        model: 'sonnet',
        mode: 'Plan',
        systemPrompt: 'sys',
        workingDirectory: '/tmp/wd',
        resumeSessionId: 'prev-1',
      }),
    });

    expect(res.status).toBe(200);
    expect(res.headers.get('content-type')).toContain('application/json');
    const body = await res.json();
    expect(body).toEqual({ sessionId: 'new-session', conversationId: 'c1' });

    expect(sm.create).toHaveBeenCalledTimes(1);
    expect(sm.create).toHaveBeenCalledWith({
      prompt: 'Hello',
      model: 'sonnet',
      mode: 'Plan',
      systemPrompt: 'sys',
      workingDirectory: '/tmp/wd',
      resumeSessionId: 'prev-1',
    });
  });

  it('returns conversationId: null when WorkerSession has none', async () => {
    const { sm, app } = createApp();
    sm.create.mockResolvedValue({ id: 'new-session' });

    const res = await app.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        prompt: 'Hello',
        model: 'sonnet',
        mode: 'Plan',
      }),
    });

    expect(res.status).toBe(200);
    expect(res.headers.get('content-type')).toContain('application/json');
    const body = await res.json();
    expect(body).toEqual({ sessionId: 'new-session', conversationId: null });
  });

  it('returns JSON 500 with STARTUP_ERROR code on create failure', async () => {
    const { sm, app } = createApp();
    sm.create.mockRejectedValue(new Error('SDK init failed'));

    const res = await app.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        prompt: 'Hello',
        model: 'sonnet',
        mode: 'Plan',
      }),
    });

    expect(res.status).toBe(500);
    expect(res.headers.get('content-type')).toContain('application/json');
    const body = await res.json();
    expect(body).toEqual({ error: 'SDK init failed', code: 'STARTUP_ERROR' });
  });
});

describe('POST /sessions/:id/message', () => {
  it('returns JSON { ok: true } and calls send with (sessionId, message, model, mode)', async () => {
    const { sm, app } = createApp();
    sm.send.mockResolvedValue(undefined);

    const res = await app.request('/sess-1/message', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Follow up', model: 'sonnet', mode: 'Build' }),
    });

    expect(res.status).toBe(200);
    expect(res.headers.get('content-type')).toContain('application/json');
    const body = await res.json();
    expect(body).toEqual({ ok: true });

    expect(sm.send).toHaveBeenCalledTimes(1);
    expect(sm.send).toHaveBeenCalledWith('sess-1', 'Follow up', 'sonnet', 'Build');
  });

  it('returns JSON 500 with MESSAGE_ERROR code when send throws', async () => {
    const { sm, app } = createApp();
    sm.send.mockRejectedValue(new Error('boom'));

    const res = await app.request('/sess-1/message', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Follow up' }),
    });

    expect(res.status).toBe(500);
    expect(res.headers.get('content-type')).toContain('application/json');
    const body = await res.json();
    expect(body).toEqual({ ok: false, error: 'boom', code: 'MESSAGE_ERROR' });
  });
});

describe('POST /sessions/:id/answer', () => {
  it('resolves pending question when one exists', async () => {
    const { sm, app } = createApp();
    sm.resolvePending.mockReturnValue(true);
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
    expect(sm.resolvePending).toHaveBeenCalledWith('sess-1', 'question', {
      answers: {
        'Which framework?': 'React',
        'Include tests?': 'Yes',
      },
    });
    // Should not send as message when pending question was resolved
    expect(sm.send).not.toHaveBeenCalled();
  });

  it('returns 400 when no pending question', async () => {
    const { sm, app } = createApp();
    sm.resolvePending.mockReturnValue(false);

    const res = await app.request('/sess-1/answer', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ answers: { Q: 'A' } }),
    });

    expect(res.status).toBe(400);
    const body = await res.json();
    expect(body.error).toBe('No pending question');
  });
});

describe('POST /sessions/:id/approve-plan', () => {
  it('resolves pending plan approval when approved', async () => {
    const { sm, app } = createApp();
    sm.resolvePending.mockReturnValue(true);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    const res = await app.request('/sess-1/approve-plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved: true }),
    });

    expect(res.status).toBe(200);
    expect(sm.resolvePending).toHaveBeenCalledWith('sess-1', 'plan', {
      approved: true,
      keepContext: undefined,
      feedback: undefined,
    });
  });

  it('resolves pending plan approval when rejected', async () => {
    const { sm, app } = createApp();
    sm.resolvePending.mockReturnValue(true);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    const res = await app.request('/sess-1/approve-plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved: false }),
    });

    expect(res.status).toBe(200);
    expect(sm.resolvePending).toHaveBeenCalledWith('sess-1', 'plan', {
      approved: false,
      keepContext: undefined,
      feedback: undefined,
    });
  });

  it('passes keepContext and feedback to resolvePending', async () => {
    const { sm, app } = createApp();
    sm.resolvePending.mockReturnValue(true);
    sm.get.mockReturnValue({ id: 'sess-1', conversationId: 'c1' });
    sm.stream.mockReturnValue((async function* () {})());

    const res = await app.request('/sess-1/approve-plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved: true, keepContext: true, feedback: 'looks good' }),
    });

    expect(res.status).toBe(200);
    expect(sm.resolvePending).toHaveBeenCalledWith('sess-1', 'plan', {
      approved: true,
      keepContext: true,
      feedback: 'looks good',
    });
  });

  it('returns 400 when no pending plan approval', async () => {
    const { sm, app } = createApp();
    sm.resolvePending.mockReturnValue(false);

    const res = await app.request('/sess-1/approve-plan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ approved: true }),
    });

    expect(res.status).toBe(400);
    const body = await res.json();
    expect(body.error).toBe('No pending plan approval');
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
      model: 'sonnet',
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

describe('POST /sessions/:id/clear-context', () => {
  it('returns JSON body with oldSessionId and newSessionId', async () => {
    const { sm, app } = createApp();
    sm.clearContextAndCreate.mockResolvedValue({
      newSession: { id: 'new-sess' },
      oldSessionId: 'old-sess',
    });

    const res = await app.request('/old-sess/clear-context', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        prompt: 'Restart',
        model: 'sonnet',
        mode: 'Build',
        systemPrompt: 'sys',
        workingDirectory: '/tmp/wd',
      }),
    });

    expect(res.status).toBe(200);
    expect(res.headers.get('content-type')).toContain('application/json');
    const body = await res.json();
    expect(body).toEqual({ oldSessionId: 'old-sess', newSessionId: 'new-sess' });

    expect(sm.clearContextAndCreate).toHaveBeenCalledTimes(1);
    expect(sm.clearContextAndCreate).toHaveBeenCalledWith('old-sess', {
      prompt: 'Restart',
      model: 'sonnet',
      mode: 'Build',
      systemPrompt: 'sys',
      workingDirectory: '/tmp/wd',
    });
  });

  it('returns JSON 500 with CLEAR_CONTEXT_ERROR code when clearContextAndCreate throws', async () => {
    const { sm, app } = createApp();
    sm.clearContextAndCreate.mockRejectedValue(new Error('clear boom'));

    const res = await app.request('/old-sess/clear-context', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        prompt: 'Restart',
        model: 'sonnet',
        mode: 'Build',
      }),
    });

    expect(res.status).toBe(500);
    expect(res.headers.get('content-type')).toContain('application/json');
    const body = await res.json();
    expect(body).toEqual({ error: 'clear boom', code: 'CLEAR_CONTEXT_ERROR' });
  });
});

describe('GET /:id/events', () => {
  it('emits every A2A event including task_notification arriving after result', async () => {
    const { sm, app } = createApp();

    // Seed: assistant text → result → task_notification (crosses the turn boundary).
    const events = [
      createAssistantMessage({ session_id: 's1' }),
      createResultMessage({
        session_id: 's1',
        subtype: 'success',
        is_error: false,
      } as any),
      createSystemMessage({ session_id: 's1', subtype: 'task_notification' } as any),
    ];

    sm.get.mockReturnValue({ id: 's1', conversationId: 's1' });
    sm.stream.mockReturnValue(
      (async function* () {
        for (const e of events) yield e;
      })(),
    );

    const res = await app.request('/s1/events');

    expect(res.status).toBe(200);
    expect(res.headers.get('content-type')).toContain('text/event-stream');

    const text = await res.text();
    const frames = parseSSEEvents(text);

    const statusUpdates = frames.filter((f) => f.event === 'status-update');
    const messages = frames.filter((f) => f.event === 'message');

    // Result → completed translation should emit one status-update with
    // state 'completed' and final: true.
    const completed = statusUpdates.filter(
      (f) =>
        (f.data as any)?.status?.state === 'completed' &&
        (f.data as any)?.final === true,
    );
    expect(completed).toHaveLength(1);
    // At minimum: assistant text + task_notification (post-result) as message frames.
    expect(messages.length).toBeGreaterThanOrEqual(2);
  });
});

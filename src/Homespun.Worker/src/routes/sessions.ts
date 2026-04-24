import { Hono } from 'hono';
import { stream } from 'hono/streaming';
import type { SessionManager } from '../services/session-manager.js';
import { streamSessionEvents, formatSSE } from '../services/sse-writer.js';
import { discoverSessions } from '../services/session-discovery.js';
import type {
  StartSessionRequest,
  SendMessageRequest,
  AnswerQuestionRequest,
  ApprovePlanRequest,
  ClearContextRequest,
} from '../types/index.js';
import { info } from '../utils/otel-logger.js';

export function createSessionsRoute(sessionManager: SessionManager) {
  const sessions = new Hono();

  // GET /sessions - List active + discoverable sessions
  sessions.get('/', async (c) => {
    const activeSessions = sessionManager.list();
    const discoveredSessions = await discoverSessions();

    return c.json({
      sessions: activeSessions,
      discoveredSessions,
    });
  });

  // GET /sessions/active - Get currently active session (if any)
  sessions.get('/active', (c) => {
    const allSessions = sessionManager.list();
    const active = allSessions.find(s => s.status !== 'closed');

    if (!active) {
      return c.json({ hasActiveSession: false });
    }

    return c.json({
      hasActiveSession: true,
      sessionId: active.sessionId,
      status: active.status,
      mode: active.mode,
      model: active.model,
      permissionMode: active.permissionMode,
      hasPendingQuestion: sessionManager.hasPending(active.sessionId, 'question'),
      hasPendingPlanApproval: sessionManager.hasPending(active.sessionId, 'plan'),
      lastActivityAt: active.lastActivityAt,
      lastMessageType: active.lastMessageType,
      lastMessageSubtype: active.lastMessageSubtype,
    });
  });

  // POST /sessions - Start or resume a session (JSON response)
  sessions.post('/', async (c) => {
    const body = await c.req.json<StartSessionRequest>();
    info(`POST /sessions - mode=${body.mode}, model=${body.model}, workingDirectory=${body.workingDirectory}, resumeSessionId=${body.resumeSessionId || 'none'}`);

    try {
      const ws = await sessionManager.create({
        prompt: body.prompt,
        model: body.model,
        mode: body.mode,
        systemPrompt: body.systemPrompt,
        workingDirectory: body.workingDirectory,
        resumeSessionId: body.resumeSessionId,
      });
      return c.json({ sessionId: ws.id, conversationId: ws.conversationId ?? null });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return c.json({ error: message, code: 'STARTUP_ERROR' }, 500);
    }
  });

  // POST /sessions/:id/message - Send a message to an existing session (SSE stream)
  sessions.post('/:id/message', async (c) => {
    const sessionId = c.req.param('id');
    const body = await c.req.json<SendMessageRequest>();
    info(`POST /sessions/${sessionId}/message - mode=${body.mode}, messageLength=${body.message?.length}, model=${body.model}`);

    c.header('Content-Type', 'text/event-stream');
    c.header('Cache-Control', 'no-cache');
    c.header('Connection', 'keep-alive');

    return stream(c, async (s) => {
      try {
        await sessionManager.send(sessionId, body.message, body.model, body.mode);

        for await (const chunk of streamSessionEvents(sessionManager, sessionId)) {
          await s.write(chunk);
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        await s.write(formatSSE('error', {
          sessionId,
          message,
          code: 'MESSAGE_ERROR',
          isRecoverable: false,
        }));
      }
    });
  });

  // POST /sessions/:id/answer - Answer a pending question (JSON response)
  // Messages continue flowing through the original SSE stream after the promise resolves.
  sessions.post('/:id/answer', async (c) => {
    const sessionId = c.req.param('id');
    const body = await c.req.json<AnswerQuestionRequest>();
    info(`POST /sessions/${sessionId}/answer - ${Object.keys(body.answers).length} answers`);

    const resolved = sessionManager.resolvePending(sessionId, 'question', { answers: body.answers });
    if (!resolved) {
      return c.json({ ok: false, error: 'No pending question' }, 400);
    }

    return c.json({ ok: true });
  });

  // POST /sessions/:id/approve-plan - Approve or reject a pending plan (JSON response)
  // Messages continue flowing through the original SSE stream after the promise resolves.
  sessions.post('/:id/approve-plan', async (c) => {
    const sessionId = c.req.param('id');
    const body = await c.req.json<ApprovePlanRequest>();
    info(`POST /sessions/${sessionId}/approve-plan - approved=${body.approved}, keepContext=${body.keepContext}`);

    const resolved = sessionManager.resolvePending(sessionId, 'plan', {
      approved: body.approved,
      keepContext: body.keepContext,
      feedback: body.feedback,
    });
    if (!resolved) {
      return c.json({ ok: false, error: 'No pending plan approval' }, 400);
    }

    return c.json({ ok: true });
  });

  // POST /sessions/:id/mode - Change session mode without sending a message
  sessions.post('/:id/mode', async (c) => {
    const sessionId = c.req.param('id');
    const body = await c.req.json<{ mode: 'Plan' | 'Build' }>();
    info(`POST /sessions/${sessionId}/mode - mode=${body.mode}`);

    const success = await sessionManager.setMode(sessionId, body.mode);
    if (!success) {
      return c.json({ ok: false, error: `Session ${sessionId} not found` }, 404);
    }

    return c.json({ ok: true });
  });

  // POST /sessions/:id/model - Change session model without sending a message
  sessions.post('/:id/model', async (c) => {
    const sessionId = c.req.param('id');
    const body = await c.req.json<{ model: string }>();
    info(`POST /sessions/${sessionId}/model - model=${body.model}`);

    const success = sessionManager.setModel(sessionId, body.model);
    if (!success) {
      return c.json({ ok: false, error: `Session ${sessionId} not found` }, 404);
    }

    return c.json({ ok: true });
  });

  // POST /sessions/:id/interrupt - Interrupt current turn
  sessions.post('/:id/interrupt', async (c) => {
    const sessionId = c.req.param('id');
    const ws = sessionManager.get(sessionId);

    if (!ws) {
      return c.json({ message: `Session ${sessionId} not found` }, 404);
    }

    // The V2 SDK session doesn't expose interrupt directly,
    // so we close and remove the session
    await sessionManager.close(sessionId);
    return c.json({ ok: true });
  });

  // GET /sessions/:id/events - Long-lived SSE stream of session events.
  // Stays open for the life of the session; emits every A2A event including
  // task notifications that arrive after the SDK result message.
  sessions.get('/:id/events', async (c) => {
    const sessionId = c.req.param('id');
    info(`GET /sessions/${sessionId}/events - long-lived SSE consumer opened`);

    c.header('Content-Type', 'text/event-stream');
    c.header('Cache-Control', 'no-cache');
    c.header('Connection', 'keep-alive');

    return stream(c, async (s) => {
      for await (const chunk of streamSessionEvents(sessionManager, sessionId)) {
        await s.write(chunk);
      }
    });
  });

  // GET /sessions/:id - Get session info
  sessions.get('/:id', (c) => {
    const sessionId = c.req.param('id');
    const ws = sessionManager.get(sessionId);

    if (!ws) {
      return c.json({ message: `Session ${sessionId} not found` }, 404);
    }

    return c.json({
      sessionId: ws.id,
      conversationId: ws.conversationId,
      mode: ws.mode,
      model: ws.model,
      status: ws.status,
      createdAt: ws.createdAt,
      lastActivityAt: ws.lastActivityAt,
    });
  });

  // DELETE /sessions/:id - Close session
  sessions.delete('/:id', async (c) => {
    const sessionId = c.req.param('id');
    const ws = sessionManager.get(sessionId);

    if (!ws) {
      return c.json({ message: `Session ${sessionId} not found` }, 404);
    }

    await sessionManager.close(sessionId);
    return c.json({ ok: true, message: 'Session stopped', sessionId });
  });

  // POST /sessions/:id/clear-context - Clear context and start a new session (SSE stream)
  sessions.post('/:id/clear-context', async (c) => {
    const sessionId = c.req.param('id');
    const body = await c.req.json<ClearContextRequest>();
    info(`POST /sessions/${sessionId}/clear-context - mode=${body.mode}, model=${body.model}`);

    c.header('Content-Type', 'text/event-stream');
    c.header('Cache-Control', 'no-cache');
    c.header('Connection', 'keep-alive');

    return stream(c, async (s) => {
      try {
        const { newSession, oldSessionId } = await sessionManager.clearContextAndCreate(
          sessionId,
          {
            prompt: body.prompt,
            model: body.model,
            mode: body.mode,
            systemPrompt: body.systemPrompt,
            workingDirectory: body.workingDirectory,
          }
        );

        // Send initial event with new session info
        await s.write(formatSSE('context_cleared', {
          oldSessionId,
          newSessionId: newSession.id,
        }));

        // Stream the new session events
        for await (const chunk of streamSessionEvents(sessionManager, newSession.id)) {
          await s.write(chunk);
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        await s.write(formatSSE('error', {
          sessionId,
          message,
          code: 'CLEAR_CONTEXT_ERROR',
          isRecoverable: false,
        }));
      }
    });
  });

  return sessions;
}

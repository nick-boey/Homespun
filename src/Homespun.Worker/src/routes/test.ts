import { Hono } from 'hono';
import type { SessionManager } from '../services/session-manager.js';
import { error as logError, info } from '../utils/logger.js';

interface TestErrorRequest {
  message?: string;
}

interface TestErrorResponse {
  ok: boolean;
  sessionId?: string;
  message: string;
  error?: string;
}

/**
 * Creates test routes for forcing error states in workers.
 * These endpoints are used for testing the container restart functionality.
 */
export function createTestRoute(sessionManager: SessionManager) {
  const test = new Hono();

  /**
   * POST /api/test/error
   * Forces an error in the current active session by closing it abruptly.
   * This simulates an error state that would trigger the "Restart Container" flow.
   */
  test.post('/error', async (c) => {
    try {
      const body = await c.req.json<TestErrorRequest>().catch(() => ({} as TestErrorRequest));
      const errorMessage = body.message || 'Test error triggered via /api/test/error endpoint';

      // Find the active (non-closed) session
      const sessions = sessionManager.list();
      const active = sessions.find((s) => s.status !== 'closed');

      if (!active) {
        return c.json<TestErrorResponse>(
          {
            ok: false,
            message: 'No active session found to trigger error',
            error: 'NO_ACTIVE_SESSION',
          },
          400
        );
      }

      info(`Test error triggered for session ${active.sessionId}: ${errorMessage}`);
      logError(`[TEST] Forcing error state: ${errorMessage}`);

      // Close the session to trigger error propagation through the SSE stream
      await sessionManager.close(active.sessionId);

      return c.json<TestErrorResponse>({
        ok: true,
        sessionId: active.sessionId,
        message: `Error triggered for session ${active.sessionId}. Session closed.`,
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      logError(`Failed to trigger test error: ${message}`);
      return c.json<TestErrorResponse>(
        {
          ok: false,
          message: 'Failed to trigger test error',
          error: message,
        },
        500
      );
    }
  });

  return test;
}

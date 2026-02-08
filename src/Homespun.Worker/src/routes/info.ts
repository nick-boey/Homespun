import { Hono } from 'hono';
import type { SessionManager } from '../services/session-manager.js';

export function createInfoRoute(sessionManager: SessionManager) {
  const info = new Hono();

  info.get('/', (c) => {
    const activeSessions = sessionManager.list();
    const hasActive = activeSessions.some((s) => s.status === 'streaming');

    return c.json({
      issueId: process.env.ISSUE_ID || '',
      projectId: process.env.PROJECT_ID || '',
      projectName: process.env.PROJECT_NAME || '',
      status: hasActive ? 'active' : 'idle',
    });
  });

  return info;
}

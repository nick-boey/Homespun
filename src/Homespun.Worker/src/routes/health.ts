import { Hono } from 'hono';
import { captureProcessDiagnostics, formatBytes } from '../utils/diagnostics.js';
import { existsSync } from 'node:fs';
import type { SessionManager } from '../services/session-manager.js';

export function createHealthRoute(sessionManager: SessionManager) {
  const health = new Hono();

  health.get('/', (c) => {
  const diagnostics = captureProcessDiagnostics();

  // Calculate error rate if we track it
  const sessions = sessionManager?.list() || [];
  const activeSessions = sessions.filter(s => s.status === 'streaming').length;
  const idleSessions = sessions.filter(s => s.status === 'idle').length;

  // Check debug log accessibility
  const debugLogPath = '/home/homespun/.claude/debug/claude_sdk_debug.log';
  const debugLogAccessible = existsSync(debugLogPath);

  return c.json({
    status: 'ok',
    timestamp: new Date().toISOString(),
    process: {
      pid: diagnostics.pid,
      uptime: Math.round(diagnostics.uptime),
      uptimeHuman: `${Math.floor(diagnostics.uptime / 60)}m ${Math.round(diagnostics.uptime % 60)}s`,
    },
    memory: {
      rss: formatBytes(diagnostics.memory.rss),
      heapUsed: formatBytes(diagnostics.memory.heapUsed),
      heapTotal: formatBytes(diagnostics.memory.heapTotal),
      external: formatBytes(diagnostics.memory.external),
      percentUsed: Math.round((diagnostics.memory.heapUsed / diagnostics.memory.heapTotal) * 100),
    },
    sessions: {
      total: sessions.length,
      active: activeSessions,
      idle: idleSessions,
    },
    debug: {
      logAccessible: debugLogAccessible,
      logPath: debugLogPath,
    },
  });
});

  return health;
}

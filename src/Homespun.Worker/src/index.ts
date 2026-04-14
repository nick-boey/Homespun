import { Hono } from 'hono';
import { serve } from '@hono/node-server';
import { query } from '@anthropic-ai/claude-agent-sdk';
import { createHealthRoute } from './routes/health.js';
import { createInfoRoute } from './routes/info.js';
import { createSessionsRoute } from './routes/sessions.js';
import { createMiniPromptRoute } from './routes/mini-prompt.js';
import { createTestRoute } from './routes/test.js';
import files from './routes/files.js';
import { SessionManager } from './services/session-manager.js';
import { emitBootInventory } from './services/session-inventory.js';
import { info } from './utils/logger.js';

const app = new Hono();
const sessionManager = new SessionManager();

// Mount all routes under /api
const api = new Hono();
api.route('/health', createHealthRoute(sessionManager));
api.route('/info', createInfoRoute(sessionManager));
api.route('/sessions', createSessionsRoute(sessionManager));
api.route('/mini-prompt', createMiniPromptRoute());
api.route('/files', files);
api.route('/test', createTestRoute(sessionManager));
app.route('/api', api);

const port = parseInt(process.env.PORT || '8080', 10);

info(`Starting Homespun Worker on port ${port}...`);

serve({ fetch: app.fetch, port });

info(`Homespun Worker listening on http://0.0.0.0:${port}`);

// Emit a one-shot boot inventory log so operators can verify the worker's
// available skills/plugins before any session runs (Feature 001, US3 / FR-012).
// Fire-and-forget: must not delay readiness and must not crash on failure.
void emitBootInventory({
  query: query as unknown as Parameters<typeof emitBootInventory>[0]['query'],
  buildOptions: () => ({
    model: 'claude-opus-4-6',
    cwd: process.env.WORKING_DIRECTORY || '/workdir',
    includePartialMessages: false,
    settingSources: ['user', 'project'],
    systemPrompt: { type: 'preset', preset: 'claude_code' },
  }),
});

// Graceful shutdown
async function shutdown() {
  info('Shutting down...');
  await sessionManager.closeAll();
  process.exit(0);
}

process.on('SIGTERM', shutdown);
process.on('SIGINT', shutdown);

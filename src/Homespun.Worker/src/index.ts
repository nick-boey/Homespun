import { Hono } from 'hono';
import { serve } from '@hono/node-server';
import health from './routes/health.js';
import { createInfoRoute } from './routes/info.js';
import { createSessionsRoute } from './routes/sessions.js';
import { createMiniPromptRoute } from './routes/mini-prompt.js';
import { createTestRoute } from './routes/test.js';
import files from './routes/files.js';
import { SessionManager } from './services/session-manager.js';
import { info } from './utils/logger.js';

const app = new Hono();
const sessionManager = new SessionManager();

// Mount all routes under /api
const api = new Hono();
api.route('/health', health);
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

// Graceful shutdown
async function shutdown() {
  info('Shutting down...');
  await sessionManager.closeAll();
  process.exit(0);
}

process.on('SIGTERM', shutdown);
process.on('SIGINT', shutdown);

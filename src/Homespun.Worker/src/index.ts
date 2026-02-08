import { Hono } from 'hono';
import { serve } from '@hono/node-server';
import health from './routes/health.js';
import { createInfoRoute } from './routes/info.js';
import { createSessionsRoute } from './routes/sessions.js';
import files from './routes/files.js';
import { SessionManager } from './services/session-manager.js';

const app = new Hono();
const sessionManager = new SessionManager();

// Mount routes
app.route('/health', health);
app.route('/info', createInfoRoute(sessionManager));
app.route('/sessions', createSessionsRoute(sessionManager));
app.route('/files', files);

const port = parseInt(process.env.PORT || '8080', 10);

console.log(`Starting Homespun Worker on port ${port}...`);

serve({ fetch: app.fetch, port });

console.log(`Homespun Worker listening on http://0.0.0.0:${port}`);

// Graceful shutdown
async function shutdown() {
  console.log('Shutting down...');
  await sessionManager.closeAll();
  process.exit(0);
}

process.on('SIGTERM', shutdown);
process.on('SIGINT', shutdown);

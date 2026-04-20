// IMPORTANT: `./instrumentation` MUST be the very first import so that
// `@opentelemetry/auto-instrumentations-node` can patch Hono, http, and the
// SDK's undici client before any other module binds the un-patched
// versions. Do not reorder.
import './instrumentation.js';

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
import { info } from './utils/otel-logger.js';
import { traceparentMiddleware } from './utils/traceparent-middleware.js';

const app = new Hono();
const sessionManager = new SessionManager();

// Re-instate W3C trace propagation for inbound requests. See
// `./utils/traceparent-middleware.ts` — `@opentelemetry/instrumentation-http`
// does not emit SERVER spans for `@hono/node-server` requests, so without
// this middleware every worker span becomes its own root trace and the
// Aspire dashboard cannot link worker activity to the triggering server
// request. Must be registered before `app.route(...)` so it wraps every
// downstream handler.
app.use('*', traceparentMiddleware);

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

// Bind explicitly to 0.0.0.0 so the Docker bridge can reach the worker
// over IPv4. Node's default when no hostname is passed binds `::` with
// `IPV6_V6ONLY=1` on some kernels, which makes 172.17.0.2:8080 from the
// host time out despite /proc/net/tcp6 showing LISTEN.
serve({ fetch: app.fetch, port, hostname: '0.0.0.0' });

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

// Graceful shutdown. SIGTERM/SIGINT also trigger the SDK shutdown handler in
// `./instrumentation.ts`; that handler calls `process.exit(0)` once telemetry
// is flushed. To avoid racing and cutting off the final OTLP batch, this
// handler only closes sessions and does NOT call `process.exit()` — the
// instrumentation handler owns that.
async function shutdown() {
  info('Shutting down...');
  await sessionManager.closeAll();
}

process.on('SIGTERM', () => {
  void shutdown();
});
process.on('SIGINT', () => {
  void shutdown();
});

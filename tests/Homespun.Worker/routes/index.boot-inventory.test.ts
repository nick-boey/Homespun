/**
 * T031 — integration test for the worker entrypoint's boot inventory
 * emission (Feature 001, US3 / FR-012).
 *
 * Stubs `@anthropic-ai/claude-agent-sdk.query` to return a mock query that
 * yields a canned `system/init` message, stubs `@hono/node-server.serve` so
 * the module does not actually bind a port, then dynamically imports the
 * entrypoint and asserts exactly one `info` log of the form
 * `inventory event=boot sessionId=boot payload={...}`.
 */
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { createSdkInitMessage } from '../helpers/sdk-init-fixture.js';

const logs: { info: string[]; warn: string[]; error: string[] } = {
  info: [],
  warn: [],
  error: [],
};

vi.mock('#src/utils/logger.js', () => ({
  info: vi.fn((m: string) => logs.info.push(m)),
  warn: vi.fn((m: string) => logs.warn.push(m)),
  error: vi.fn((m: string) => logs.error.push(m)),
  debug: vi.fn(),
}));

vi.mock('@hono/node-server', () => ({
  serve: vi.fn(),
}));

const queryFactory = vi.fn(() => {
  const init = createSdkInitMessage();
  const interrupt = vi.fn(async () => {});
  return {
    interrupt,
    [Symbol.asyncIterator]: async function* () {
      yield init;
    },
  };
});

vi.mock('@anthropic-ai/claude-agent-sdk', () => ({
  query: queryFactory,
}));

describe('worker entrypoint boot inventory (T031)', () => {
  beforeEach(() => {
    logs.info.length = 0;
    logs.warn.length = 0;
    logs.error.length = 0;
    queryFactory.mockClear();
    vi.resetModules();
  });

  it('emits exactly one `inventory event=boot sessionId=boot payload={` info log on import', async () => {
    // Act — import the entrypoint. The void emitBootInventory() fires.
    await import('#src/index.js');

    // Poll for up to ~1s for the fire-and-forget boot inventory to land.
    const deadline = Date.now() + 1000;
    while (Date.now() < deadline) {
      const hit = logs.info.some((l) =>
        l.startsWith('inventory event=boot sessionId=boot payload={'),
      );
      if (hit) break;
      await new Promise((r) => setTimeout(r, 10));
    }

    const bootLines = logs.info.filter((l) =>
      l.startsWith('inventory event=boot sessionId=boot payload={'),
    );
    expect(bootLines).toHaveLength(1);
    expect(queryFactory).toHaveBeenCalledTimes(1);
  });
});

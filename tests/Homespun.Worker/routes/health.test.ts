import { vi, beforeEach } from 'vitest';
import { createHealthRoute } from '#src/routes/health.js';
import { SessionManager } from '#src/services/session-manager.js';
import { existsSync } from 'node:fs';

// Mock dependencies
vi.mock('node:fs', () => ({
  existsSync: vi.fn(),
}));

vi.mock('#src/utils/diagnostics.js', () => ({
  captureProcessDiagnostics: vi.fn(),
  formatBytes: vi.fn((bytes: number) => {
    if (bytes >= 1073741824) return `${(bytes / 1073741824).toFixed(2)} GB`;
    if (bytes >= 1048576) return `${(bytes / 1048576).toFixed(2)} MB`;
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(2)} KB`;
    return `${bytes} B`;
  }),
}));

describe('GET /health', () => {
  let sessionManager: SessionManager;
  let healthRoute: any;

  beforeEach(() => {
    vi.clearAllMocks();
    sessionManager = new SessionManager();
    healthRoute = createHealthRoute(sessionManager);
  });

  it('returns 200 with status ok', async () => {
    const mockDiagnostics = {
      pid: 1234,
      uptime: 300,
      memory: {
        rss: 100000000,
        heapUsed: 50000000,
        heapTotal: 80000000,
        external: 1000000,
      },
      cpu: { user: 1000, system: 500 },
      timestamp: '2024-01-01T00:00:00Z',
    };

    const { captureProcessDiagnostics } = await import('#src/utils/diagnostics.js');
    vi.mocked(captureProcessDiagnostics).mockReturnValue(mockDiagnostics);
    vi.mocked(existsSync).mockReturnValue(false);

    const res = await healthRoute.request('/');
    const data = await res.json();

    expect(res.status).toBe(200);
    expect(data).toMatchObject({
      status: 'ok',
      process: {
        pid: 1234,
        uptime: 300,
        uptimeHuman: '5m 0s',
      },
      memory: {
        rss: '95.37 MB',
        heapUsed: '47.68 MB',
        heapTotal: '76.29 MB',
        external: '976.56 KB',
        percentUsed: 63,
      },
      sessions: {
        total: 0,
        active: 0,
        idle: 0,
      },
      debug: {
        logAccessible: false,
        logPath: '/home/homespun/.claude/debug/claude_sdk_debug.log',
      },
    });
  });
});

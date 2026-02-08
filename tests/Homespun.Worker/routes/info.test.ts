import { vi } from 'vitest';
import { createInfoRoute } from '#src/routes/info.js';
import { createMockSessionManager } from '../helpers/mock-session-manager.js';

describe('GET /info', () => {
  beforeEach(() => {
    vi.stubEnv('ISSUE_ID', 'issue-42');
    vi.stubEnv('PROJECT_ID', 'proj-7');
    vi.stubEnv('PROJECT_NAME', 'Homespun');
  });

  it('returns issueId, projectId, projectName from env', async () => {
    const sm = createMockSessionManager();
    sm.list.mockReturnValue([]);
    const app = createInfoRoute(sm as any);

    const res = await app.request('/');
    const body = await res.json();

    expect(body.issueId).toBe('issue-42');
    expect(body.projectId).toBe('proj-7');
    expect(body.projectName).toBe('Homespun');
  });

  it('returns empty strings when env vars are missing', async () => {
    vi.stubEnv('ISSUE_ID', '');
    vi.stubEnv('PROJECT_ID', '');
    vi.stubEnv('PROJECT_NAME', '');

    const sm = createMockSessionManager();
    sm.list.mockReturnValue([]);
    const app = createInfoRoute(sm as any);

    const res = await app.request('/');
    const body = await res.json();

    expect(body.issueId).toBe('');
    expect(body.projectId).toBe('');
    expect(body.projectName).toBe('');
  });

  it('returns status active when there is a streaming session', async () => {
    const sm = createMockSessionManager();
    sm.list.mockReturnValue([{ status: 'streaming' }]);
    const app = createInfoRoute(sm as any);

    const res = await app.request('/');
    const body = await res.json();

    expect(body.status).toBe('active');
  });

  it('returns status idle when no streaming sessions', async () => {
    const sm = createMockSessionManager();
    sm.list.mockReturnValue([{ status: 'idle' }]);
    const app = createInfoRoute(sm as any);

    const res = await app.request('/');
    const body = await res.json();

    expect(body.status).toBe('idle');
  });
});

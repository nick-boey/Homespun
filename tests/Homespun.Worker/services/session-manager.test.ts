import { vi, type Mock } from 'vitest';
import { createMockSDKSession, mockStreamFromMessages, type MockSDKSession } from '../helpers/mock-sdk.js';
import { createAssistantMessage, createResultMessage, createSystemMessage } from '../helpers/test-fixtures.js';
import { collectAsyncGenerator } from '../helpers/async-helpers.js';

// Use vi.hoisted to ensure variables are available during vi.mock factory execution
const { mockCreateSession, mockResumeSession, mockRandomUUID, getMockSession, setMockSession } = vi.hoisted(() => {
  let _mockSession: any = null;
  return {
    mockCreateSession: vi.fn((..._args: any[]) => _mockSession),
    mockResumeSession: vi.fn((..._args: any[]) => _mockSession),
    mockRandomUUID: vi.fn(() => 'test-uuid-1234'),
    getMockSession: () => _mockSession,
    setMockSession: (s: any) => { _mockSession = s; },
  };
});

vi.mock('@anthropic-ai/claude-agent-sdk', () => ({
  unstable_v2_createSession: mockCreateSession,
  unstable_v2_resumeSession: mockResumeSession,
}));

vi.mock('node:crypto', () => ({
  randomUUID: mockRandomUUID,
}));

import { SessionManager, mapPermissionMode } from '#src/services/session-manager.js';

describe('mapPermissionMode()', () => {
  it('maps Default to default', () => {
    expect(mapPermissionMode('Default')).toBe('default');
  });

  it('maps AcceptEdits to acceptEdits', () => {
    expect(mapPermissionMode('AcceptEdits')).toBe('acceptEdits');
  });

  it('maps Plan to plan', () => {
    expect(mapPermissionMode('Plan')).toBe('plan');
  });

  it('maps BypassPermissions to bypassPermissions', () => {
    expect(mapPermissionMode('BypassPermissions')).toBe('bypassPermissions');
  });

  it('returns bypassPermissions for undefined', () => {
    expect(mapPermissionMode(undefined)).toBe('bypassPermissions');
  });

  it('returns bypassPermissions for unknown value', () => {
    expect(mapPermissionMode('SomethingElse')).toBe('bypassPermissions');
  });
});

describe('SessionManager', () => {
  let manager: SessionManager;
  let mockSession: MockSDKSession;

  beforeEach(() => {
    manager = new SessionManager();
    mockSession = createMockSDKSession();
    setMockSession(mockSession);

    // Reset mock implementations after restoreMocks clears them
    mockCreateSession.mockImplementation((..._args: any[]) => getMockSession());
    mockResumeSession.mockImplementation((..._args: any[]) => getMockSession());
    mockRandomUUID.mockImplementation(() => 'test-uuid-1234');

    vi.stubEnv('WORKING_DIRECTORY', '/test/workdir');
    vi.stubEnv('GIT_AUTHOR_NAME', 'Test Author');
    vi.stubEnv('GIT_AUTHOR_EMAIL', 'test@example.com');
    vi.stubEnv('GIT_COMMITTER_NAME', '');
    vi.stubEnv('GIT_COMMITTER_EMAIL', '');
    vi.stubEnv('GITHUB_TOKEN', 'ghp_test123');
    vi.stubEnv('GitHub__Token', '');
    vi.stubEnv('PLAYWRIGHT_BROWSERS_PATH', '');
  });

  describe('create()', () => {
    const baseOpts = {
      prompt: 'Hello agent',
      model: 'claude-sonnet-4-20250514',
      mode: 'Plan',
    };

    it('passes permissionMode plan and allowedTools for plan mode', async () => {
      await manager.create(baseOpts);

      expect(mockCreateSession).toHaveBeenCalledOnce();
      const opts = mockCreateSession.mock.calls[0][0] as Record<string, unknown>;
      expect(opts.permissionMode).toBe('plan');
      expect(opts.allowedTools).toEqual([
        'Read', 'Glob', 'Grep', 'WebFetch', 'WebSearch',
        'Task', 'AskUserQuestion', 'ExitPlanMode',
      ]);
    });

    it('passes permissionMode bypassPermissions for build mode', async () => {
      await manager.create({ ...baseOpts, mode: 'Build' });

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, unknown>;
      expect(opts.permissionMode).toBe('bypassPermissions');
      expect(opts.allowDangerouslySkipPermissions).toBe(true);
    });

    it('calls unstable_v2_resumeSession when resumeSessionId is provided', async () => {
      await manager.create({ ...baseOpts, resumeSessionId: 'conv-abc' });

      expect(mockResumeSession).toHaveBeenCalledOnce();
      expect(mockResumeSession.mock.calls[0][0]).toBe('conv-abc');
      expect(mockCreateSession).not.toHaveBeenCalled();
    });

    it('calls unstable_v2_createSession without resumeSessionId', async () => {
      await manager.create(baseOpts);

      expect(mockCreateSession).toHaveBeenCalledOnce();
      expect(mockResumeSession).not.toHaveBeenCalled();
    });

    it('sends initial prompt via session.send()', async () => {
      await manager.create(baseOpts);

      expect(mockSession.send).toHaveBeenCalledWith('Hello agent');
    });

    it('sets status to streaming after send', async () => {
      const ws = await manager.create(baseOpts);

      expect(ws.status).toBe('streaming');
    });

    it('stores session retrievable by get()', async () => {
      const ws = await manager.create(baseOpts);

      const retrieved = manager.get(ws.id);
      expect(retrieved).toBe(ws);
    });

    it('uses randomUUID() for session ID', async () => {
      const ws = await manager.create(baseOpts);

      expect(ws.id).toBe('test-uuid-1234');
    });

    it('passes append field in systemPrompt option when systemPrompt provided', async () => {
      await manager.create({ ...baseOpts, systemPrompt: 'Be helpful' });

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, unknown>;
      expect(opts.systemPrompt).toEqual({
        type: 'preset',
        preset: 'claude_code',
        append: 'Be helpful',
      });
    });

    it('omits append when no systemPrompt', async () => {
      await manager.create(baseOpts);

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, unknown>;
      expect(opts.systemPrompt).toEqual({
        type: 'preset',
        preset: 'claude_code',
      });
    });

    it('uses workingDirectory param over env var', async () => {
      await manager.create({ ...baseOpts, workingDirectory: '/custom/dir' });

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, unknown>;
      expect(opts.cwd).toBe('/custom/dir');
    });

    it('falls back to WORKING_DIRECTORY env var', async () => {
      await manager.create(baseOpts);

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, unknown>;
      expect(opts.cwd).toBe('/test/workdir');
    });

    it('falls back to /workdir when no env var set', async () => {
      vi.stubEnv('WORKING_DIRECTORY', '');

      await manager.create(baseOpts);

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, unknown>;
      expect(opts.cwd).toBe('/workdir');
    });

    it('uses env var defaults for git identity', async () => {
      await manager.create(baseOpts);

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, any>;
      expect(opts.env.GIT_AUTHOR_NAME).toBe('Test Author');
      expect(opts.env.GIT_AUTHOR_EMAIL).toBe('test@example.com');
    });

    it('defaults git identity to Homespun Bot when env vars not set', async () => {
      vi.stubEnv('GIT_AUTHOR_NAME', '');
      vi.stubEnv('GIT_AUTHOR_EMAIL', '');

      await manager.create(baseOpts);

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, any>;
      expect(opts.env.GIT_AUTHOR_NAME).toBe('Homespun Bot');
      expect(opts.env.GIT_AUTHOR_EMAIL).toBe('homespun@localhost');
    });

    it('resolves GITHUB_TOKEN from env', async () => {
      await manager.create(baseOpts);

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, any>;
      expect(opts.env.GITHUB_TOKEN).toBe('ghp_test123');
      expect(opts.env.GH_TOKEN).toBe('ghp_test123');
    });

    it('falls back to GitHub__Token env var', async () => {
      vi.stubEnv('GITHUB_TOKEN', '');
      vi.stubEnv('GitHub__Token', 'ghp_fallback');

      await manager.create(baseOpts);

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, any>;
      expect(opts.env.GITHUB_TOKEN).toBe('ghp_fallback');
    });

    it('initializes permissionMode to plan for Plan mode', async () => {
      const ws = await manager.create(baseOpts);

      expect(ws.permissionMode).toBe('plan');
    });

    it('initializes permissionMode to bypassPermissions for Build mode', async () => {
      const ws = await manager.create({ ...baseOpts, mode: 'Build' });

      expect(ws.permissionMode).toBe('bypassPermissions');
    });

    it('includes Playwright MCP server config', async () => {
      await manager.create(baseOpts);

      const opts = mockCreateSession.mock.calls[0][0] as Record<string, any>;
      expect(opts.mcpServers.playwright).toMatchObject({
        type: 'stdio',
        command: 'npx',
        args: expect.arrayContaining(['@playwright/mcp@latest']),
      });
    });
  });

  describe('send()', () => {
    it('calls session.send with message and updates lastActivityAt', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      const beforeActivity = ws.lastActivityAt;

      await new Promise((r) => setTimeout(r, 5));

      await manager.send(ws.id, 'follow up');

      expect(mockSession.send).toHaveBeenCalledWith('follow up');
      expect(ws.lastActivityAt.getTime()).toBeGreaterThanOrEqual(beforeActivity.getTime());
    });

    it('sets status to streaming', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      ws.status = 'idle';

      await manager.send(ws.id, 'msg');

      expect(ws.status).toBe('streaming');
    });

    it('updates permissionMode when provided', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Plan' });
      expect(ws.permissionMode).toBe('plan');

      await manager.send(ws.id, 'msg', undefined, 'BypassPermissions');

      expect(ws.permissionMode).toBe('bypassPermissions');
    });

    it('preserves permissionMode when not provided', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      expect(ws.permissionMode).toBe('bypassPermissions');

      await manager.send(ws.id, 'msg');

      expect(ws.permissionMode).toBe('bypassPermissions');
    });

    it('maps AcceptEdits permissionMode correctly', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });

      await manager.send(ws.id, 'msg', undefined, 'AcceptEdits');

      expect(ws.permissionMode).toBe('acceptEdits');
    });

    it('throws for non-existent session', async () => {
      await expect(manager.send('no-such-id', 'msg')).rejects.toThrow(
        'Session no-such-id not found',
      );
    });
  });

  describe('stream()', () => {
    it('yields SDK messages from underlying session stream', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      mockStreamFromMessages(mockSession, [createAssistantMessage(), createResultMessage()]);

      const result = await collectAsyncGenerator(manager.stream(ws.id));

      expect(result).toHaveLength(2);
    });

    it('captures msg.session_id as conversationId', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      mockStreamFromMessages(mockSession, [
        createSystemMessage({ session_id: 'captured-conv-id' }),
      ]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(ws.conversationId).toBe('captured-conv-id');
    });

    it('sets status to idle when stream completes', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      mockStreamFromMessages(mockSession, [createAssistantMessage()]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(ws.status).toBe('idle');
    });

    it('sets status to idle even when stream throws', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      mockSession.stream.mockReturnValue(
        (async function* () {
          throw new Error('boom');
        })(),
      );

      try {
        await collectAsyncGenerator(manager.stream(ws.id));
      } catch {
        // expected
      }

      expect(ws.status).toBe('idle');
    });

    it('calls setPermissionMode with bypassPermissions for Build mode', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      mockStreamFromMessages(mockSession, [createSystemMessage(), createResultMessage()]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(mockSession.query.setPermissionMode).toHaveBeenCalledOnce();
      expect(mockSession.query.setPermissionMode).toHaveBeenCalledWith('bypassPermissions');
    });

    it('calls setPermissionMode with plan for Plan mode', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Plan' });
      mockStreamFromMessages(mockSession, [createSystemMessage(), createResultMessage()]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(mockSession.query.setPermissionMode).toHaveBeenCalledOnce();
      expect(mockSession.query.setPermissionMode).toHaveBeenCalledWith('plan');
    });

    it('calls setPermissionMode only once even with multiple messages', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      mockStreamFromMessages(mockSession, [
        createSystemMessage(),
        createAssistantMessage(),
        createResultMessage(),
      ]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(mockSession.query.setPermissionMode).toHaveBeenCalledOnce();
    });

    it('calls setPermissionMode after first message, not before', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      const callOrder: string[] = [];

      mockSession.query.setPermissionMode.mockImplementation(async () => {
        callOrder.push('setPermissionMode');
      });

      mockSession.stream.mockReturnValue(
        (async function* () {
          callOrder.push('first-message');
          yield createSystemMessage();
          callOrder.push('second-message');
          yield createResultMessage();
        })(),
      );

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(callOrder).toEqual(['first-message', 'setPermissionMode', 'second-message']);
    });

    it('uses per-message permissionMode override (BypassPermissions on Plan session)', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Plan' });
      // Simulate a send() that changed the permissionMode
      await manager.send(ws.id, 'do it', undefined, 'BypassPermissions');
      mockStreamFromMessages(mockSession, [createSystemMessage(), createResultMessage()]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(mockSession.query.setPermissionMode).toHaveBeenCalledWith('bypassPermissions');
    });

    it('uses per-message permissionMode override (AcceptEdits on Build session)', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      await manager.send(ws.id, 'edit carefully', undefined, 'AcceptEdits');
      mockStreamFromMessages(mockSession, [createSystemMessage(), createResultMessage()]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(mockSession.query.setPermissionMode).toHaveBeenCalledWith('acceptEdits');
    });

    it('throws for non-existent session', async () => {
      const gen = manager.stream('no-such-id');
      await expect(gen.next()).rejects.toThrow('Session no-such-id not found');
    });
  });

  describe('close()', () => {
    it('calls session.close(), removes from map, sets status to closed', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });

      await manager.close(ws.id);

      expect(mockSession.close).toHaveBeenCalled();
      expect(ws.status).toBe('closed');
      expect(manager.get(ws.id)).toBeUndefined();
    });

    it('does not throw for non-existent session', async () => {
      await expect(manager.close('no-such-id')).resolves.not.toThrow();
    });
  });

  describe('get()', () => {
    it('returns session when it exists', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });

      expect(manager.get(ws.id)).toBe(ws);
    });

    it('returns undefined when session does not exist', () => {
      expect(manager.get('nonexistent')).toBeUndefined();
    });
  });

  describe('list()', () => {
    it('maps all sessions to SessionInfo[] with correct fields', async () => {
      await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Plan' });

      const list = manager.list();

      expect(list).toHaveLength(1);
      expect(list[0]).toMatchObject({
        sessionId: 'test-uuid-1234',
        mode: 'Plan',
        model: 'claude-sonnet-4-20250514',
        status: 'streaming',
      });
      expect(list[0].createdAt).toBeDefined();
      expect(list[0].lastActivityAt).toBeDefined();
    });

    it('returns empty array when no sessions', () => {
      expect(manager.list()).toEqual([]);
    });
  });

  describe('closeAll()', () => {
    it('closes all sessions', async () => {
      const session1 = createMockSDKSession();
      const session2 = createMockSDKSession();
      let callCount = 0;
      mockCreateSession.mockImplementation((..._args: any[]) => {
        callCount++;
        const s = callCount === 1 ? session1 : session2;
        setMockSession(s);
        return s;
      });

      let uuidCount = 0;
      mockRandomUUID.mockImplementation(() => {
        uuidCount++;
        return `uuid-${uuidCount}`;
      });

      await manager.create({ prompt: 'a', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      await manager.create({ prompt: 'b', model: 'claude-sonnet-4-20250514', mode: 'Build' });

      await manager.closeAll();

      expect(manager.list()).toEqual([]);
    });

    it('works with empty map', async () => {
      await expect(manager.closeAll()).resolves.not.toThrow();
    });
  });
});

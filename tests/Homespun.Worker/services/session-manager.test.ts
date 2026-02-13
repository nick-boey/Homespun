import { vi, type Mock } from 'vitest';
import { createMockQuery, setMockQueryMessages, type MockQuery } from '../helpers/mock-sdk.js';
import { createAssistantMessage, createResultMessage, createSystemMessage } from '../helpers/test-fixtures.js';
import { collectAsyncGenerator } from '../helpers/async-helpers.js';

// Track the canUseTool callback that gets passed to query()
let capturedCanUseTool: ((toolName: string, input: Record<string, unknown>) => Promise<any>) | undefined;

// Use vi.hoisted to ensure variables are available during vi.mock factory execution
const { mockQuery, mockRandomUUID, getMockQuery, setMockQuery, getCapturedCanUseTool, setCapturedCanUseTool } = vi.hoisted(() => {
  let _mockQuery: any = null;
  let _capturedCanUseTool: any = undefined;
  return {
    mockQuery: vi.fn((...args: any[]) => {
      // Capture the canUseTool callback from options
      const options = args[0]?.options;
      if (options?.canUseTool) {
        _capturedCanUseTool = options.canUseTool;
      }
      return _mockQuery;
    }),
    mockRandomUUID: vi.fn(() => 'test-uuid-1234'),
    getMockQuery: () => _mockQuery,
    setMockQuery: (q: any) => { _mockQuery = q; },
    getCapturedCanUseTool: () => _capturedCanUseTool,
    setCapturedCanUseTool: (fn: any) => { _capturedCanUseTool = fn; },
  };
});

vi.mock('@anthropic-ai/claude-agent-sdk', () => ({
  query: mockQuery,
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
  let mockQueryObj: MockQuery;

  beforeEach(() => {
    manager = new SessionManager();
    mockQueryObj = createMockQuery();
    setMockQuery(mockQueryObj);
    setCapturedCanUseTool(undefined);

    // Reset mock implementations after restoreMocks clears them
    mockQuery.mockImplementation((...args: any[]) => {
      const options = args[0]?.options;
      if (options?.canUseTool) {
        setCapturedCanUseTool(options.canUseTool);
      }
      return getMockQuery();
    });
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

    it('calls query() with options including canUseTool callback', async () => {
      await manager.create(baseOpts);

      expect(mockQuery).toHaveBeenCalledOnce();
      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      expect(args.options).toBeDefined();
      expect((args.options as any).canUseTool).toBeDefined();
    });

    it('passes permissionMode plan and allowedTools for plan mode', async () => {
      await manager.create(baseOpts);

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.permissionMode).toBe('plan');
      expect(opts.allowedTools).toEqual([
        'Read', 'Glob', 'Grep', 'WebFetch', 'WebSearch',
        'Task', 'AskUserQuestion', 'ExitPlanMode',
      ]);
    });

    it('passes permissionMode bypassPermissions for build mode', async () => {
      await manager.create({ ...baseOpts, mode: 'Build' });

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.permissionMode).toBe('bypassPermissions');
      expect(opts.allowDangerouslySkipPermissions).toBe(true);
    });

    it('passes resume option when resumeSessionId is provided', async () => {
      await manager.create({ ...baseOpts, resumeSessionId: 'conv-abc' });

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.resume).toBe('conv-abc');
    });

    it('sets status to streaming after create', async () => {
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

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.systemPrompt).toEqual({
        type: 'preset',
        preset: 'claude_code',
        append: 'Be helpful',
      });
    });

    it('omits append when no systemPrompt', async () => {
      await manager.create(baseOpts);

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.systemPrompt).toEqual({
        type: 'preset',
        preset: 'claude_code',
      });
    });

    it('uses workingDirectory param over env var', async () => {
      await manager.create({ ...baseOpts, workingDirectory: '/custom/dir' });

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.cwd).toBe('/custom/dir');
    });

    it('falls back to WORKING_DIRECTORY env var', async () => {
      await manager.create(baseOpts);

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.cwd).toBe('/test/workdir');
    });

    it('falls back to /workdir when no env var set', async () => {
      vi.stubEnv('WORKING_DIRECTORY', '');

      await manager.create(baseOpts);

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.cwd).toBe('/workdir');
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

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, any>;
      expect(opts.mcpServers.playwright).toMatchObject({
        type: 'stdio',
        command: 'npx',
        args: expect.arrayContaining(['@playwright/mcp@latest']),
      });
    });
  });

  describe('send()', () => {
    it('updates lastActivityAt', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      const beforeActivity = ws.lastActivityAt;
      mockQuery.mockClear(); // Clear the create() call

      await new Promise((r) => setTimeout(r, 5));

      await manager.send(ws.id, 'follow up');

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

    it('throws for non-existent session', async () => {
      await expect(manager.send('no-such-id', 'msg')).rejects.toThrow(
        'Session no-such-id not found',
      );
    });
  });

  describe('stream()', () => {
    it('yields messages from the query async generator', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      setMockQueryMessages(mockQueryObj, [createAssistantMessage(), createResultMessage()]);

      const result = await collectAsyncGenerator(manager.stream(ws.id));

      expect(result).toHaveLength(2);
    });

    it('captures msg.session_id as conversationId', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      setMockQueryMessages(mockQueryObj, [
        createSystemMessage({ session_id: 'captured-conv-id' }),
      ]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(ws.conversationId).toBe('captured-conv-id');
    });

    it('sets status to idle when stream completes', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });
      setMockQueryMessages(mockQueryObj, [createAssistantMessage()]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(ws.status).toBe('idle');
    });

    it('throws for non-existent session', async () => {
      const gen = manager.stream('no-such-id');
      await expect(gen.next()).rejects.toThrow('Session no-such-id not found');
    });
  });

  describe('canUseTool callback', () => {
    it('allows non-AskUserQuestion tools immediately', async () => {
      await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });

      const canUseTool = getCapturedCanUseTool();
      expect(canUseTool).toBeDefined();

      const result = await canUseTool('Bash', { command: 'ls' });

      expect(result).toEqual({
        behavior: 'allow',
        updatedInput: { command: 'ls' },
      });
    });

    it('pauses on AskUserQuestion and resumes when resolved', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Plan' });

      const canUseTool = getCapturedCanUseTool();
      expect(canUseTool).toBeDefined();

      // Start the canUseTool call (it will wait)
      const questionInput = {
        questions: [
          {
            question: 'Which framework?',
            header: 'Framework',
            options: [
              { label: 'React', description: 'React framework' },
              { label: 'Vue', description: 'Vue framework' },
            ],
            multiSelect: false,
          },
        ],
      };

      const resultPromise = canUseTool('AskUserQuestion', questionInput);

      // Verify there's a pending question
      expect(manager.hasPendingQuestion(ws.id)).toBe(true);

      // Resolve the question
      const resolved = manager.resolvePendingQuestion(ws.id, { 'Which framework?': 'React' });
      expect(resolved).toBe(true);

      // Check the result
      const result = await resultPromise;
      expect(result).toEqual({
        behavior: 'allow',
        updatedInput: {
          questions: questionInput.questions,
          answers: { 'Which framework?': 'React' },
        },
      });

      // Pending question should be cleared
      expect(manager.hasPendingQuestion(ws.id)).toBe(false);
    });

    it('pauses on ExitPlanMode and resumes when approved', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Plan' });

      const canUseTool = getCapturedCanUseTool();
      const planInput = { plan: 'The plan content' };

      const resultPromise = canUseTool('ExitPlanMode', planInput);

      // Verify there's a pending approval
      expect(manager.hasPendingPlanApproval(ws.id)).toBe(true);

      // Approve the plan
      const resolved = manager.resolvePendingPlanApproval(ws.id, true);
      expect(resolved).toBe(true);

      // Check the result
      const result = await resultPromise;
      expect(result).toEqual({
        behavior: 'allow',
        updatedInput: planInput,
      });

      expect(manager.hasPendingPlanApproval(ws.id)).toBe(false);
    });

    it('denies ExitPlanMode when rejected', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Plan' });

      const canUseTool = getCapturedCanUseTool();
      const planInput = { plan: 'The plan content' };

      const resultPromise = canUseTool('ExitPlanMode', planInput);

      // Reject the plan
      manager.resolvePendingPlanApproval(ws.id, false);

      // Check the result
      const result = await resultPromise;
      expect(result).toEqual({
        behavior: 'deny',
        message: 'User rejected the plan',
      });
    });
  });

  describe('resolvePendingQuestion()', () => {
    it('returns false when no pending question exists', () => {
      const result = manager.resolvePendingQuestion('non-existent', { Q: 'A' });
      expect(result).toBe(false);
    });
  });

  describe('resolvePendingPlanApproval()', () => {
    it('returns false when no pending approval exists', () => {
      const result = manager.resolvePendingPlanApproval('non-existent', true);
      expect(result).toBe(false);
    });
  });

  describe('close()', () => {
    it('removes session from map and sets status to closed', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Build' });

      await manager.close(ws.id);

      expect(ws.status).toBe('closed');
      expect(manager.get(ws.id)).toBeUndefined();
    });

    it('rejects pending questions when session is closed', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'claude-sonnet-4-20250514', mode: 'Plan' });

      const canUseTool = getCapturedCanUseTool();
      const questionInput = {
        questions: [{ question: 'Test?', header: 'Test', options: [], multiSelect: false }],
      };

      const resultPromise = canUseTool('AskUserQuestion', questionInput);

      // Close the session while question is pending
      await manager.close(ws.id);

      // The promise should be rejected
      await expect(resultPromise).rejects.toThrow('Session closed');
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

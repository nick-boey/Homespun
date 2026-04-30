import { vi } from 'vitest';
import { createMockQuery, createBlockingMockQuery, setMockQueryMessages, type MockQuery } from '../helpers/mock-sdk.js';
import { createAssistantMessage, createResultMessage, createSystemMessage } from '../helpers/test-fixtures.js';
import { collectAsyncGenerator } from '../helpers/async-helpers.js';

// Use vi.hoisted to ensure variables are available during vi.mock factory execution
const { mockQuery, mockRandomUUID, getMockQuery, setMockQuery, getCapturedCanUseTool, setCapturedCanUseTool } = vi.hoisted(() => {
  let _mockQuery: any = null;
  let _capturedCanUseTool: any = undefined;
  return {
    mockQuery: vi.fn((...args: any[]) => {
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

import { SessionManager, mapMode } from '#src/services/session-manager.js';

describe('mapMode()', () => {
  it('maps Plan to plan', () => {
    expect(mapMode('Plan')).toBe('plan');
  });

  it('maps Build to bypassPermissions', () => {
    expect(mapMode('Build')).toBe('bypassPermissions');
  });

  it('returns bypassPermissions for undefined', () => {
    expect(mapMode(undefined)).toBe('bypassPermissions');
  });

  it('returns bypassPermissions for unknown value', () => {
    expect(mapMode('SomethingElse')).toBe('bypassPermissions');
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
    mockQuery.mockClear();

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
      model: 'sonnet',
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
        'Task', 'AskUserQuestion', 'ExitPlanMode', 'Skill',
      ]);
    });

    it('passes permissionMode bypassPermissions for build mode', async () => {
      await manager.create({ ...baseOpts, mode: 'Build' });

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.permissionMode).toBe('bypassPermissions');
      expect(opts.allowDangerouslySkipPermissions).toBe(true);
    });

    it('includes "Skill" in allowedTools for build mode so Agent Skills are enabled', async () => {
      await manager.create({ ...baseOpts, mode: 'Build' });

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.allowedTools).toContain('Skill');
    });

    it('build mode allowedTools is a superset of plan mode allowedTools', async () => {
      // Build mode bypasses permissions via allowDangerouslySkipPermissions +
      // canUseTool, but it should still expose at least everything plan mode
      // exposes so the model has consistent tool visibility across modes.
      await manager.create(baseOpts);
      const planOpts = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const planAllowed = (planOpts.options as { allowedTools: string[] }).allowedTools;

      mockQuery.mockClear();
      mockRandomUUID.mockReturnValueOnce('build-session-id');
      await manager.create({ ...baseOpts, mode: 'Build' });
      const buildOpts = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const buildAllowed = (buildOpts.options as { allowedTools: string[] }).allowedTools;

      for (const tool of planAllowed) {
        expect(buildAllowed).toContain(tool);
      }
    });

    it('includes "Skill" in allowedTools for plan mode so Agent Skills are enabled', async () => {
      await manager.create(baseOpts);

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.allowedTools).toContain('Skill');
    });

    it('passes resume option when resumeSessionId is provided', async () => {
      await manager.create({ ...baseOpts, resumeSessionId: 'conv-abc' });

      const args = mockQuery.mock.calls[0][0] as Record<string, unknown>;
      const opts = args.options as Record<string, unknown>;
      expect(opts.resume).toBe('conv-abc');
    });

    it('sets status to streaming after create', async () => {
      setMockQuery(createBlockingMockQuery());
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
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });
      const beforeActivity = ws.lastActivityAt;

      await new Promise((r) => setTimeout(r, 5));

      await manager.send(ws.id, 'follow up');

      expect(ws.lastActivityAt.getTime()).toBeGreaterThanOrEqual(beforeActivity.getTime());
    });

    it('sets status to streaming', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });
      ws.status = 'idle';

      await manager.send(ws.id, 'msg');

      expect(ws.status).toBe('streaming');
    });

    it('updates permissionMode when provided', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });
      expect(ws.permissionMode).toBe('plan');

      await manager.send(ws.id, 'msg', undefined, 'BypassPermissions');

      expect(ws.permissionMode).toBe('bypassPermissions');
    });

    it('preserves permissionMode when not provided', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });
      expect(ws.permissionMode).toBe('bypassPermissions');

      await manager.send(ws.id, 'msg');

      expect(ws.permissionMode).toBe('bypassPermissions');
    });

    it('throws for non-existent session', async () => {
      await expect(manager.send('no-such-id', 'msg')).rejects.toThrow(
        'Session no-such-id not found',
      );
    });

    // After `fix-worker-streaminput-multi-turn`: send() delivers the new user
    // message by pushing into the session's persistent input queue (the same
    // iterable passed to `query({prompt})`). It does NOT call query() again
    // and does NOT call q.streamInput() — the latter would close stdin to
    // the CLI and break subsequent turns.
    it('task 3.2: delivers follow-up by pushing into inputQueue (not streamInput)', async () => {
      setMockQueryMessages(mockQueryObj, [createResultMessage()]);
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      // Drain the first query so a result is observed (forwarder finishes).
      await collectAsyncGenerator(manager.stream(ws.id));

      const pushSpy = vi.spyOn(ws.inputQueue, 'push');
      const streamInputMock = (ws.query as unknown as { streamInput: ReturnType<typeof vi.fn> }).streamInput;
      mockQuery.mockClear();

      await manager.send(ws.id, 'follow up');

      expect(mockQuery).not.toHaveBeenCalled();
      expect(streamInputMock).not.toHaveBeenCalled();
      expect(pushSpy).toHaveBeenCalledOnce();
      const [pushed] = pushSpy.mock.calls[0];
      expect(pushed).toMatchObject({
        type: 'user',
        message: {
          role: 'user',
          content: [{ type: 'text', text: 'follow up' }],
        },
      });
    });

    // Mock-contract test: the mock SDK's streamInput must fail once the
    // initial input iterable has been exhausted, mirroring the real SDK's
    // `ProcessTransport is not ready for writing` behavior. This guards
    // against a future regression that reintroduces the onceIterator +
    // q.streamInput pattern by making the unit tests red.
    it('mock SDK contract: streamInput throws after initial input iterable exhausted', async () => {
      setMockQueryMessages(mockQueryObj, [createResultMessage()]);
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });
      await collectAsyncGenerator(manager.stream(ws.id));

      // Simulate the SDK's endInput() firing after the initial iterable completes.
      (ws.query as unknown as { _simulateInputEnded: () => void })._simulateInputEnded();

      await expect(
        (ws.query as unknown as { streamInput: (x: unknown) => Promise<void> }).streamInput('anything'),
      ).rejects.toThrow(/ProcessTransport is not ready for writing/);
    });
  });

  // Task 3.3: setMode() calls q.setPermissionMode() after a prior `result`.
  describe('setMode() after result', () => {
    it('task 3.3: setMode applies setPermissionMode to the live query', async () => {
      setMockQueryMessages(mockQueryObj, [createResultMessage()]);
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });

      // Drain so that from a client's perspective a result has been received.
      await collectAsyncGenerator(manager.stream(ws.id));

      const setPermissionModeMock = vi.fn().mockResolvedValue(undefined);
      (ws.query as any).setPermissionMode = setPermissionModeMock;

      const ok = await manager.setMode(ws.id, 'Build');
      expect(ok).toBe(true);
      expect(setPermissionModeMock).toHaveBeenCalledWith('bypassPermissions');
      expect(ws.permissionMode).toBe('bypassPermissions');
      expect(ws.mode).toBe('Build');
    });
  });

  // Task 3.4: setModel() updates the session model and is callable after a prior result.
  describe('setModel() after result', () => {
    it('task 3.4: setModel updates model and calls q.setModel', async () => {
      setMockQueryMessages(mockQueryObj, [createResultMessage()]);
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      await collectAsyncGenerator(manager.stream(ws.id));

      const setModelMock = vi.fn().mockResolvedValue(undefined);
      (ws.query as any).setModel = setModelMock;

      const ok = await manager.setModel(ws.id, 'opus');
      expect(ok).toBe(true);
      expect(setModelMock).toHaveBeenCalledWith('opus');
      expect(ws.model).toBe('opus');
    });
  });

  describe('runQueryForwarder()', () => {
    it('transitions to idle on query completion', async () => {
      setMockQueryMessages(mockQueryObj, [
        createAssistantMessage(),
        createResultMessage(),
      ]);
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(ws.status).toBe('idle');
    });

    it('sets status to error on genuine error before result', async () => {
      const errorQuery = createMockQuery();
      (errorQuery as any)[Symbol.asyncIterator] = async function* () {
        throw new Error('SDK crashed');
      };
      setMockQuery(errorQuery);

      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      const messages = await collectAsyncGenerator(manager.stream(ws.id));

      expect(ws.status).toBe('error');
      const errorResult = messages.find((m: any) => m.type === 'result' && m.is_error);
      expect(errorResult).toBeDefined();
    });
  });

  describe('stream()', () => {
    it('yields messages from the query async generator', async () => {
      setMockQueryMessages(mockQueryObj, [createAssistantMessage(), createResultMessage()]);
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      const result = await collectAsyncGenerator(manager.stream(ws.id));

      expect(result).toHaveLength(2);
    });

    it('captures msg.session_id as conversationId', async () => {
      setMockQueryMessages(mockQueryObj, [
        createSystemMessage({ session_id: 'captured-conv-id' }),
      ]);
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(ws.conversationId).toBe('captured-conv-id');
    });

    it('sets status to idle when stream completes', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });
      setMockQueryMessages(mockQueryObj, [createAssistantMessage()]);

      await collectAsyncGenerator(manager.stream(ws.id));

      expect(ws.status).toBe('idle');
    });

    it('throws for non-existent session', async () => {
      const gen = manager.stream('no-such-id');
      await expect(gen.next()).rejects.toThrow('Session no-such-id not found');
    });
  });

  describe('canUseTool callback dispatch', () => {
    it('allows non-special tools immediately', async () => {
      setMockQuery(createBlockingMockQuery());
      await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      const canUseTool = getCapturedCanUseTool();
      expect(canUseTool).toBeDefined();

      const result = await canUseTool('Bash', { command: 'ls' });

      expect(result).toEqual({
        behavior: 'allow',
        updatedInput: { command: 'ls' },
      });
    });

    // Task 3.5: canUseTool("AskUserQuestion", ...) emits question_pending
    // and awaits resolvePending(id, "question", { answers }).
    it('task 3.5: AskUserQuestion emits question_pending and awaits resolvePending', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });

      const canUseTool = getCapturedCanUseTool();

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

      expect(manager.hasPending(ws.id, 'question')).toBe(true);

      // Verify the question_pending control event was pushed to the channel.
      let sawQuestionPending = false;
      const streamGen = manager.stream(ws.id);
      const firstEvent = await streamGen.next();
      if (!firstEvent.done && (firstEvent.value as any).type === 'question_pending') {
        sawQuestionPending = true;
      }
      expect(sawQuestionPending).toBe(true);

      const resolved = manager.resolvePending(ws.id, 'question', {
        answers: { 'Which framework?': 'React' },
      });
      expect(resolved).toBe(true);

      const result = await resultPromise;
      expect(result).toEqual({
        behavior: 'allow',
        updatedInput: {
          questions: questionInput.questions,
          answers: { 'Which framework?': 'React' },
        },
      });

      expect(manager.hasPending(ws.id, 'question')).toBe(false);
      // Silence the still-open async iterator.
      await streamGen.return(undefined);
    });

    // Task 3.6: canUseTool("ExitPlanMode", ...) emits plan_pending and resolves
    // via resolvePending(id, "plan", { approved, keepContext, feedback }).
    it('task 3.6: ExitPlanMode emits plan_pending and resolves via resolvePending', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });

      const canUseTool = getCapturedCanUseTool();
      const planInput = { plan: 'The plan content' };

      const resultPromise = canUseTool('ExitPlanMode', planInput);

      expect(manager.hasPending(ws.id, 'plan')).toBe(true);

      const resolved = manager.resolvePending(ws.id, 'plan', {
        approved: true,
        keepContext: true,
      });
      expect(resolved).toBe(true);

      const result = await resultPromise;
      expect(result).toEqual({
        behavior: 'allow',
        updatedInput: { plan: planInput.plan },
      });

      expect(manager.hasPending(ws.id, 'plan')).toBe(false);
    });

    it('resolvePending(plan, approved without keepContext) denies with interrupt message', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });
      const canUseTool = getCapturedCanUseTool();

      const resultPromise = canUseTool('ExitPlanMode', { plan: 'x' });
      manager.resolvePending(ws.id, 'plan', { approved: true });

      const result = await resultPromise;
      expect(result).toEqual({
        behavior: 'deny',
        message: 'Plan approved. Interrupting to start fresh implementation session.',
      });
    });

    it('resolvePending(plan, rejected) denies with rejection message', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });
      const canUseTool = getCapturedCanUseTool();

      const resultPromise = canUseTool('ExitPlanMode', { plan: 'x' });
      manager.resolvePending(ws.id, 'plan', { approved: false });

      const result = await resultPromise;
      expect(result).toEqual({
        behavior: 'deny',
        message: 'User rejected the plan. Please revise.',
      });
    });

  });

  describe('resolvePending()', () => {
    it('returns false when no pending interaction exists (question)', () => {
      const result = manager.resolvePending('non-existent', 'question', { answers: {} });
      expect(result).toBe(false);
    });

    it('returns false when no pending interaction exists (plan)', () => {
      const result = manager.resolvePending('non-existent', 'plan', { approved: true });
      expect(result).toBe(false);
    });
  });

  describe('hasPending() / getPendingData()', () => {
    it('returns false / undefined before registration', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });

      expect(manager.hasPending(ws.id, 'question')).toBe(false);
      expect(manager.hasPending(ws.id, 'plan')).toBe(false);
      expect(manager.getPendingData(ws.id, 'question')).toBeUndefined();
      expect(manager.getPendingData(ws.id, 'plan')).toBeUndefined();
    });

    it('exposes captured data while a pending interaction is outstanding', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });
      const canUseTool = getCapturedCanUseTool();

      const questionInput = {
        questions: [{ question: 'Q?', header: 'Q', options: [], multiSelect: false }],
      };
      const promise = canUseTool('AskUserQuestion', questionInput);

      const data = manager.getPendingData<{ questions: unknown[] }>(ws.id, 'question');
      expect(data?.questions).toEqual(questionInput.questions);

      manager.resolvePending(ws.id, 'question', { answers: {} });
      await promise;
    });
  });

  describe('close()', () => {
    it('removes session from map and sets status to closed', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      await manager.close(ws.id);

      expect(ws.status).toBe('closed');
      expect(manager.get(ws.id)).toBeUndefined();
    });

    // Task 3.9: close() rejects any outstanding pending interactions for the session.
    it('task 3.9: rejects outstanding pending interactions on close', async () => {
      setMockQuery(createBlockingMockQuery());
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });
      const canUseTool = getCapturedCanUseTool();

      const questionPromise = canUseTool('AskUserQuestion', {
        questions: [{ question: 'Test?', header: 'Test', options: [], multiSelect: false }],
      });
      const planPromise = canUseTool('ExitPlanMode', { plan: 'x' });

      expect(manager.hasPending(ws.id, 'question')).toBe(true);
      expect(manager.hasPending(ws.id, 'plan')).toBe(true);

      await manager.close(ws.id);

      await expect(questionPromise).rejects.toThrow('Session closed');
      await expect(planPromise).rejects.toThrow('Session closed');
    });

    it('does not throw for non-existent session', async () => {
      await expect(manager.close('no-such-id')).resolves.not.toThrow();
    });
  });

  describe('get()', () => {
    it('returns session when it exists', async () => {
      const ws = await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Build' });

      expect(manager.get(ws.id)).toBe(ws);
    });

    it('returns undefined when session does not exist', () => {
      expect(manager.get('nonexistent')).toBeUndefined();
    });
  });

  describe('list()', () => {
    it('maps all sessions to SessionInfo[] with correct fields', async () => {
      setMockQuery(createBlockingMockQuery());
      await manager.create({ prompt: 'init', model: 'sonnet', mode: 'Plan' });

      const list = manager.list();

      expect(list).toHaveLength(1);
      expect(list[0]).toMatchObject({
        sessionId: 'test-uuid-1234',
        mode: 'Plan',
        model: 'sonnet',
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

      await manager.create({ prompt: 'a', model: 'sonnet', mode: 'Build' });
      await manager.create({ prompt: 'b', model: 'sonnet', mode: 'Build' });

      await manager.closeAll();

      expect(manager.list()).toEqual([]);
    });

    it('works with empty map', async () => {
      await expect(manager.closeAll()).resolves.not.toThrow();
    });
  });

  // FI-2: clearContextAndCreate is the entry point used by the server's
  // ClearContextAndStartNew flow. Cover both the existing-session and
  // unknown-session paths so the close-then-create sequencing is asserted.
  describe('clearContextAndCreate() (FI-2)', () => {
    it('closes the existing session and creates a new one', async () => {
      let uuidCount = 0;
      mockRandomUUID.mockImplementation(() => `uuid-${++uuidCount}`);
      setMockQuery(createBlockingMockQuery());

      const old = await manager.create({
        prompt: 'first',
        model: 'sonnet',
        mode: 'Build',
      });

      const result = await manager.clearContextAndCreate(old.id, {
        prompt: 'fresh',
        model: 'sonnet',
        mode: 'Build',
      });

      expect(result.oldSessionId).toBe(old.id);
      expect(result.newSession.id).not.toBe(old.id);
      expect(manager.get(old.id)).toBeUndefined();
      expect(manager.get(result.newSession.id)).toBe(result.newSession);
    });

    it('still creates a new session when the current id is unknown', async () => {
      setMockQuery(createBlockingMockQuery());

      const result = await manager.clearContextAndCreate('unknown', {
        prompt: 'fresh',
        model: 'sonnet',
        mode: 'Build',
      });

      expect(result.oldSessionId).toBe('unknown');
      expect(result.newSession).toBeDefined();
      expect(manager.get(result.newSession.id)).toBe(result.newSession);
    });
  });
});

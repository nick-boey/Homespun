import { vi, beforeEach, describe, it, expect, type Mock } from 'vitest';
import { createMockQuery, setMockQueryMessages } from '../helpers/mock-sdk.js';
import { createAssistantMessage, createResultMessage } from '../helpers/test-fixtures.js';
import { collectAsyncGenerator } from '../helpers/async-helpers.js';
import { isControlEvent } from '../../../src/Homespun.Worker/src/services/session-manager.js';

// Mock the logger module
vi.mock('#src/utils/logger.js', () => ({
  info: vi.fn(),
  error: vi.fn(),
  warn: vi.fn(),
  debug: vi.fn(),
}));

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
    mockRandomUUID: vi.fn(() => 'test-session-123'),
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

import { SessionManager } from '#src/services/session-manager.js';
import { info, error, warn, debug } from '#src/utils/logger.js';

describe('SessionManager Logging', () => {
  let sessionManager: SessionManager;
  let mockQueryInstance: any;

  beforeEach(() => {
    vi.clearAllMocks();
    setCapturedCanUseTool(undefined);
    sessionManager = new SessionManager();
    mockQueryInstance = createMockQuery();
    setMockQuery(mockQueryInstance);
  });

  describe('Status transition logging', () => {
    it('logs status change from idle to streaming when session is created', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Hello'),
        createResultMessage(),
      ]);

      // Act
      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session status changed: idle → streaming (sessionId: test-session-123, reason: session_created)')
      );
    });

    it('logs status change from idle to streaming when sending a message', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Initial response'),
        createResultMessage(),
      ]);

      const session = await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      // Clear previous logs
      vi.clearAllMocks();

      // Act
      await sessionManager.send('test-session-123', 'Follow-up message');

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session status changed: streaming → streaming (sessionId: test-session-123, reason: message_sent)')
      );
    });

    it('logs status change from streaming to idle when stream completes', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Response'),
        createResultMessage(),
      ]);

      const session = await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      // Clear previous logs
      vi.clearAllMocks();

      // Act
      const events = await collectAsyncGenerator(sessionManager.stream('test-session-123'));

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session status changed: streaming → idle (sessionId: test-session-123, reason: stream_complete)')
      );
    });

    it('logs status change from idle to closed when closing session', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Response'),
        createResultMessage(),
      ]);

      const session = await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      // Clear previous logs
      vi.clearAllMocks();

      // Act
      await sessionManager.close('test-session-123');

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session status changed: streaming → closed (sessionId: test-session-123, reason: session_closed)')
      );
    });
  });

  describe('Pending state logging', () => {
    it('logs when entering pending question state', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Let me ask you something'),
        createResultMessage(),
      ]);

      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      const canUseTool = getCapturedCanUseTool();
      expect(canUseTool).toBeDefined();

      // Clear previous logs
      vi.clearAllMocks();

      // Act - simulate AskUserQuestion tool
      const promise = canUseTool!('AskUserQuestion', {
        questions: [
          {
            question: 'What is your preference?',
            header: 'Preference',
            options: [
              { label: 'Option A', description: 'First option' },
              { label: 'Option B', description: 'Second option' },
            ],
          },
        ],
      });

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session entering pending question state (sessionId: test-session-123, questionCount: 1)')
      );

      // Clean up
      sessionManager.resolvePendingQuestion('test-session-123', { '0': 'Option A' });
      await promise;
    });

    it('logs when exiting pending question state', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Let me ask you something'),
        createResultMessage(),
      ]);

      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      const canUseTool = getCapturedCanUseTool();
      const promise = canUseTool!('AskUserQuestion', {
        questions: [{ question: 'Test?', header: 'Test', options: [] }],
      });

      // Clear previous logs
      vi.clearAllMocks();

      // Act
      sessionManager.resolvePendingQuestion('test-session-123', { '0': 'Answer' });
      await promise;

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session exiting pending question state (sessionId: test-session-123, resolved: true)')
      );
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session status changed: streaming → streaming (sessionId: test-session-123, reason: resuming_after_question)')
      );
    });

    it('logs when entering pending plan approval state', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Here is my plan'),
        createResultMessage(),
      ]);

      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Plan',
      });

      const canUseTool = getCapturedCanUseTool();
      expect(canUseTool).toBeDefined();

      // Clear previous logs
      vi.clearAllMocks();

      // Act - simulate ExitPlanMode tool
      const promise = canUseTool!('ExitPlanMode', {
        plan: 'My implementation plan',
      });

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session entering pending plan approval state (sessionId: test-session-123)')
      );

      // Clean up
      sessionManager.resolvePendingPlanApproval('test-session-123', true, true);
      await promise;
    });

    it('logs when exiting pending plan approval state', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Here is my plan'),
        createResultMessage(),
      ]);

      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Plan',
      });

      const canUseTool = getCapturedCanUseTool();
      const promise = canUseTool!('ExitPlanMode', {
        plan: 'My implementation plan',
      });

      // Clear previous logs
      vi.clearAllMocks();

      // Act
      sessionManager.resolvePendingPlanApproval('test-session-123', true, true);
      await promise;

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session exiting pending plan approval state (sessionId: test-session-123, approved: true)')
      );
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Session status changed: streaming → streaming (sessionId: test-session-123, reason: resuming_after_plan_approval)')
      );
    });

    it('warns when closing session with pending question', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Let me ask you something'),
        createResultMessage(),
      ]);

      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      const canUseTool = getCapturedCanUseTool();
      // Start pending question but don't resolve it
      const questionPromise = canUseTool!('AskUserQuestion', {
        questions: [{ question: 'Test?', header: 'Test', options: [] }],
      }).catch(() => {}); // Catch to prevent unhandled rejection

      // Clear previous logs
      vi.clearAllMocks();

      // Act
      await sessionManager.close('test-session-123');

      // Assert
      expect(warn).toHaveBeenCalledWith(
        expect.stringContaining('Session closed with pending question (sessionId: test-session-123)')
      );
    });

    it('warns when closing session with pending plan approval', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Here is my plan'),
        createResultMessage(),
      ]);

      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Plan',
      });

      const canUseTool = getCapturedCanUseTool();
      // Start pending plan but don't resolve it
      const planPromise = canUseTool!('ExitPlanMode', {
        plan: 'My implementation plan',
      }).catch(() => {}); // Catch to prevent unhandled rejection

      // Clear previous logs
      vi.clearAllMocks();

      // Act
      await sessionManager.close('test-session-123');

      // Assert
      expect(warn).toHaveBeenCalledWith(
        expect.stringContaining('Session closed with pending plan approval (sessionId: test-session-123)')
      );
    });
  });

  describe('Query processing logging', () => {
    it('logs query processing start and completion', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Response 1'),
        createAssistantMessage('Response 2'),
        createResultMessage(),
      ]);

      // Act
      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      // Wait for background task to complete
      await collectAsyncGenerator(sessionManager.stream('test-session-123'));

      // Assert
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Query processing started (sessionId: test-session-123)')
      );
      expect(info).toHaveBeenCalledWith(
        expect.stringContaining('Query processing completed (sessionId: test-session-123, messageCount: 3)')
      );
    });
  });

  describe('Control event detail logging', () => {
    it('logs control event details for questions', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Let me ask you something'),
        createResultMessage(),
      ]);

      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Build',
      });

      const canUseTool = getCapturedCanUseTool();

      // Clear previous logs
      vi.clearAllMocks();

      // Act - simulate AskUserQuestion tool
      const promise = canUseTool!('AskUserQuestion', {
        questions: [
          { question: 'Q1?', header: 'Q1', options: [] },
          { question: 'Q2?', header: 'Q2', options: [] },
        ],
      });

      // Stream to trigger the control event logging
      const generator = sessionManager.stream('test-session-123');
      // Need to consume events until we get the control event
      let foundControlEvent = false;
      for await (const event of generator) {
        if (isControlEvent(event) && event.type === 'question_pending') {
          foundControlEvent = true;
          break;
        }
      }

      // Assert
      expect(foundControlEvent).toBe(true);
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('Control event details: 2 questions pending')
      );

      // Clean up
      sessionManager.resolvePendingQuestion('test-session-123', { '0': 'A1', '1': 'A2' });
      await promise;
    });

    it('logs control event details for plan approval', async () => {
      // Arrange
      setMockQueryMessages(mockQueryInstance, [
        createAssistantMessage('Here is my plan'),
        createResultMessage(),
      ]);

      await sessionManager.create({
        prompt: 'Test prompt',
        model: 'claude-3-opus-20240229',
        mode: 'Plan',
      });

      const canUseTool = getCapturedCanUseTool();

      // Clear previous logs
      vi.clearAllMocks();

      // Act - simulate ExitPlanMode tool
      const promise = canUseTool!('ExitPlanMode', {
        plan: 'My implementation plan',
      });

      // Stream to trigger the control event logging
      const generator = sessionManager.stream('test-session-123');
      // Need to consume events until we get the control event
      let foundControlEvent = false;
      for await (const event of generator) {
        if (isControlEvent(event) && event.type === 'plan_pending') {
          foundControlEvent = true;
          break;
        }
      }

      // Assert
      expect(foundControlEvent).toBe(true);
      expect(debug).toHaveBeenCalledWith(
        expect.stringContaining('Control event details: plan approval pending')
      );

      // Clean up
      sessionManager.resolvePendingPlanApproval('test-session-123', true, true);
      await promise;
    });
  });
});
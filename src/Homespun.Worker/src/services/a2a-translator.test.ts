import { describe, it, expect, beforeEach } from 'vitest';
import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';
import {
  createInitialTask,
  createWorkingStatus,
  translateSdkMessage,
  translateResultToStatus,
  translateControlEvent,
  createErrorStatus,
  getEventKind,
  type TranslationContext,
} from './a2a-translator.js';
import type { ControlEvent } from './session-manager.js';

describe('A2A Translator', () => {
  let ctx: TranslationContext;

  beforeEach(() => {
    ctx = {
      taskId: 'test-task-123',
      contextId: 'test-context-456',
    };
  });

  describe('createInitialTask', () => {
    it('creates a task with submitted state', () => {
      const task = createInitialTask('task-1', 'context-1');

      expect(task.kind).toBe('task');
      expect(task.id).toBe('task-1');
      expect(task.contextId).toBe('context-1');
      expect(task.status.state).toBe('submitted');
      expect(task.status.timestamp).toBeDefined();
    });
  });

  describe('createWorkingStatus', () => {
    it('creates a status update with working state', () => {
      const status = createWorkingStatus(ctx);

      expect(status.kind).toBe('status-update');
      expect(status.taskId).toBe('test-task-123');
      expect(status.contextId).toBe('test-context-456');
      expect(status.status.state).toBe('working');
      expect(status.final).toBe(false);
    });
  });

  describe('translateSdkMessage', () => {
    it('translates assistant message to A2A message with agent role', () => {
      const sdkMsg: SDKMessage = {
        type: 'assistant',
        session_id: 'session-1',
        message: {
          role: 'assistant',
          content: [{ type: 'text', text: 'Hello, world!' }],
        },
      } as any;

      const a2aMsg = translateSdkMessage(sdkMsg, ctx);

      expect(a2aMsg).not.toBeNull();
      expect(a2aMsg!.kind).toBe('message');
      expect(a2aMsg!.role).toBe('agent');
      expect(a2aMsg!.parts).toHaveLength(1);
      expect(a2aMsg!.parts[0].kind).toBe('text');
      expect((a2aMsg!.parts[0] as any).text).toBe('Hello, world!');
    });

    it('translates user message to A2A message with user role', () => {
      const sdkMsg: SDKMessage = {
        type: 'user',
        session_id: 'session-1',
        message: {
          role: 'user',
          content: [{ type: 'text', text: 'Hello from user' }],
        },
      } as any;

      const a2aMsg = translateSdkMessage(sdkMsg, ctx);

      expect(a2aMsg).not.toBeNull();
      expect(a2aMsg!.role).toBe('user');
      expect(a2aMsg!.parts[0].kind).toBe('text');
    });

    it('translates tool_use content to A2A DataPart', () => {
      const sdkMsg: SDKMessage = {
        type: 'assistant',
        session_id: 'session-1',
        message: {
          role: 'assistant',
          content: [{
            type: 'tool_use',
            id: 'tool-1',
            name: 'Read',
            input: { file_path: '/test.txt' },
          }],
        },
      } as any;

      const a2aMsg = translateSdkMessage(sdkMsg, ctx);

      expect(a2aMsg).not.toBeNull();
      expect(a2aMsg!.parts).toHaveLength(1);
      expect(a2aMsg!.parts[0].kind).toBe('data');
      const dataPart = a2aMsg!.parts[0] as any;
      expect(dataPart.data.toolName).toBe('Read');
      expect(dataPart.data.toolUseId).toBe('tool-1');
    });

    it('translates thinking content to A2A DataPart', () => {
      const sdkMsg: SDKMessage = {
        type: 'assistant',
        session_id: 'session-1',
        message: {
          role: 'assistant',
          content: [{
            type: 'thinking',
            thinking: 'Let me think about this...',
          }],
        },
      } as any;

      const a2aMsg = translateSdkMessage(sdkMsg, ctx);

      expect(a2aMsg).not.toBeNull();
      expect(a2aMsg!.parts).toHaveLength(1);
      expect(a2aMsg!.parts[0].kind).toBe('data');
      const dataPart = a2aMsg!.parts[0] as any;
      expect(dataPart.data.thinking).toBe('Let me think about this...');
    });

    it('returns null for result messages', () => {
      const sdkMsg: SDKMessage = {
        type: 'result',
        session_id: 'session-1',
        subtype: 'success',
        is_error: false,
      } as any;

      const a2aMsg = translateSdkMessage(sdkMsg, ctx);

      expect(a2aMsg).toBeNull();
    });

    it('translates system message to A2A message with system data', () => {
      const sdkMsg: SDKMessage = {
        type: 'system',
        session_id: 'session-1',
        subtype: 'init',
        model: 'sonnet',
        permissionMode: 'bypassPermissions',
      } as any;

      const a2aMsg = translateSdkMessage(sdkMsg, ctx);

      expect(a2aMsg).not.toBeNull();
      expect(a2aMsg!.role).toBe('agent');
      expect(a2aMsg!.parts).toHaveLength(1);
      expect(a2aMsg!.parts[0].kind).toBe('data');
    });
  });

  describe('translateResultToStatus', () => {
    it('translates success result to completed status', () => {
      const sdkMsg: SDKMessage = {
        type: 'result',
        session_id: 'session-1',
        subtype: 'success',
        is_error: false,
        result: 'Task completed successfully',
        duration_ms: 1000,
        duration_api_ms: 800,
        num_turns: 5,
        total_cost_usd: 0.05,
      } as any;

      const status = translateResultToStatus(sdkMsg, ctx);

      expect(status.kind).toBe('status-update');
      expect(status.status.state).toBe('completed');
      expect(status.final).toBe(true);
      expect(status.taskId).toBe('test-task-123');
    });

    it('translates error result to failed status', () => {
      const sdkMsg: SDKMessage = {
        type: 'result',
        session_id: 'session-1',
        subtype: 'error_during_execution',
        is_error: true,
        result: 'Something went wrong',
        errors: ['Error 1', 'Error 2'],
      } as any;

      const status = translateResultToStatus(sdkMsg, ctx);

      expect(status.kind).toBe('status-update');
      expect(status.status.state).toBe('failed');
      expect(status.final).toBe(true);
    });
  });

  describe('translateControlEvent', () => {
    it('translates question_pending to input-required status', () => {
      const event: ControlEvent = {
        type: 'question_pending',
        data: {
          questions: [{
            question: 'What color?',
            header: 'Color',
            options: [
              { label: 'Red', description: 'The color red' },
              { label: 'Blue', description: 'The color blue' },
            ],
            multiSelect: false,
          }],
        },
      };

      const status = translateControlEvent(event, ctx);

      expect(status.kind).toBe('status-update');
      expect(status.status.state).toBe('input-required');
      expect(status.final).toBe(false);
      expect((status.metadata as any)?.inputType).toBe('question');
    });

    it('translates plan_pending to input-required status', () => {
      const event: ControlEvent = {
        type: 'plan_pending',
        data: {
          plan: '# Implementation Plan\n\n1. Do thing\n2. Do another thing',
        },
      };

      const status = translateControlEvent(event, ctx);

      expect(status.kind).toBe('status-update');
      expect(status.status.state).toBe('input-required');
      expect(status.final).toBe(false);
      expect((status.metadata as any)?.inputType).toBe('plan-approval');
    });
  });

  describe('createErrorStatus', () => {
    it('creates a failed status with error message', () => {
      const status = createErrorStatus(ctx, 'Something went wrong', 'TEST_ERROR');

      expect(status.kind).toBe('status-update');
      expect(status.status.state).toBe('failed');
      expect(status.final).toBe(true);
      expect((status.metadata as any)?.errorCode).toBe('TEST_ERROR');
    });
  });

  describe('getEventKind', () => {
    it('returns status-update for question_pending control events', () => {
      const event: ControlEvent = {
        type: 'question_pending',
        data: { questions: [] },
      };

      expect(getEventKind(event)).toBe('status-update');
    });

    it('returns status-update for plan_pending control events', () => {
      const event: ControlEvent = {
        type: 'plan_pending',
        data: { plan: '' },
      };

      expect(getEventKind(event)).toBe('status-update');
    });

    it('returns status-update for result SDK messages', () => {
      const sdkMsg: SDKMessage = {
        type: 'result',
        session_id: 'session-1',
      } as any;

      expect(getEventKind(sdkMsg)).toBe('status-update');
    });

    it('returns message for assistant SDK messages', () => {
      const sdkMsg: SDKMessage = {
        type: 'assistant',
        session_id: 'session-1',
      } as any;

      expect(getEventKind(sdkMsg)).toBe('message');
    });
  });
});

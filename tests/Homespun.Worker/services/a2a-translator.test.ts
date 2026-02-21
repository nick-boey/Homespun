import { describe, it, expect } from 'vitest';
import {
  createInitialTask,
  createWorkingStatus,
  translateSdkMessage,
  translateResultToStatus,
  translateControlEvent,
  createErrorStatus,
  type TranslationContext,
} from '#src/services/a2a-translator.js';
import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';

const ctx: TranslationContext = {
  taskId: 'task-123',
  contextId: 'ctx-456',
};

describe('createInitialTask', () => {
  it('creates A2A Task with state submitted', () => {
    const task = createInitialTask('task-1', 'ctx-1');

    expect(task.kind).toBe('task');
    expect(task.id).toBe('task-1');
    expect(task.contextId).toBe('ctx-1');
    expect(task.status.state).toBe('submitted');
    expect(task.status.timestamp).toBeDefined();
  });
});

describe('createWorkingStatus', () => {
  it('creates TaskStatusUpdateEvent with state working', () => {
    const status = createWorkingStatus(ctx);

    expect(status.kind).toBe('status-update');
    expect(status.taskId).toBe('task-123');
    expect(status.contextId).toBe('ctx-456');
    expect(status.status.state).toBe('working');
    expect(status.final).toBe(false);
  });
});

describe('translateSdkMessage', () => {
  it('translates assistant message to A2A Message with role agent', () => {
    const sdkMsg: SDKMessage = {
      type: 'assistant',
      session_id: 'sess-1',
      message: {
        role: 'assistant',
        content: [{ type: 'text', text: 'Hello world' }],
      },
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);

    expect(a2aMsg).not.toBeNull();
    expect(a2aMsg!.kind).toBe('message');
    expect(a2aMsg!.role).toBe('agent');
    expect(a2aMsg!.contextId).toBe('ctx-456');
    expect(a2aMsg!.taskId).toBe('task-123');
    expect(a2aMsg!.parts).toHaveLength(1);
    expect(a2aMsg!.parts[0].kind).toBe('text');
  });

  it('translates user message to A2A Message with role user', () => {
    const sdkMsg: SDKMessage = {
      type: 'user',
      session_id: 'sess-1',
      message: {
        role: 'user',
        content: [{ type: 'text', text: 'Help me' }],
      },
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);

    expect(a2aMsg).not.toBeNull();
    expect(a2aMsg!.kind).toBe('message');
    expect(a2aMsg!.role).toBe('user');
  });

  it('translates tool_use content block to DataPart', () => {
    const sdkMsg: SDKMessage = {
      type: 'assistant',
      session_id: 'sess-1',
      message: {
        role: 'assistant',
        content: [
          {
            type: 'tool_use',
            id: 'tool-123',
            name: 'Read',
            input: { path: '/test.txt' },
          },
        ],
      },
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);

    expect(a2aMsg!.parts).toHaveLength(1);
    expect(a2aMsg!.parts[0].kind).toBe('data');
    const dataPart = a2aMsg!.parts[0] as { kind: 'data'; data: any; metadata?: any };
    expect(dataPart.data.toolName).toBe('Read');
    expect(dataPart.data.toolUseId).toBe('tool-123');
  });

  it('translates thinking block to DataPart', () => {
    const sdkMsg: SDKMessage = {
      type: 'assistant',
      session_id: 'sess-1',
      message: {
        role: 'assistant',
        content: [{ type: 'thinking', thinking: 'Analyzing...' }],
      },
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);

    expect(a2aMsg!.parts).toHaveLength(1);
    expect(a2aMsg!.parts[0].kind).toBe('data');
    const dataPart = a2aMsg!.parts[0] as { kind: 'data'; data: any; metadata?: any };
    expect(dataPart.data.thinking).toBe('Analyzing...');
  });

  it('returns null for result message type', () => {
    const sdkMsg: SDKMessage = {
      type: 'result',
      session_id: 'sess-1',
      result: 'done',
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);
    expect(a2aMsg).toBeNull();
  });

  it('translates system message to A2A Message with DataPart', () => {
    const sdkMsg: SDKMessage = {
      type: 'system',
      session_id: 'sess-1',
      subtype: 'init',
      model: 'claude-3-opus',
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);

    expect(a2aMsg).not.toBeNull();
    expect(a2aMsg!.kind).toBe('message');
    expect(a2aMsg!.role).toBe('agent');
    expect(a2aMsg!.parts).toHaveLength(1);
    expect(a2aMsg!.parts[0].kind).toBe('data');
  });
});

describe('translateResultToStatus', () => {
  it('translates success result to completed status', () => {
    const sdkMsg: SDKMessage = {
      type: 'result',
      session_id: 'sess-1',
      subtype: 'success',
      is_error: false,
      result: 'Task completed successfully',
      duration_ms: 1000,
      duration_api_ms: 500,
      num_turns: 3,
      total_cost_usd: 0.05,
    } as SDKMessage;

    const status = translateResultToStatus(sdkMsg, ctx);

    expect(status.kind).toBe('status-update');
    expect(status.status.state).toBe('completed');
    expect(status.final).toBe(true);
    expect(status.taskId).toBe('task-123');
    expect(status.contextId).toBe('ctx-456');
  });

  it('translates error result to failed status', () => {
    const sdkMsg: SDKMessage = {
      type: 'result',
      session_id: 'sess-1',
      subtype: 'error_during_execution',
      is_error: true,
      result: 'Something went wrong',
      errors: ['Error 1', 'Error 2'],
    } as SDKMessage;

    const status = translateResultToStatus(sdkMsg, ctx);

    expect(status.status.state).toBe('failed');
    expect(status.final).toBe(true);
  });
});

describe('translateControlEvent', () => {
  it('translates question_pending to input-required status', () => {
    const event = {
      type: 'question_pending' as const,
      data: {
        questions: [
          {
            question: 'What framework?',
            header: 'Framework',
            options: [
              { label: 'React', description: 'React library' },
              { label: 'Vue', description: 'Vue framework' },
            ],
            multiSelect: false,
          },
        ],
      },
    };

    const status = translateControlEvent(event, ctx);

    expect(status.kind).toBe('status-update');
    expect(status.status.state).toBe('input-required');
    expect(status.final).toBe(false);
    expect(status.metadata).toMatchObject({ inputType: 'question' });
  });

  it('translates plan_pending to input-required status with plan-approval type', () => {
    const event = {
      type: 'plan_pending' as const,
      data: {
        plan: '## Implementation Plan\n1. Do this\n2. Do that',
      },
    };

    const status = translateControlEvent(event, ctx);

    expect(status.status.state).toBe('input-required');
    expect(status.metadata).toMatchObject({ inputType: 'plan-approval' });
  });
});

describe('createErrorStatus', () => {
  it('creates failed status with error message', () => {
    const status = createErrorStatus(ctx, 'Connection lost', 'CONNECTION_ERROR');

    expect(status.kind).toBe('status-update');
    expect(status.status.state).toBe('failed');
    expect(status.final).toBe(true);
    expect(status.metadata).toMatchObject({ errorCode: 'CONNECTION_ERROR' });
  });
});

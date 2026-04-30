import { describe, it, expect } from 'vitest';
import {
  createInitialTask,
  createWorkingStatus,
  translateSdkMessage,
  translateResultToStatus,
  translateControlEvent,
  createErrorStatus,
  getEventKind,
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
      model: 'opus',
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

// FI-2: a2a-translator coverage gap fill — exercise the suppression rules for
// `AskUserQuestion` / `ExitPlanMode` (the design D6 "the suppression rules
// in `a2a-translator.ts`" callout) and the `tool_result` + unknown-block
// branches under `translateContentBlocks`, plus the `getEventKind` classifier.

describe('translateSdkMessage — interactive tool suppression (FI-2)', () => {
  it('drops AskUserQuestion tool_use blocks at the translator boundary', () => {
    const sdkMsg: SDKMessage = {
      type: 'assistant',
      session_id: 'sess-1',
      message: {
        role: 'assistant',
        content: [
          { type: 'tool_use', id: 'q-1', name: 'AskUserQuestion', input: {} },
        ],
      },
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);

    expect(a2aMsg).not.toBeNull();
    expect(a2aMsg!.parts).toHaveLength(0);
  });

  it('drops the paired tool_result for a suppressed AskUserQuestion call', () => {
    const turnCtx: TranslationContext = {
      taskId: 't1',
      contextId: 'c1',
      suppressedToolUseIds: new Set<string>(),
    };

    const askMsg: SDKMessage = {
      type: 'assistant',
      session_id: 's',
      message: {
        role: 'assistant',
        content: [
          { type: 'tool_use', id: 'tool-suppressed', name: 'ExitPlanMode', input: {} },
        ],
      },
    } as SDKMessage;
    translateSdkMessage(askMsg, turnCtx);
    expect(turnCtx.suppressedToolUseIds!.has('tool-suppressed')).toBe(true);

    const resultMsg: SDKMessage = {
      type: 'user',
      session_id: 's',
      message: {
        role: 'user',
        content: [
          { type: 'tool_result', tool_use_id: 'tool-suppressed', content: 'x', is_error: false },
        ],
      },
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(resultMsg, turnCtx);
    expect(a2aMsg).not.toBeNull();
    expect(a2aMsg!.parts).toHaveLength(0);
  });

  it('emits non-suppressed tool_result blocks as data parts with kind=tool_result', () => {
    const sdkMsg: SDKMessage = {
      type: 'user',
      session_id: 's',
      message: {
        role: 'user',
        content: [
          { type: 'tool_result', tool_use_id: 'bash-1', content: 'ok', is_error: false },
        ],
      },
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);

    expect(a2aMsg!.parts).toHaveLength(1);
    expect(a2aMsg!.parts[0].kind).toBe('data');
    const dataPart = a2aMsg!.parts[0] as { kind: 'data'; data: any; metadata?: any };
    expect(dataPart.data.toolUseId).toBe('bash-1');
    expect(dataPart.data.content).toBe('ok');
    expect(dataPart.data.isError).toBe(false);
    expect(dataPart.metadata?.kind).toBe('tool_result');
  });

  it('preserves an unknown content block as data part with kind=unknown_block', () => {
    const sdkMsg: SDKMessage = {
      type: 'assistant',
      session_id: 's',
      message: {
        role: 'assistant',
        content: [
          { type: 'unrecognized_kind', extra: 'data' },
        ],
      },
    } as SDKMessage;

    const a2aMsg = translateSdkMessage(sdkMsg, ctx);

    expect(a2aMsg!.parts).toHaveLength(1);
    expect(a2aMsg!.parts[0].kind).toBe('data');
    const dataPart = a2aMsg!.parts[0] as { kind: 'data'; data: any; metadata?: any };
    expect(dataPart.metadata?.kind).toBe('unknown_block');
  });
});

describe('getEventKind (FI-2)', () => {
  it('classifies question_pending control events as status-update', () => {
    expect(
      getEventKind({ type: 'question_pending', data: { questions: [] } } as any),
    ).toBe('status-update');
  });

  it('classifies plan_pending control events as status-update', () => {
    expect(getEventKind({ type: 'plan_pending', data: { plan: '' } } as any)).toBe(
      'status-update',
    );
  });

  it('classifies status_resumed control events as status-update', () => {
    expect(getEventKind({ type: 'status_resumed', data: {} } as any)).toBe('status-update');
  });

  it('classifies SDK result messages as status-update', () => {
    expect(getEventKind({ type: 'result', session_id: 's' } as SDKMessage)).toBe(
      'status-update',
    );
  });

  it('classifies SDK assistant messages as message', () => {
    expect(
      getEventKind({
        type: 'assistant',
        session_id: 's',
        message: { role: 'assistant', content: [] },
      } as SDKMessage),
    ).toBe('message');
  });

  it('classifies SDK user messages as message', () => {
    expect(
      getEventKind({
        type: 'user',
        session_id: 's',
        message: { role: 'user', content: [] },
      } as SDKMessage),
    ).toBe('message');
  });
});

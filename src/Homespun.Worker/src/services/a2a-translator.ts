/**
 * A2A Event Translator
 *
 * Translates Claude Agent SDK events to A2A protocol-compliant events.
 * Maps SDK message types to A2A Messages, Tasks, and TaskStatusUpdateEvents.
 */

import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';
import type {
  Task,
  Message,
  Part,
  TaskState,
  TaskStatusUpdateEvent,
  HomespunMessageMetadata,
  InputRequiredMetadata,
  QuestionData,
} from '../types/a2a.js';
import {
  createTask,
  createMessage,
  createTextPart,
  createDataPart,
  createTaskStatusUpdate,
  A2A_MIME_TYPES,
} from '../types/a2a.js';
import type { ControlEvent } from './session-manager.js';
import { randomUUID } from 'node:crypto';

/**
 * Translation context containing task and context IDs
 */
export interface TranslationContext {
  taskId: string;
  contextId: string;
}

/**
 * Creates the initial A2A Task event when a session starts.
 * Maps to the 'session_started' event in the old format.
 */
export function createInitialTask(
  taskId: string,
  contextId: string,
): Task {
  return createTask(taskId, contextId, 'submitted');
}

/**
 * Creates a TaskStatusUpdateEvent indicating the task is now working.
 * Should be emitted after the initial task and before the first message.
 */
export function createWorkingStatus(
  ctx: TranslationContext,
): TaskStatusUpdateEvent {
  return createTaskStatusUpdate(ctx.taskId, ctx.contextId, 'working', false);
}

/**
 * Translates a Claude SDK message to an A2A Message.
 * Returns null for messages that should be represented differently (e.g., result).
 */
export function translateSdkMessage(
  msg: SDKMessage,
  ctx: TranslationContext,
): Message | null {
  const messageId = randomUUID();

  switch (msg.type) {
    case 'assistant': {
      const parts = translateContentBlocks((msg as any).message?.content ?? []);
      return createMessage(
        messageId,
        'agent',
        parts,
        ctx.contextId,
        ctx.taskId,
        {
          sdkMessageType: 'assistant',
          parentToolUseId: (msg as any).parent_tool_use_id,
        } satisfies HomespunMessageMetadata,
      );
    }

    case 'user': {
      const parts = translateContentBlocks((msg as any).message?.content ?? []);
      return createMessage(
        messageId,
        'user',
        parts,
        ctx.contextId,
        ctx.taskId,
        {
          sdkMessageType: 'user',
          parentToolUseId: (msg as any).parent_tool_use_id,
        } satisfies HomespunMessageMetadata,
      );
    }

    case 'system': {
      // System messages contain initialization info - emit as agent message with metadata
      const systemMsg = msg as any;
      const data: Record<string, unknown> = {
        subtype: systemMsg.subtype,
      };
      if (systemMsg.model) data.model = systemMsg.model;
      if (systemMsg.tools) data.tools = systemMsg.tools;
      if (systemMsg.permissionMode) data.permissionMode = systemMsg.permissionMode;

      return createMessage(
        messageId,
        'agent',
        [createDataPart(data, { kind: 'system' })],
        ctx.contextId,
        ctx.taskId,
        {
          sdkMessageType: 'system',
        } satisfies HomespunMessageMetadata,
      );
    }

    case 'stream_event': {
      // Stream events contain partial content - emit as agent message
      const streamMsg = msg as any;
      const eventData = streamMsg.event ?? {};

      return createMessage(
        messageId,
        'agent',
        [createDataPart(eventData as Record<string, unknown>, { kind: 'stream_event' })],
        ctx.contextId,
        ctx.taskId,
        {
          sdkMessageType: 'stream_event',
          isStreaming: true,
          parentToolUseId: streamMsg.parent_tool_use_id,
        } satisfies HomespunMessageMetadata,
      );
    }

    case 'result': {
      // Result is translated to TaskStatusUpdateEvent, not Message
      return null;
    }

    default: {
      // Unknown message type - emit as data part
      return createMessage(
        messageId,
        'agent',
        [createDataPart(msg as unknown as Record<string, unknown>, { kind: 'unknown' })],
        ctx.contextId,
        ctx.taskId,
        {
          sdkMessageType: msg.type,
        } satisfies HomespunMessageMetadata,
      );
    }
  }
}

/**
 * Translates SDK content blocks to A2A Parts.
 */
function translateContentBlocks(content: unknown[]): Part[] {
  if (!Array.isArray(content)) return [];

  return content.map((block: any): Part => {
    switch (block.type) {
      case 'text':
        return createTextPart(block.text ?? '');

      case 'thinking':
        return createDataPart(
          { thinking: block.thinking },
          { kind: 'thinking', isThinking: true },
        );

      case 'tool_use':
        return createDataPart(
          {
            toolName: block.name,
            toolUseId: block.id,
            input: block.input,
          },
          { kind: 'tool_use' },
        );

      case 'tool_result':
        return createDataPart(
          {
            toolUseId: block.tool_use_id,
            content: block.content,
            isError: block.is_error,
          },
          { kind: 'tool_result' },
        );

      default:
        // Unknown block type - preserve as data
        return createDataPart(block, { kind: 'unknown_block' });
    }
  });
}

/**
 * Translates a Claude SDK result message to an A2A TaskStatusUpdateEvent.
 */
export function translateResultToStatus(
  msg: SDKMessage,
  ctx: TranslationContext,
): TaskStatusUpdateEvent {
  const resultMsg = msg as any;
  const isError = resultMsg.is_error ?? false;
  const state: TaskState = isError ? 'failed' : 'completed';

  // Create a summary message for the status
  const summaryParts: Part[] = [];

  if (resultMsg.result) {
    summaryParts.push(createTextPart(resultMsg.result));
  }

  // Include result metadata
  summaryParts.push(createDataPart({
    subtype: resultMsg.subtype,
    durationMs: resultMsg.duration_ms,
    durationApiMs: resultMsg.duration_api_ms,
    numTurns: resultMsg.num_turns,
    totalCostUsd: resultMsg.total_cost_usd,
    usage: resultMsg.usage,
    errors: resultMsg.errors,
  }, { kind: 'result_metadata' }));

  const statusMessage = createMessage(
    randomUUID(),
    'agent',
    summaryParts,
    ctx.contextId,
    ctx.taskId,
    { sdkMessageType: 'result' },
  );

  return createTaskStatusUpdate(
    ctx.taskId,
    ctx.contextId,
    state,
    true, // final
    statusMessage,
  );
}

/**
 * Translates a control event (question_pending or plan_pending) to A2A TaskStatusUpdateEvent.
 * These map to 'input-required' state in A2A.
 */
export function translateControlEvent(
  event: ControlEvent,
  ctx: TranslationContext,
): TaskStatusUpdateEvent {
  const inputType: InputRequiredMetadata['inputType'] =
    event.type === 'question_pending' ? 'question' : 'plan-approval';

  let metadata: InputRequiredMetadata;
  let statusMessageParts: Part[];

  if (event.type === 'question_pending') {
    const questions = (event.data as { questions: QuestionData[] }).questions;
    metadata = {
      inputType: 'question',
      questions,
    };
    statusMessageParts = [
      createTextPart('User input required'),
      createDataPart({ questions }, { kind: 'questions' }),
    ];
  } else {
    const plan = (event.data as { plan: string }).plan;
    metadata = {
      inputType: 'plan-approval',
      plan,
    };
    statusMessageParts = [
      createTextPart('Plan approval required'),
      createDataPart({ plan }, { kind: 'plan' }),
    ];
  }

  const statusMessage = createMessage(
    randomUUID(),
    'agent',
    statusMessageParts,
    ctx.contextId,
    ctx.taskId,
    { sdkMessageType: event.type },
  );

  return createTaskStatusUpdate(
    ctx.taskId,
    ctx.contextId,
    'input-required',
    false, // not final - waiting for input
    statusMessage,
    metadata as unknown as Record<string, unknown>,
  );
}

/**
 * Creates an error TaskStatusUpdateEvent.
 */
export function createErrorStatus(
  ctx: TranslationContext,
  errorMessage: string,
  errorCode: string,
): TaskStatusUpdateEvent {
  const statusMessage = createMessage(
    randomUUID(),
    'agent',
    [
      createTextPart(errorMessage),
      createDataPart({ code: errorCode }, { kind: 'error' }),
    ],
    ctx.contextId,
    ctx.taskId,
    { sdkMessageType: 'error' },
  );

  return createTaskStatusUpdate(
    ctx.taskId,
    ctx.contextId,
    'failed',
    true, // final
    statusMessage,
    { errorCode },
  );
}

/**
 * Determines the A2A event kind for an SDK message or control event.
 */
export function getEventKind(
  event: SDKMessage | ControlEvent,
): 'task' | 'message' | 'status-update' {
  if ('type' in event && 'data' in event) {
    // Control event
    const controlType = (event as ControlEvent).type;
    if (controlType === 'question_pending' || controlType === 'plan_pending') {
      return 'status-update';
    }
  }

  const sdkMsg = event as SDKMessage;
  if (sdkMsg.type === 'result') {
    return 'status-update';
  }

  return 'message';
}

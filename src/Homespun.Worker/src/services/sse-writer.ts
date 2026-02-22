/**
 * SSE Writer for A2A Protocol Events
 *
 * Streams A2A-compliant events over Server-Sent Events (SSE).
 * Translates Claude Agent SDK messages to A2A protocol format.
 */

import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';
import { isControlEvent, type ControlEvent } from './session-manager.js';
import type { SessionManager } from './session-manager.js';
import {
  createInitialTask,
  createWorkingStatus,
  translateSdkMessage,
  translateResultToStatus,
  translateControlEvent,
  createErrorStatus,
  type TranslationContext,
} from './a2a-translator.js';
import type { A2AStreamEvent } from '../types/a2a.js';
import { info } from '../utils/logger.js';

/**
 * Formats an A2A event as an SSE message.
 * Uses the A2A event 'kind' as the SSE event name.
 */
export function formatSSE(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

/**
 * Streams A2A-formatted events for a session.
 *
 * Event sequence:
 * 1. `task` - Initial task with state 'submitted'
 * 2. `status-update` - Task state changes to 'working'
 * 3. `message` - Agent/user messages (translated from SDK)
 * 4. `status-update` with `input-required` - When questions or plan approval needed
 * 5. `status-update` with `completed`/`failed` and `final: true` - Terminal state
 */
export async function* streamSessionEvents(
  sessionManager: SessionManager,
  sessionId: string,
): AsyncGenerator<string> {
  const ws = sessionManager.get(sessionId);
  if (!ws) {
    // Create error context for session not found
    const errorCtx: TranslationContext = {
      taskId: sessionId,
      contextId: sessionId,
    };
    const errorEvent = createErrorStatus(
      errorCtx,
      `Session ${sessionId} not found`,
      'SESSION_NOT_FOUND',
    );
    yield formatSSE(errorEvent.kind, errorEvent);
    return;
  }

  // Create translation context - taskId maps to sessionId, contextId to conversationId
  const ctx: TranslationContext = {
    taskId: sessionId,
    contextId: ws.conversationId ?? sessionId,
  };

  // 1. Emit initial task with state 'submitted'
  const initialTask = createInitialTask(ctx.taskId, ctx.contextId);
  info(`A2A event emitted: kind='${initialTask.kind}', sessionId='${sessionId}'`);
  yield formatSSE(initialTask.kind, initialTask);

  // 2. Emit working status and set effective status to 'working'
  const workingStatus = createWorkingStatus(ctx);
  sessionManager.setEffectiveStatus(sessionId, 'working');
  info(`A2A event emitted: kind='${workingStatus.kind}', state='working', sessionId='${sessionId}'`);
  yield formatSSE(workingStatus.kind, workingStatus);

  try {
    for await (const event of sessionManager.stream(sessionId)) {
      // Update contextId if we get a conversation ID from SDK
      if (!isControlEvent(event)) {
        const sdkMsg = event as SDKMessage;
        if (sdkMsg.session_id && ctx.contextId === sessionId) {
          ctx.contextId = sdkMsg.session_id;
        }
      }

      if (isControlEvent(event)) {
        // Control events (question_pending, plan_pending) -> TaskStatusUpdateEvent with input-required
        const controlEvent = event as ControlEvent;

        // Set effective status based on control event type
        const inputType = controlEvent.type === 'plan_pending' ? 'plan_pending' : 'question_pending';
        sessionManager.setEffectiveStatus(sessionId, inputType);
        info(`A2A event emitted: kind='status-update', state='input-required', inputType='${inputType}', sessionId='${sessionId}'`);

        const statusUpdate = translateControlEvent(controlEvent, ctx);
        yield formatSSE(statusUpdate.kind, statusUpdate);
        continue;
      }

      const msg = event as SDKMessage;

      if (msg.type === 'system') {
        info(`A2A system message: subtype='${(msg as any).subtype}', permissionMode='${(msg as any).permissionMode || 'N/A'}'`);
      }

      if (msg.type === 'result') {
        // Result -> TaskStatusUpdateEvent with completed/failed, final: true
        const r = msg as any;
        const finalStatus = r.is_error ? 'failed' : 'completed';
        sessionManager.setEffectiveStatus(sessionId, finalStatus);
        info(`A2A event emitted: kind='status-update', state='${finalStatus}', subtype='${r.subtype}', sessionId='${sessionId}'`);

        const statusUpdate = translateResultToStatus(msg, ctx);
        yield formatSSE(statusUpdate.kind, statusUpdate);
        return;
      }

      // Regular SDK messages -> A2A Message
      const a2aMessage = translateSdkMessage(msg, ctx);
      if (a2aMessage) {
        // If we're in a pending state and we get a message event, the agent must be working
        // This handles edge cases where the agent produces content after plan/question
        const currentStatus = sessionManager.getEffectiveStatus(sessionId);
        if (currentStatus === 'plan_pending' || currentStatus === 'question_pending') {
          sessionManager.setEffectiveStatus(sessionId, 'working');
          info(`A2A: Override pending status to 'working' due to message event, sessionId='${sessionId}'`);
        }
        info(`A2A event emitted: kind='${a2aMessage.kind}', sessionId='${sessionId}'`);
        yield formatSSE(a2aMessage.kind, a2aMessage);
      }
    }

    // After stream completes without result message, set to idle
    sessionManager.setEffectiveStatus(sessionId, 'idle');
    info(`A2A: Stream completed, setting status to 'idle', sessionId='${sessionId}'`);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    sessionManager.setEffectiveStatus(sessionId, 'failed');
    info(`A2A event emitted: kind='status-update', state='failed', error='${message}', sessionId='${sessionId}'`);
    const errorEvent = createErrorStatus(ctx, message, 'AGENT_ERROR');
    yield formatSSE(errorEvent.kind, errorEvent);
  }
}

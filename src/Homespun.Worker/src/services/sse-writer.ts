import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';
import { isControlEvent, type OutputEvent } from './session-manager.js';
import type { SessionManager } from './session-manager.js';
import { info } from '../utils/logger.js';

export function formatSSE(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

/**
 * Streams raw SDK messages and control events as SSE-formatted strings.
 * SDK messages are emitted with their `type` field as the SSE event name.
 * Control events (e.g. question_pending) use their own event type.
 * The C# consumer handles all content block assembly and question parsing.
 */
export async function* streamSessionEvents(
  sessionManager: SessionManager,
  sessionId: string,
): AsyncGenerator<string> {
  const ws = sessionManager.get(sessionId);
  if (!ws) {
    yield formatSSE('error', {
      sessionId,
      message: `Session ${sessionId} not found`,
      code: 'SESSION_NOT_FOUND',
      isRecoverable: false,
    });
    return;
  }

  // Emit session started (lifecycle event, not an SDK message)
  yield formatSSE('session_started', {
    sessionId,
    conversationId: ws.conversationId,
  });

  try {
    for await (const event of sessionManager.stream(sessionId)) {
      if (isControlEvent(event)) {
        info(`control event: type='${event.type}'`);
        yield formatSSE(event.type, event.data);
        continue;
      }

      const msg = event as SDKMessage;
      if (msg.type === 'system') {
        info(`system message: subtype='${(msg as any).subtype}', permissionMode='${(msg as any).permissionMode || 'N/A'}'`);
      }
      if (msg.type === 'result') {
        info(`result: subtype='${(msg as any).subtype}'`);
      }
      yield formatSSE(msg.type, msg);

      if (msg.type === 'result') {
        return;
      }
    }
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    yield formatSSE('error', {
      sessionId,
      message,
      code: 'AGENT_ERROR',
      isRecoverable: false,
    });
  }
}

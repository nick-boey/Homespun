import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';
import type { SessionManager } from './session-manager.js';
import type {
  ContentBlockReceivedData,
  MessageReceivedData,
  QuestionReceivedData,
  UserQuestionData,
  QuestionOptionData,
  ResultReceivedData,
  SessionEndedData,
  SessionStartedData,
  ErrorData,
} from '../types/index.js';
import { SseEventTypes } from '../types/index.js';
import { randomUUID } from 'node:crypto';

export function formatSSE(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

/**
 * Tracks tool_use IDs to tool names for correlating tool results.
 */
class ToolUseTracker {
  private toolUses = new Map<string, string>();

  track(toolUseId: string, toolName: string): void {
    this.toolUses.set(toolUseId, toolName);
  }

  getToolName(toolUseId: string): string {
    return this.toolUses.get(toolUseId) || 'unknown';
  }
}

/**
 * Processes stream events (content_block_start/delta/stop) into our SSE event format.
 * Mirrors the C# WorkerSessionService.ProcessStreamEvent logic.
 */
class StreamEventProcessor {
  private currentContentBlocks: ContentBlockReceivedData[] = [];
  private toolTracker: ToolUseTracker;
  private sessionId: string;

  constructor(sessionId: string, toolTracker: ToolUseTracker) {
    this.sessionId = sessionId;
    this.toolTracker = toolTracker;
  }

  processStreamEvent(event: Record<string, unknown>): Array<{ eventType: string; data: unknown }> {
    const events: Array<{ eventType: string; data: unknown }> = [];
    const type = event.type as string | undefined;

    switch (type) {
      case 'content_block_start':
        this.processContentBlockStart(event);
        break;
      case 'content_block_delta':
        this.processContentBlockDelta(event);
        break;
      case 'content_block_stop':
        events.push(...this.processContentBlockStop(event));
        break;
    }

    return events;
  }

  getContentBlocks(): ContentBlockReceivedData[] {
    return this.currentContentBlocks;
  }

  clearContentBlocks(): ContentBlockReceivedData[] {
    const blocks = [...this.currentContentBlocks];
    this.currentContentBlocks = [];
    return blocks;
  }

  private processContentBlockStart(event: Record<string, unknown>): void {
    const contentBlock = event.content_block as Record<string, unknown> | undefined;
    if (!contentBlock) return;

    const index = (event.index as number) ?? this.currentContentBlocks.length;
    const blockType = contentBlock.type as string;

    let content: ContentBlockReceivedData | null = null;

    switch (blockType) {
      case 'text':
        content = {
          sessionId: this.sessionId,
          type: 'Text',
          text: '',
          index,
        };
        break;
      case 'thinking':
        content = {
          sessionId: this.sessionId,
          type: 'Thinking',
          text: '',
          index,
        };
        break;
      case 'tool_use': {
        const toolUseId = (contentBlock.id as string) || '';
        const toolName = (contentBlock.name as string) || 'unknown';

        if (toolUseId) {
          this.toolTracker.track(toolUseId, toolName);
        }

        content = {
          sessionId: this.sessionId,
          type: 'ToolUse',
          toolName,
          toolUseId,
          toolInput: '',
          index,
        };
        break;
      }
    }

    if (content) {
      this.currentContentBlocks.push(content);
    }
  }

  private processContentBlockDelta(event: Record<string, unknown>): void {
    const delta = event.delta as Record<string, unknown> | undefined;
    if (!delta) return;

    const index = (event.index as number) ?? this.currentContentBlocks.length - 1;
    if (index < 0 || index >= this.currentContentBlocks.length) return;

    const block = this.currentContentBlocks[index];
    const deltaType = delta.type as string;

    switch (deltaType) {
      case 'text_delta':
        block.text = (block.text || '') + ((delta.text as string) || '');
        break;
      case 'thinking_delta':
        block.text = (block.text || '') + ((delta.thinking as string) || '');
        break;
      case 'input_json_delta':
        block.toolInput = (block.toolInput || '') + ((delta.partial_json as string) || '');
        break;
    }
  }

  private processContentBlockStop(event: Record<string, unknown>): Array<{ eventType: string; data: unknown }> {
    const events: Array<{ eventType: string; data: unknown }> = [];
    const index = (event.index as number) ?? this.currentContentBlocks.length - 1;

    if (index < 0 || index >= this.currentContentBlocks.length) return events;

    const block = this.currentContentBlocks[index];

    // Emit content block received
    events.push({ eventType: SseEventTypes.ContentBlockReceived, data: block });

    // Check for AskUserQuestion
    if (block.toolName === 'AskUserQuestion' && block.toolInput) {
      const questionEvent = tryParseAskUserQuestion(this.sessionId, block);
      if (questionEvent) {
        events.push({ eventType: SseEventTypes.QuestionReceived, data: questionEvent });
      }
    }

    return events;
  }
}

function tryParseAskUserQuestion(sessionId: string, block: ContentBlockReceivedData): QuestionReceivedData | null {
  try {
    const toolInput = JSON.parse(block.toolInput!) as { questions?: Array<Record<string, unknown>> };
    if (!toolInput.questions || !Array.isArray(toolInput.questions)) return null;

    const questions: UserQuestionData[] = toolInput.questions.map((q) => {
      const options: QuestionOptionData[] = Array.isArray(q.options)
        ? (q.options as Array<Record<string, unknown>>).map((o) => ({
            label: (o.label as string) || '',
            description: (o.description as string) || '',
          }))
        : [];

      return {
        question: (q.question as string) || '',
        header: (q.header as string) || '',
        options,
        multiSelect: (q.multiSelect as boolean) || false,
      };
    });

    if (questions.length === 0) return null;

    return {
      sessionId,
      questionId: randomUUID(),
      toolUseId: block.toolUseId || '',
      questions,
    };
  } catch {
    return null;
  }
}

/**
 * Converts assistant message content blocks to ContentBlockReceivedData.
 * Used when content comes directly in the message (not via streaming events).
 */
function convertAssistantContent(sessionId: string, content: Array<Record<string, unknown>>): ContentBlockReceivedData[] {
  const result: ContentBlockReceivedData[] = [];

  for (let i = 0; i < content.length; i++) {
    const block = content[i];
    const blockType = block.type as string;
    let data: ContentBlockReceivedData | null = null;

    switch (blockType) {
      case 'text':
        data = {
          sessionId,
          type: 'Text',
          text: (block.text as string) || '',
          index: i,
        };
        break;
      case 'thinking':
        data = {
          sessionId,
          type: 'Thinking',
          text: (block.thinking as string) || '',
          index: i,
        };
        break;
      case 'tool_use':
        data = {
          sessionId,
          type: 'ToolUse',
          toolName: (block.name as string) || 'unknown',
          toolUseId: (block.id as string) || '',
          toolInput: block.input ? JSON.stringify(block.input) : '',
          index: i,
        };
        break;
    }

    if (data) {
      result.push(data);
    }
  }

  return result;
}

/**
 * Streams session events as SSE-formatted strings.
 * Mirrors the C# WorkerSessionService.ProcessMessagesAsync logic.
 */
export async function* streamSessionEvents(
  sessionManager: SessionManager,
  sessionId: string,
): AsyncGenerator<string> {
  const ws = sessionManager.get(sessionId);
  if (!ws) {
    yield formatSSE(SseEventTypes.Error, {
      sessionId,
      message: `Session ${sessionId} not found`,
      code: 'SESSION_NOT_FOUND',
      isRecoverable: false,
    } satisfies ErrorData);
    return;
  }

  // Emit session started
  yield formatSSE(SseEventTypes.SessionStarted, {
    sessionId,
    conversationId: ws.conversationId,
  } satisfies SessionStartedData);

  const toolTracker = new ToolUseTracker();
  const streamProcessor = new StreamEventProcessor(sessionId, toolTracker);

  try {
    for await (const msg of sessionManager.stream(sessionId)) {
      const events = processSdkMessage(sessionId, msg, streamProcessor, toolTracker);
      for (const evt of events) {
        yield formatSSE(evt.eventType, evt.data);

        // Stop after result
        if (evt.eventType === SseEventTypes.ResultReceived) {
          yield formatSSE(SseEventTypes.SessionEnded, {
            sessionId,
            reason: 'completed',
          } satisfies SessionEndedData);
          return;
        }
      }
    }

    // Stream ended without a result message
    yield formatSSE(SseEventTypes.SessionEnded, {
      sessionId,
      reason: 'completed',
    } satisfies SessionEndedData);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    yield formatSSE(SseEventTypes.Error, {
      sessionId,
      message,
      code: 'AGENT_ERROR',
      isRecoverable: false,
    } satisfies ErrorData);
    yield formatSSE(SseEventTypes.SessionEnded, {
      sessionId,
      reason: 'error',
    } satisfies SessionEndedData);
  }
}

function processSdkMessage(
  sessionId: string,
  msg: SDKMessage,
  streamProcessor: StreamEventProcessor,
  toolTracker: ToolUseTracker,
): Array<{ eventType: string; data: unknown }> {
  const events: Array<{ eventType: string; data: unknown }> = [];

  switch (msg.type) {
    case 'stream_event': {
      const event = msg.event as Record<string, unknown>;
      if (event) {
        events.push(...streamProcessor.processStreamEvent(event));
      }
      break;
    }

    case 'assistant': {
      const assistantMsg = msg.message as { content?: Array<Record<string, unknown>> };
      let contentToEmit: ContentBlockReceivedData[];

      const currentBlocks = streamProcessor.getContentBlocks();
      if (currentBlocks.length > 0) {
        contentToEmit = streamProcessor.clearContentBlocks();
      } else if (assistantMsg.content && assistantMsg.content.length > 0) {
        contentToEmit = convertAssistantContent(sessionId, assistantMsg.content);
      } else {
        contentToEmit = [];
      }

      if (contentToEmit.length > 0) {
        events.push({
          eventType: SseEventTypes.MessageReceived,
          data: {
            sessionId,
            role: 'Assistant',
            content: contentToEmit,
          } satisfies MessageReceivedData,
        });
      }
      break;
    }

    case 'user': {
      const userMsg = msg.message as { content?: Array<Record<string, unknown>> };
      if (!userMsg.content || !Array.isArray(userMsg.content)) break;

      const toolResultContents: ContentBlockReceivedData[] = [];

      for (const block of userMsg.content) {
        if (block.type === 'tool_result') {
          const toolUseId = (block.tool_use_id as string) || '';
          const toolName = toolTracker.getToolName(toolUseId);
          const isError = (block.is_error as boolean) || false;

          toolResultContents.push({
            sessionId,
            type: 'ToolResult',
            toolName,
            toolUseId,
            toolSuccess: !isError,
            text: block.content ? String(block.content) : '',
            index: toolResultContents.length,
          });
        }
      }

      if (toolResultContents.length > 0) {
        events.push({
          eventType: SseEventTypes.MessageReceived,
          data: {
            sessionId,
            role: 'User',
            content: toolResultContents,
          } satisfies MessageReceivedData,
        });
      }
      break;
    }

    case 'result': {
      const resultMsg = msg as Record<string, unknown>;

      if (resultMsg.is_error && resultMsg.result) {
        events.push({
          eventType: SseEventTypes.Error,
          data: {
            sessionId,
            message: String(resultMsg.result),
            code: 'AGENT_ERROR',
            isRecoverable: false,
          } satisfies ErrorData,
        });
      }

      events.push({
        eventType: SseEventTypes.ResultReceived,
        data: {
          sessionId,
          totalCostUsd: (resultMsg.total_cost_usd as number) || 0,
          durationMs: (resultMsg.duration_ms as number) || 0,
          conversationId: resultMsg.session_id as string | undefined,
        } satisfies ResultReceivedData,
      });
      break;
    }
  }

  return events;
}

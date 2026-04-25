/**
 * SSE Writer for A2A Protocol Events
 *
 * Streams A2A-compliant events over Server-Sent Events (SSE).
 * Translates Claude Agent SDK messages to A2A protocol format.
 */

import type { SDKMessage } from "@anthropic-ai/claude-agent-sdk";
import { isControlEvent, type ControlEvent } from "./session-manager.js";
import type { SessionManager } from "./session-manager.js";
import {
  createInitialTask,
  createWorkingStatus,
  translateSdkMessage,
  translateResultToStatus,
  translateControlEvent,
  createErrorStatus,
  type TranslationContext,
} from "./a2a-translator.js";
import type { A2AStreamEvent } from "../types/a2a.js";
import { trace, SpanKind } from "@opentelemetry/api";
import {
  info,
  debug,
  extractA2ACorrelation,
  extractMessagePreview,
  gateContentPreview,
  a2aEmitDebug,
  type A2AEmitSpanFields,
} from "../utils/otel-logger.js";

// Resolve the tracer on every call rather than caching at module load. The
// OTel API's `ProxyTracer` caches its delegate on first use, which makes
// test isolation with per-test `setGlobalTracerProvider` impossible if we
// bind once. Runtime cost is negligible: `getTracer` is a map lookup.
function workerTracer() {
  return trace.getTracer("homespun.worker");
}

/**
 * Formats tool parameters for debug logging.
 * Shows parameter names with scalar values (strings truncated to 50 chars).
 * Objects/arrays shown as [Object] or [Array(N)].
 */
function formatToolParams(input: unknown): string {
  if (!input || typeof input !== "object") return "";

  const params: string[] = [];
  for (const [key, value] of Object.entries(input as Record<string, unknown>)) {
    if (value === undefined || value === null) {
      continue;
    } else if (typeof value === "string") {
      const truncated = value.length > 50 ? value.slice(0, 50) + "..." : value;
      params.push(`${key}="${truncated}"`);
    } else if (typeof value === "number" || typeof value === "boolean") {
      params.push(`${key}=${value}`);
    } else if (Array.isArray(value)) {
      params.push(`${key}=[Array(${value.length})]`);
    } else if (typeof value === "object") {
      params.push(`${key}=[Object]`);
    }
  }

  return params.length > 0 ? params.join(" ") : "";
}

/**
 * Extracts a text summary from message content blocks.
 * Truncates to specified length with '...' if needed.
 */
function extractContentSummary(msg: unknown, maxLength: number): string {
  const assistantMsg = msg as {
    type: string;
    message?: {
      content?: Array<{ type: string; text?: string }>;
    };
  };

  const content = assistantMsg.message?.content;
  if (!content || !Array.isArray(content)) {
    return "";
  }

  const textParts: string[] = [];
  for (const block of content) {
    if (block.type === "text" && block.text) {
      textParts.push(block.text);
    }
  }

  const fullText = textParts.join(" ").replace(/\s+/g, " ").trim();
  if (fullText.length > maxLength) {
    return fullText.slice(0, maxLength) + "...";
  }
  return fullText;
}

/**
 * Formats an A2A event as an SSE message.
 * Uses the A2A event 'kind' as the SSE event name.
 */
export function formatSSE(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

/**
 * Emits a `homespun.a2a.emit` PRODUCER span for an A2A stream event about to
 * be written to SSE, then returns the SSE-formatted string. Every outbound
 * event is spanned exactly once with its correlation IDs attached as span
 * attributes.
 */
export function emitAndFormatSSE(
  sessionId: string,
  event: string,
  data: unknown,
): string {
  const fields = extractA2ACorrelation(event, data);
  fields.SessionId = fields.SessionId || sessionId;

  if (event === "message") {
    const preview = gateContentPreview(extractMessagePreview(data));
    if (preview !== undefined) fields.ContentPreview = preview;
  }

  const span = workerTracer().startSpan("homespun.a2a.emit", {
    kind: SpanKind.PRODUCER,
  });
  span.setAttributes(mapFieldsToAttributes(fields));
  span.end();

  // Full-body debug log gated by HOMESPUN_DEBUG_FULL_MESSAGES (umbrella) or
  // DEBUG_AGENT_SDK (back-compat). No-op when neither is set.
  a2aEmitDebug(fields.SessionId, event, data, fields.Seq);

  return formatSSE(event, data);
}

function mapFieldsToAttributes(
  fields: A2AEmitSpanFields,
): Record<string, string | number> {
  const attrs: Record<string, string | number> = {
    "homespun.session.id": fields.SessionId,
  };
  if (fields.TaskId !== undefined) attrs["homespun.task.id"] = fields.TaskId;
  if (fields.MessageId !== undefined) attrs["homespun.message.id"] = fields.MessageId;
  if (fields.ArtifactId !== undefined) attrs["homespun.artifact.id"] = fields.ArtifactId;
  if (fields.StatusTimestamp !== undefined) attrs["homespun.status.timestamp"] = fields.StatusTimestamp;
  if (fields.A2AKind !== undefined) attrs["homespun.a2a.kind"] = fields.A2AKind;
  if (fields.AGUIType !== undefined) attrs["homespun.agui.type"] = fields.AGUIType;
  if (fields.AGUICustomName !== undefined) attrs["homespun.agui.custom_name"] = fields.AGUICustomName;
  if (fields.ContentPreview !== undefined) attrs["homespun.content.preview"] = fields.ContentPreview;
  if (fields.Seq !== undefined) attrs["homespun.seq"] = fields.Seq;
  if (fields.EventId !== undefined) attrs["homespun.event.id"] = fields.EventId;
  return attrs;
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
      "SESSION_NOT_FOUND",
    );
    yield emitAndFormatSSE(sessionId, errorEvent.kind, errorEvent);
    return;
  }

  // Create translation context - taskId maps to sessionId, contextId to conversationId
  const ctx: TranslationContext = {
    taskId: sessionId,
    contextId: ws.conversationId ?? sessionId,
  };

  // 1. Emit initial task with state 'submitted'
  const initialTask = createInitialTask(ctx.taskId, ctx.contextId);
  yield emitAndFormatSSE(sessionId, initialTask.kind, initialTask);
  info(`A2A state transition: submitted → working (sessionId: ${sessionId})`);

  // 2. Emit working status
  const workingStatus = createWorkingStatus(ctx);
  yield emitAndFormatSSE(sessionId, workingStatus.kind, workingStatus);

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
        const controlEvent = event as ControlEvent;

        if (controlEvent.type === "status_resumed") {
          // Emit working status when resuming from question/plan
          info(
            `A2A state transition: input-required → working (sessionId: ${sessionId})`,
          );
          const workingStatus = createWorkingStatus(ctx);
          yield emitAndFormatSSE(sessionId, workingStatus.kind, workingStatus);
          continue;
        }

        // Control events (question_pending, plan_pending) -> TaskStatusUpdateEvent with input-required
        info(`A2A control event: type='${controlEvent.type}'`);
        info(
          `A2A state transition: working → input-required (sessionId: ${sessionId})`,
        );

        const statusUpdate = translateControlEvent(controlEvent, ctx);
        yield emitAndFormatSSE(sessionId, statusUpdate.kind, statusUpdate);
        continue;
      }

      const msg = event as SDKMessage;

      if (msg.type === "system") {
        info(
          `A2A system message: subtype='${(msg as any).subtype}', permissionMode='${(msg as any).permissionMode || "N/A"}'`,
        );
      }

      if (msg.type === "result") {
        // Result -> TaskStatusUpdateEvent with completed/failed, final: true
        const r = msg as any;
        info(`A2A result: subtype='${r.subtype}', is_error=${r.is_error}`);
        const finalState = r.is_error ? "failed" : "completed";
        info(
          `A2A state transition: working → ${finalState} (sessionId: ${sessionId})`,
        );

        const statusUpdate = translateResultToStatus(msg, ctx);
        yield emitAndFormatSSE(sessionId, statusUpdate.kind, statusUpdate);
        // Do NOT return here: the SDK query iterator stays open across turns
        // and still emits task_notification / task_started / task_updated
        // messages whenever background tools finish (sometimes minutes after
        // the result). The long-lived GET /events consumer on the server
        // drains them; closing the generator orphans them in OutputChannel.
        continue;
      }

      // Regular SDK messages -> A2A Message
      const a2aMessage = translateSdkMessage(msg, ctx);
      if (a2aMessage) {
        // Log tool executions at DEBUG level with parameter details
        if (msg.type === "stream_event") {
          const streamMsg = msg as any;
          const eventData = streamMsg.event;
          if (eventData?.type === "tool_invocation") {
            const toolName = eventData.name || "unknown";
            const params = formatToolParams(eventData.input);
            debug(`Tool: ${toolName} ${params} (sessionId: ${sessionId})`);
          }
        }

        // Log assistant message content summary
        if (msg.type === "assistant") {
          const summary = extractContentSummary(msg, 200);
          if (summary) {
            debug(`Assistant: ${summary} (sessionId: ${sessionId})`);
          }
        }

        yield emitAndFormatSSE(sessionId, a2aMessage.kind, a2aMessage);
      }
    }
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    const errorEvent = createErrorStatus(ctx, message, "AGENT_ERROR");
    yield emitAndFormatSSE(sessionId, errorEvent.kind, errorEvent);
  }
}

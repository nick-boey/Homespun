/**
 * Thin OpenTelemetry-backed replacement for the legacy `./logger.ts` module.
 *
 * All log call sites emit through `logs.getLogger('homespun.worker')`. Errors
 * attach `exception.*` attributes and, when inside a span, call
 * `span.recordException(err)` so Seq surfaces them on the trace timeline.
 *
 * The hop-based session-event helper has been retired — every previous call
 * site now starts a `homespun.a2a.emit` PRODUCER span via the worker tracer.
 * The correlation-extraction helpers (`extractA2ACorrelation`,
 * `extractMessagePreview`) remain so span callers can reuse the same
 * attribute shape, and `gateContentPreview` replaces the pair of
 * `getContentPreviewChars()` + `truncatePreview()` helpers that used to gate
 * the `ContentPreview` hop-log field.
 */
import { logs, SeverityNumber, type AnyValueMap } from '@opentelemetry/api-logs';
import { trace, type Span } from '@opentelemetry/api';

const LOGGER_NAME = 'homespun.worker';

function getLogger() {
  return logs.getLogger(LOGGER_NAME);
}

function attachException(err: unknown): AnyValueMap {
  if (err === undefined || err === null) return {};
  const asError = err instanceof Error ? err : undefined;
  const attrs: AnyValueMap = {
    'exception.message': asError ? asError.message : String(err),
    'exception.type': asError?.name ?? typeof err,
  };
  if (asError?.stack) attrs['exception.stacktrace'] = asError.stack;
  const activeSpan: Span | undefined = trace.getActiveSpan();
  if (activeSpan && asError) {
    activeSpan.recordException(asError);
  }
  return attrs;
}

const debugEnabled = process.env.DEBUG_LOGGING === 'true';

export function debug(message: string): void {
  if (!debugEnabled) return;
  getLogger().emit({
    severityNumber: SeverityNumber.DEBUG,
    severityText: 'Debug',
    body: message,
  });
}

export function info(message: string): void {
  getLogger().emit({
    severityNumber: SeverityNumber.INFO,
    severityText: 'Information',
    body: message,
  });
}

export function warn(message: string): void {
  getLogger().emit({
    severityNumber: SeverityNumber.WARN,
    severityText: 'Warning',
    body: message,
  });
}

export function error(message: string, err?: unknown): void {
  getLogger().emit({
    severityNumber: SeverityNumber.ERROR,
    severityText: 'Error',
    body: message,
    attributes: attachException(err),
  });
}

/**
 * Whether `DEBUG_AGENT_SDK=true` is set in the process environment. Read
 * lazily on each call so tests that stub env after import still work.
 */
export function isSdkDebugEnabled(): boolean {
  return process.env.DEBUG_AGENT_SDK === 'true';
}

/**
 * Emit an SDK-boundary debug event. Historically this wrote JSON to stdout
 * under `DEBUG_AGENT_SDK=true`; the OTel replacement attaches the payload as
 * a span event on the active span (when one exists) and additionally emits a
 * DEBUG-severity log record so tools like Seq still see it outside of a
 * trace.
 *
 * - `direction: 'tx'` — outbound from worker to SDK (session options, user
 *   messages pushed into the input queue, control calls like
 *   setPermissionMode / setModel).
 * - `direction: 'rx'` — inbound from SDK (raw messages yielded by the
 *   `Query` async iterable).
 */
export function sdkDebug(direction: 'tx' | 'rx', msg: unknown): void {
  if (!isSdkDebugEnabled()) return;
  let payload: string;
  try {
    payload = JSON.stringify(msg);
  } catch {
    payload = String(msg);
  }

  const activeSpan: Span | undefined = trace.getActiveSpan();
  if (activeSpan) {
    activeSpan.addEvent(`sdk.${direction}`, { payload });
  }

  getLogger().emit({
    severityNumber: SeverityNumber.DEBUG,
    severityText: 'Debug',
    body: `[SDK ${direction}] ${payload}`,
    attributes: {
      'sdk.direction': direction,
    },
  });
}

// ---------------------------------------------------------------------------
// Span-attribute helpers for the `homespun.a2a.emit` span call sites.
// ---------------------------------------------------------------------------

export interface A2AEmitSpanFields {
  SessionId: string;
  TaskId?: string;
  MessageId?: string;
  ArtifactId?: string;
  StatusTimestamp?: string;
  A2AKind?: string;
  AGUIType?: string;
  AGUICustomName?: string;
  ContentPreview?: string;
  Seq?: number;
  EventId?: string;
}

function getContentPreviewChars(): number {
  const raw = process.env.CONTENT_PREVIEW_CHARS;
  if (raw === undefined || raw === '') {
    return process.env.NODE_ENV === 'production' ? 0 : 80;
  }
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) && n >= 0 ? n : 0;
}

/**
 * Returns <paramref name="text"/> truncated to the configured preview budget
 * followed by an ellipsis when longer. Returns <c>undefined</c> when the
 * budget is zero or the text is null/undefined so the caller can skip the
 * <c>homespun.content.preview</c> attribute entirely.
 */
export function gateContentPreview(
  text: string | null | undefined,
): string | undefined {
  const chars = getContentPreviewChars();
  if (chars <= 0 || text === undefined || text === null) return undefined;
  if (text.length <= chars) return text;
  return text.slice(0, chars) + '\u2026';
}

export function extractA2ACorrelation(
  kind: string,
  data: unknown,
): A2AEmitSpanFields {
  const sessionId = extractString(data, ['contextId']) ?? '';
  const fields: A2AEmitSpanFields = {
    SessionId: sessionId,
    A2AKind: kind,
  };
  const taskId = extractString(data, ['taskId']) ?? extractString(data, ['id']);
  if (taskId) fields.TaskId = taskId;

  if (kind === 'message') {
    const messageId = extractString(data, ['messageId']);
    if (messageId) fields.MessageId = messageId;
  }
  if (kind === 'status-update') {
    const ts = extractString(data, ['status', 'timestamp']);
    if (ts) fields.StatusTimestamp = ts;
  }
  if (kind === 'artifact-update') {
    const artifactId = extractString(data, ['artifact', 'artifactId']);
    if (artifactId) fields.ArtifactId = artifactId;
  }

  return fields;
}

function extractString(obj: unknown, path: string[]): string | undefined {
  let cur: unknown = obj;
  for (const key of path) {
    if (cur === null || cur === undefined || typeof cur !== 'object')
      return undefined;
    cur = (cur as Record<string, unknown>)[key];
  }
  return typeof cur === 'string' ? cur : undefined;
}

export function extractMessagePreview(data: unknown): string | undefined {
  if (data === null || typeof data !== 'object') return undefined;
  const parts = (data as { parts?: unknown }).parts;
  if (!Array.isArray(parts)) return undefined;
  for (const part of parts) {
    if (
      part &&
      typeof part === 'object' &&
      (part as { kind?: string }).kind === 'text'
    ) {
      const text = (part as { text?: string }).text;
      if (typeof text === 'string') return text;
    }
  }
  return undefined;
}

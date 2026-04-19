/**
 * Thin OpenTelemetry-backed replacement for the legacy `./logger.ts` module.
 *
 * All log call sites emit through `logs.getLogger('homespun.worker')`. Errors
 * attach `exception.*` attributes and, when inside a span, call
 * `span.recordException(err)` so Seq surfaces them on the trace timeline.
 *
 * The legacy `sessionEventLog` / `extractA2ACorrelation` / `extractMessagePreview`
 * helpers are preserved (under TODO comments) so existing call sites keep
 * compiling while `session-event-log-to-spans` converts them to real spans.
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

// Legacy exports retained for backward compatibility. `debug(...)` is still
// gated by DEBUG_LOGGING to preserve the original noise-reduction behaviour.
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
// Session-event-log compatibility shim.
//
// The hop-based SessionEventLog entries are owned by
// `session-event-log-to-spans` (change #6). Until that converts them to
// proper spans, every legacy `sessionEventLog(...)` call is forwarded to a
// DEBUG-severity OTel log with a `homespun.deprecated.hop` attribute so the
// records remain searchable while they wait on migration.
// ---------------------------------------------------------------------------

export const SessionEventHop = {
  WorkerA2AEmit: 'worker.a2a.emit',
} as const;
export type SessionEventHopValue =
  (typeof SessionEventHop)[keyof typeof SessionEventHop];

export interface SessionEventLogFields {
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

export function getContentPreviewChars(): number {
  const raw = process.env.CONTENT_PREVIEW_CHARS;
  if (raw === undefined || raw === '') {
    return process.env.NODE_ENV === 'production' ? 0 : 80;
  }
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) && n >= 0 ? n : 0;
}

export function truncatePreview(
  text: string | null | undefined,
  chars: number,
): string | undefined {
  if (chars <= 0 || text === undefined || text === null) return undefined;
  if (text.length <= chars) return text;
  return text.slice(0, chars) + '\u2026';
}

/**
 * TODO(#session-event-log-to-spans): replace every call site with a real
 * span. In the meantime this emits a DEBUG log carrying the same fields as
 * attributes so ops can grep for the hop in Seq.
 */
export function sessionEventLog(
  hop: SessionEventHopValue,
  fields: SessionEventLogFields,
): void {
  const attrs: AnyValueMap = {
    'homespun.deprecated.hop': hop,
    'homespun.session.id': fields.SessionId,
  };
  if (fields.TaskId !== undefined) attrs['homespun.task.id'] = fields.TaskId;
  if (fields.MessageId !== undefined)
    attrs['homespun.message.id'] = fields.MessageId;
  if (fields.ArtifactId !== undefined)
    attrs['homespun.artifact.id'] = fields.ArtifactId;
  if (fields.StatusTimestamp !== undefined)
    attrs['homespun.status.timestamp'] = fields.StatusTimestamp;
  if (fields.A2AKind !== undefined) attrs['homespun.a2a.kind'] = fields.A2AKind;
  if (fields.AGUIType !== undefined)
    attrs['homespun.agui.type'] = fields.AGUIType;
  if (fields.AGUICustomName !== undefined)
    attrs['homespun.agui.custom_name'] = fields.AGUICustomName;
  if (fields.ContentPreview !== undefined)
    attrs['homespun.content.preview'] = fields.ContentPreview;
  if (fields.Seq !== undefined) attrs['homespun.seq'] = fields.Seq;
  if (fields.EventId !== undefined) attrs['homespun.event.id'] = fields.EventId;

  getLogger().emit({
    severityNumber: SeverityNumber.DEBUG,
    severityText: 'Debug',
    body: buildSessionEventLogMessage(hop, fields),
    attributes: attrs,
  });
}

function buildSessionEventLogMessage(
  hop: string,
  fields: SessionEventLogFields,
): string {
  const parts: string[] = [hop];
  if (fields.A2AKind) parts.push(`a2aKind=${fields.A2AKind}`);
  if (fields.MessageId) parts.push(`msg=${fields.MessageId.slice(0, 8)}`);
  return parts.join(' ');
}

export function extractA2ACorrelation(
  kind: string,
  data: unknown,
): SessionEventLogFields {
  const sessionId = extractString(data, ['contextId']) ?? '';
  const fields: SessionEventLogFields = {
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

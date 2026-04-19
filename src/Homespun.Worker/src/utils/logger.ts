interface LogEntry {
  Timestamp: string;
  Level: string;
  Message: string;
  SourceContext: string;
  Component: string;
  Exception?: string;
  IssueId?: string;
  ProjectName?: string;
}

// Cache environment values at startup
const issueId = process.env.ISSUE_ID || undefined;
const projectName = process.env.PROJECT_NAME || undefined;
const debugEnabled = process.env.DEBUG_LOGGING === 'true';

/**
 * Whether `DEBUG_AGENT_SDK=true` is set in the process environment. Read
 * lazily on each call so tests that stub env after import still work.
 */
export function isSdkDebugEnabled(): boolean {
  return process.env.DEBUG_AGENT_SDK === 'true';
}

function getCallerInfo(): { file: string; line: number } {
  const stack = new Error().stack?.split('\n')[3]; // Skip: Error, getCallerInfo, log fn
  const match = stack?.match(/at .* \((.+):(\d+):\d+\)/) || stack?.match(/at (.+):(\d+):\d+/);
  if (match) {
    const fullPath = match[1];
    const fileName = fullPath.split('/').pop() || 'unknown';
    return { file: fileName, line: parseInt(match[2], 10) };
  }
  return { file: 'unknown', line: 0 };
}

function formatLog(level: string, message: string, error?: unknown): string {
  const { file } = getCallerInfo();
  const entry: LogEntry = {
    Timestamp: new Date().toISOString(),
    Level: level,
    Message: message,
    SourceContext: file,
    Component: 'Worker',
  };
  if (error) {
    entry.Exception = error instanceof Error ? error.message : String(error);
  }
  if (issueId) {
    entry.IssueId = issueId;
  }
  if (projectName) {
    entry.ProjectName = projectName;
  }
  return JSON.stringify(entry);
}

export function debug(message: string): void {
  if (!debugEnabled) return;
  console.debug(formatLog('Debug', message));
}

export function info(message: string): void {
  console.log(formatLog('Information', message));
}

export function warn(message: string): void {
  console.warn(formatLog('Warning', message));
}

export function error(message: string, err?: unknown): void {
  console.error(formatLog('Error', message, err));
}

/**
 * Emit a single structured log entry capturing an SDK-boundary message.
 *
 * Gated by `DEBUG_AGENT_SDK=true`. Enabled by default in both docker-compose
 * and server-spawned agent containers; set `DEBUG_AGENT_SDK=false` to disable.
 * The payload is JSON-serialized into the entry's `Message` field, so
 * `docker logs` consumers handle it identically to other worker logs.
 *
 * - `direction: 'tx'` — outbound from worker to SDK (session options, user
 *   messages pushed into the input queue, control calls like setPermissionMode
 *   / setModel).
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
  console.log(
    formatLog('Debug', `[SDK ${direction}] ${payload}`),
  );
}

/**
 * Pipeline hop identifiers that worker-emitted SessionEventLog entries use. The
 * worker only emits at `worker.a2a.emit`; the remaining hops are logged by the
 * server and client.
 */
export const SessionEventHop = {
  WorkerA2AEmit: 'worker.a2a.emit',
} as const;
export type SessionEventHopValue =
  (typeof SessionEventHop)[keyof typeof SessionEventHop];

/**
 * Fields carried by every {@link sessionEventLog} entry. Mirrors the C#
 * `SessionEventLogEntry` record so Seq / the Aspire dashboard surface them
 * as addressable OTLP log attributes.
 */
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

interface SessionEventLogEntry extends LogEntry {
  Hop: string;
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

/**
 * Number of characters retained in the `ContentPreview` field on every
 * session-event-log entry. `0` disables the field entirely. Read lazily so
 * tests can override via `process.env`.
 */
export function getContentPreviewChars(): number {
  const raw = process.env.CONTENT_PREVIEW_CHARS;
  if (raw === undefined || raw === '') {
    // No explicit setting. Development defaults to 80, production to 0.
    return process.env.NODE_ENV === 'production' ? 0 : 80;
  }
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) && n >= 0 ? n : 0;
}

/**
 * Truncates `text` to `chars` characters followed by an ellipsis when longer;
 * returns `undefined` when `chars` is zero or `text` is undefined.
 */
export function truncatePreview(
  text: string | null | undefined,
  chars: number,
): string | undefined {
  if (chars <= 0 || text === undefined || text === null) return undefined;
  if (text.length <= chars) return text;
  return text.slice(0, chars) + '\u2026';
}

/**
 * Emits a structured {@link SessionEventLogEntry} for a pipeline hop
 * (worker side only emits `worker.a2a.emit`). Fields are placed at the
 * top level of the JSON object for LogQL filtering.
 */
export function sessionEventLog(
  hop: SessionEventHopValue,
  fields: SessionEventLogFields,
): void {
  const entry: SessionEventLogEntry = {
    Timestamp: new Date().toISOString(),
    Level: 'Information',
    Message: buildSessionEventLogMessage(hop, fields),
    SourceContext: 'Worker',
    Component: 'Worker',
    Hop: hop,
    SessionId: fields.SessionId,
  };
  if (fields.TaskId !== undefined) entry.TaskId = fields.TaskId;
  if (fields.MessageId !== undefined) entry.MessageId = fields.MessageId;
  if (fields.ArtifactId !== undefined) entry.ArtifactId = fields.ArtifactId;
  if (fields.StatusTimestamp !== undefined)
    entry.StatusTimestamp = fields.StatusTimestamp;
  if (fields.A2AKind !== undefined) entry.A2AKind = fields.A2AKind;
  if (fields.AGUIType !== undefined) entry.AGUIType = fields.AGUIType;
  if (fields.AGUICustomName !== undefined)
    entry.AGUICustomName = fields.AGUICustomName;
  if (fields.ContentPreview !== undefined)
    entry.ContentPreview = fields.ContentPreview;
  if (fields.Seq !== undefined) entry.Seq = fields.Seq;
  if (fields.EventId !== undefined) entry.EventId = fields.EventId;
  if (issueId) entry.IssueId = issueId;
  if (projectName) entry.ProjectName = projectName;
  console.log(JSON.stringify(entry));
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

/**
 * Extracts correlation fields from an A2A stream event payload for logging
 * at the `worker.a2a.emit` hop. Returns undefined for missing fields.
 */
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

/**
 * Pulls a short text preview from an A2A `Message` event's first text part.
 * Returns undefined for non-message events or messages without text parts.
 */
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

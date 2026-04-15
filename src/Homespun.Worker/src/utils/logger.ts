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
 * Gated by `DEBUG_AGENT_SDK=true` so production traffic is unaffected unless
 * the flag is explicitly enabled. The payload is JSON-serialized into the
 * entry's `Message` field, so existing Promtail / `docker logs` consumers
 * handle it identically to other worker logs.
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

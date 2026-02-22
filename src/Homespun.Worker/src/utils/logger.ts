interface LogEntry {
  Timestamp: string;
  Level: string;
  Message: string;
  SourceContext: string;
  Component: string;
  Exception?: string;
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
  return JSON.stringify(entry);
}

export function debug(message: string): void {
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

type LogLevel = 'DEBUG' | 'INFO' | 'WARN' | 'ERROR';

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

function formatLog(level: LogLevel, message: string): string {
  const { file, line } = getCallerInfo();
  return `${level} [${file}:${line}] ${message}`;
}

export function debug(message: string): void {
  console.debug(formatLog('DEBUG', message));
}

export function info(message: string): void {
  console.log(formatLog('INFO', message));
}

export function warn(message: string): void {
  console.warn(formatLog('WARN', message));
}

export function error(message: string, err?: unknown): void {
  const suffix = err ? `: ${err instanceof Error ? err.message : String(err)}` : '';
  console.error(formatLog('ERROR', `${message}${suffix}`));
}

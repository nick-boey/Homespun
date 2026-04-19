import { readFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { error as logError } from './otel-logger.js';

/**
 * Reads the last N lines from a file.
 * Returns an empty array if the file doesn't exist or can't be read.
 */
export async function readLastLines(filePath: string, maxLines: number): Promise<string[]> {
  try {
    if (!existsSync(filePath)) {
      return [];
    }

    const content = await readFile(filePath, 'utf-8');
    const lines = content.split('\n').filter(line => line.trim().length > 0);
    const startIndex = Math.max(0, lines.length - maxLines);
    return lines.slice(startIndex);
  } catch (err) {
    logError(`Failed to read last lines from ${filePath}`, err);
    return [];
  }
}

/**
 * Process diagnostics information.
 */
export interface ProcessDiagnostics {
  pid: number;
  uptime: number;
  memory: {
    rss: number;
    heapUsed: number;
    heapTotal: number;
    external: number;
  };
  cpu: NodeJS.CpuUsage;
  timestamp: string;
}

/**
 * Debug information captured during error scenarios.
 */
export interface DebugInfo {
  lastStderr: string[];
  diagnostics: ProcessDiagnostics;
  timestamp: string;
  sessionCount?: number;
}

/**
 * Captures current process diagnostics.
 */
export function captureProcessDiagnostics(): ProcessDiagnostics {
  const memUsage = process.memoryUsage();
  const cpuUsage = process.cpuUsage();

  return {
    pid: process.pid,
    uptime: process.uptime(),
    memory: {
      rss: memUsage.rss,
      heapUsed: memUsage.heapUsed,
      heapTotal: memUsage.heapTotal,
      external: memUsage.external,
    },
    cpu: cpuUsage,
    timestamp: new Date().toISOString(),
  };
}

/**
 * Captures debug information including stderr logs and process diagnostics.
 */
export async function captureDebugInfo(sessionCount?: number): Promise<DebugInfo> {
  const debugLogPath = '/home/homespun/.claude/debug/claude_sdk_debug.log';
  const lastStderr = await readLastLines(debugLogPath, 50);
  const diagnostics = captureProcessDiagnostics();

  const debugInfo: DebugInfo = {
    lastStderr,
    diagnostics,
    timestamp: new Date().toISOString(),
  };

  if (sessionCount !== undefined) {
    debugInfo.sessionCount = sessionCount;
  }

  return debugInfo;
}

/**
 * Formats bytes to human-readable string.
 */
export function formatBytes(bytes: number): string {
  const units = ['B', 'KB', 'MB', 'GB'];
  let size = bytes;
  let unitIndex = 0;

  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex++;
  }

  return `${size.toFixed(2)} ${units[unitIndex]}`;
}

/**
 * Monitors a file for changes and returns new lines since last check.
 */
export class FileMonitor {
  private lastPosition = 0;
  private filePath: string;

  constructor(filePath: string) {
    this.filePath = filePath;
  }

  /**
   * Reads new lines added to the file since last check.
   */
  async readNewLines(): Promise<string[]> {
    try {
      if (!existsSync(this.filePath)) {
        return [];
      }

      const content = await readFile(this.filePath, 'utf-8');
      const newContent = content.slice(this.lastPosition);
      this.lastPosition = content.length;

      if (newContent.length === 0) {
        return [];
      }

      return newContent.split('\n').filter(line => line.trim().length > 0);
    } catch (err) {
      logError(`Failed to read new lines from ${this.filePath}`, err);
      return [];
    }
  }

  /**
   * Resets the file position to start reading from the beginning.
   */
  reset(): void {
    this.lastPosition = 0;
  }
}
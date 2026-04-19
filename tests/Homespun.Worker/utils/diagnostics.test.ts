import { vi, beforeEach } from 'vitest';
import { readFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import {
  readLastLines,
  captureProcessDiagnostics,
  captureDebugInfo,
  formatBytes,
  FileMonitor,
} from '#src/utils/diagnostics.js';

// Mock fs modules
vi.mock('node:fs', () => ({
  existsSync: vi.fn(),
}));

vi.mock('node:fs/promises', () => ({
  readFile: vi.fn(),
}));

vi.mock('#src/utils/otel-logger.js', () => ({
  error: vi.fn(),
}));

describe('Diagnostics Utilities', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('readLastLines', () => {
    it('returns last N lines from file', async () => {
      vi.mocked(existsSync).mockReturnValue(true);
      vi.mocked(readFile).mockResolvedValue('line1\nline2\nline3\nline4\nline5\n');

      const result = await readLastLines('/test/file.log', 3);

      expect(result).toEqual(['line3', 'line4', 'line5']);
      expect(readFile).toHaveBeenCalledWith('/test/file.log', 'utf-8');
    });

    it('returns empty array for non-existent file', async () => {
      vi.mocked(existsSync).mockReturnValue(false);

      const result = await readLastLines('/test/missing.log', 5);

      expect(result).toEqual([]);
      expect(readFile).not.toHaveBeenCalled();
    });

    it('returns empty array on read error', async () => {
      vi.mocked(existsSync).mockReturnValue(true);
      vi.mocked(readFile).mockRejectedValue(new Error('Permission denied'));

      const result = await readLastLines('/test/file.log', 5);

      expect(result).toEqual([]);
    });

    it('filters out empty lines', async () => {
      vi.mocked(existsSync).mockReturnValue(true);
      vi.mocked(readFile).mockResolvedValue('line1\n\n\nline2\n   \nline3\n');

      const result = await readLastLines('/test/file.log', 10);

      expect(result).toEqual(['line1', 'line2', 'line3']);
    });
  });

  describe('captureProcessDiagnostics', () => {
    it('captures current process information', () => {
      const diagnostics = captureProcessDiagnostics();

      expect(diagnostics.pid).toBe(process.pid);
      expect(diagnostics.uptime).toBeGreaterThanOrEqual(0);
      expect(diagnostics.memory).toMatchObject({
        rss: expect.any(Number),
        heapUsed: expect.any(Number),
        heapTotal: expect.any(Number),
        external: expect.any(Number),
      });
      expect(diagnostics.cpu).toMatchObject({
        user: expect.any(Number),
        system: expect.any(Number),
      });
      expect(diagnostics.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/);
    });
  });

  describe('captureDebugInfo', () => {
    it('captures debug information with stderr logs', async () => {
      vi.mocked(existsSync).mockReturnValue(true);
      vi.mocked(readFile).mockResolvedValue('stderr line 1\nstderr line 2\n');

      const debugInfo = await captureDebugInfo(3);

      expect(debugInfo.lastStderr).toEqual(['stderr line 1', 'stderr line 2']);
      expect(debugInfo.diagnostics).toMatchObject({
        pid: process.pid,
        memory: expect.any(Object),
      });
      expect(debugInfo.sessionCount).toBe(3);
      expect(debugInfo.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/);
    });

    it('handles missing debug log gracefully', async () => {
      vi.mocked(existsSync).mockReturnValue(false);

      const debugInfo = await captureDebugInfo();

      expect(debugInfo.lastStderr).toEqual([]);
      expect(debugInfo.diagnostics).toBeDefined();
      expect(debugInfo.sessionCount).toBeUndefined();
    });
  });

  describe('formatBytes', () => {
    it('formats bytes correctly', () => {
      expect(formatBytes(0)).toBe('0.00 B');
      expect(formatBytes(512)).toBe('512.00 B');
      expect(formatBytes(1024)).toBe('1.00 KB');
      expect(formatBytes(1536)).toBe('1.50 KB');
      expect(formatBytes(1048576)).toBe('1.00 MB');
      expect(formatBytes(1073741824)).toBe('1.00 GB');
      expect(formatBytes(1610612736)).toBe('1.50 GB');
    });
  });

  describe('FileMonitor', () => {
    let monitor: FileMonitor;

    beforeEach(() => {
      monitor = new FileMonitor('/test/file.log');
    });

    it('reads new lines since last check', async () => {
      vi.mocked(existsSync).mockReturnValue(true);
      vi.mocked(readFile)
        .mockResolvedValueOnce('line1\nline2\n')
        .mockResolvedValueOnce('line1\nline2\nline3\nline4\n');

      // First read
      const firstLines = await monitor.readNewLines();
      expect(firstLines).toEqual(['line1', 'line2']);

      // Second read - only new lines
      const newLines = await monitor.readNewLines();
      expect(newLines).toEqual(['line3', 'line4']);
    });

    it('returns empty array for non-existent file', async () => {
      vi.mocked(existsSync).mockReturnValue(false);

      const lines = await monitor.readNewLines();
      expect(lines).toEqual([]);
    });

    it('handles read errors gracefully', async () => {
      vi.mocked(existsSync).mockReturnValue(true);
      vi.mocked(readFile).mockRejectedValue(new Error('Read error'));

      const lines = await monitor.readNewLines();
      expect(lines).toEqual([]);
    });

    it('resets position when requested', async () => {
      vi.mocked(existsSync).mockReturnValue(true);
      vi.mocked(readFile).mockResolvedValue('line1\nline2\n');

      // First read
      await monitor.readNewLines();

      // Reset
      monitor.reset();

      // Read again - should get all lines
      const lines = await monitor.readNewLines();
      expect(lines).toEqual(['line1', 'line2']);
    });

    it('filters empty lines from new content', async () => {
      vi.mocked(existsSync).mockReturnValue(true);
      vi.mocked(readFile)
        .mockResolvedValueOnce('line1\n')
        .mockResolvedValueOnce('line1\n\n\nline2\n   \n');

      // First read
      await monitor.readNewLines();

      // Second read - should filter empty lines
      const newLines = await monitor.readNewLines();
      expect(newLines).toEqual(['line2']);
    });
  });
});
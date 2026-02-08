import { vi } from 'vitest';

vi.mock('node:fs/promises');
vi.mock('node:os', () => ({
  homedir: () => '/home/testuser',
}));
// Use posix path behavior to match Linux container runtime
vi.mock('node:path', async () => {
  const actual = await vi.importActual<typeof import('node:path')>('node:path');
  return {
    ...actual.posix,
    default: actual.posix,
  };
});

import { readdir, stat } from 'node:fs/promises';
import { discoverSessions } from '#src/services/session-discovery.js';

const mockReaddir = vi.mocked(readdir);
const mockStat = vi.mocked(stat);

describe('discoverSessions', () => {
  it('returns empty array when projects directory does not exist', async () => {
    mockStat.mockRejectedValue(new Error('ENOENT'));

    const result = await discoverSessions();

    expect(result).toEqual([]);
  });

  it('returns empty array when projects directory is empty', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([] as any);

    const result = await discoverSessions();

    expect(result).toEqual([]);
  });

  it('returns DiscoveredSession array for .jsonl files', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([
      { name: 'my-project', isDirectory: () => true },
    ] as any);
    mockReaddir.mockResolvedValueOnce(['session-abc.jsonl'] as any);
    mockStat.mockResolvedValueOnce({
      mtime: new Date('2025-01-15T10:00:00Z'),
    } as any);

    const result = await discoverSessions();

    expect(result).toHaveLength(1);
    expect(result[0]).toEqual({
      sessionId: 'session-abc',
      filePath: '/home/testuser/.claude/projects/my-project/session-abc.jsonl',
      lastModified: '2025-01-15T10:00:00.000Z',
    });
  });

  it('ignores non-.jsonl files', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([
      { name: 'my-project', isDirectory: () => true },
    ] as any);
    mockReaddir.mockResolvedValueOnce(['notes.txt', 'config.json'] as any);

    const result = await discoverSessions();

    expect(result).toEqual([]);
  });

  it('skips non-directory entries in projects dir', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([
      { name: 'some-file.txt', isDirectory: () => false },
    ] as any);

    const result = await discoverSessions();

    expect(result).toEqual([]);
  });

  it('sorts by lastModified descending (most recent first)', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([
      { name: 'project-a', isDirectory: () => true },
    ] as any);
    mockReaddir.mockResolvedValueOnce(['old.jsonl', 'new.jsonl'] as any);
    mockStat
      .mockResolvedValueOnce({ mtime: new Date('2025-01-01T00:00:00Z') } as any)
      .mockResolvedValueOnce({ mtime: new Date('2025-06-01T00:00:00Z') } as any);

    const result = await discoverSessions();

    expect(result).toHaveLength(2);
    expect(result[0].sessionId).toBe('new');
    expect(result[1].sessionId).toBe('old');
  });

  it('extracts sessionId from basename without .jsonl extension', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([
      { name: 'proj', isDirectory: () => true },
    ] as any);
    mockReaddir.mockResolvedValueOnce(['abc-123-def.jsonl'] as any);
    mockStat.mockResolvedValueOnce({ mtime: new Date() } as any);

    const result = await discoverSessions();

    expect(result[0].sessionId).toBe('abc-123-def');
  });

  it('skips unreadable subdirectory gracefully', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([
      { name: 'bad-dir', isDirectory: () => true },
      { name: 'good-dir', isDirectory: () => true },
    ] as any);
    mockReaddir
      .mockRejectedValueOnce(new Error('Permission denied'))
      .mockResolvedValueOnce(['session.jsonl'] as any);
    mockStat.mockResolvedValueOnce({ mtime: new Date('2025-03-01T00:00:00Z') } as any);

    const result = await discoverSessions();

    expect(result).toHaveLength(1);
    expect(result[0].sessionId).toBe('session');
  });

  it('skips unstatable .jsonl file gracefully', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([
      { name: 'proj', isDirectory: () => true },
    ] as any);
    mockReaddir.mockResolvedValueOnce(['bad.jsonl', 'good.jsonl'] as any);
    mockStat
      .mockRejectedValueOnce(new Error('Permission denied'))
      .mockResolvedValueOnce({ mtime: new Date('2025-05-01T00:00:00Z') } as any);

    const result = await discoverSessions();

    expect(result).toHaveLength(1);
    expect(result[0].sessionId).toBe('good');
  });

  it('aggregates files from multiple project directories', async () => {
    mockStat.mockResolvedValueOnce({ isDirectory: () => true } as any);
    mockReaddir.mockResolvedValueOnce([
      { name: 'project-a', isDirectory: () => true },
      { name: 'project-b', isDirectory: () => true },
    ] as any);
    mockReaddir
      .mockResolvedValueOnce(['s1.jsonl'] as any)
      .mockResolvedValueOnce(['s2.jsonl'] as any);
    mockStat
      .mockResolvedValueOnce({ mtime: new Date('2025-01-01T00:00:00Z') } as any)
      .mockResolvedValueOnce({ mtime: new Date('2025-02-01T00:00:00Z') } as any);

    const result = await discoverSessions();

    expect(result).toHaveLength(2);
    expect(result[0].sessionId).toBe('s2');
    expect(result[1].sessionId).toBe('s1');
  });
});

import { vi } from 'vitest';
import path from 'node:path';

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

import { readFile, readdir } from 'node:fs/promises';
import files from '#src/routes/files.js';

const mockReadFile = vi.mocked(readFile);
const mockReaddir = vi.mocked(readdir);

function postRead(filePath: string) {
  return files.request('/read', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ filePath }),
  });
}

describe('POST /files/read', () => {
  it('returns content for allowed plan file path', async () => {
    mockReadFile.mockResolvedValue('# My Plan' as any);

    const res = await postRead('/home/testuser/.claude/plans/plan.md');

    expect(res.status).toBe(200);
    const body = await res.json();
    expect(body.content).toBe('# My Plan');
  });

  it('allows ~/.claude/plan.md', async () => {
    mockReadFile.mockResolvedValue('plan content' as any);

    const res = await postRead('/home/testuser/.claude/plan.md');

    expect(res.status).toBe(200);
  });

  it('allows ~/.claude/PLAN.md', async () => {
    mockReadFile.mockResolvedValue('PLAN content' as any);

    const res = await postRead('/home/testuser/.claude/PLAN.md');

    expect(res.status).toBe(200);
  });

  it('returns 403 for disallowed path', async () => {
    const res = await postRead('/etc/shadow');

    expect(res.status).toBe(403);
  });

  it('returns 403 for path traversal attempt', async () => {
    const res = await postRead('/home/testuser/.claude/plans/../../etc/passwd');

    expect(res.status).toBe(403);
  });

  it('returns 400 for missing filePath', async () => {
    const res = await files.request('/read', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({}),
    });

    expect(res.status).toBe(400);
  });

  it('returns 404 when file not found', async () => {
    const enoent = new Error('ENOENT') as NodeJS.ErrnoException;
    enoent.code = 'ENOENT';
    mockReadFile.mockRejectedValue(enoent);

    const res = await postRead('/home/testuser/.claude/plan.md');

    expect(res.status).toBe(404);
  });

  it('returns 500 for other read errors', async () => {
    mockReadFile.mockRejectedValue(new Error('Disk failure'));

    const res = await postRead('/home/testuser/.claude/plan.md');

    expect(res.status).toBe(500);
  });
});

describe('GET /files/plans', () => {
  it('returns .md files in plans directory', async () => {
    mockReaddir.mockResolvedValue(['a.md', 'b.md', 'c.txt'] as any);

    const res = await files.request('/plans');
    const body = await res.json();

    expect(body.files).toHaveLength(2);
    expect(body.files[0].name).toBe('a.md');
    expect(body.files[1].name).toBe('b.md');
  });

  it('returns empty array when no .md files', async () => {
    mockReaddir.mockResolvedValue(['data.json'] as any);

    const res = await files.request('/plans');
    const body = await res.json();

    expect(body.files).toEqual([]);
  });

  it('returns empty files array when directory is missing', async () => {
    mockReaddir.mockRejectedValue(new Error('ENOENT'));

    const res = await files.request('/plans');
    const body = await res.json();

    expect(body).toEqual({ files: [] });
  });
});

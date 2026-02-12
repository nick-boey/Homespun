import { Hono } from 'hono';
import { readFile, readdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { homedir } from 'node:os';
const ALLOWED_PREFIXES = [
    () => resolve(homedir(), '.claude', 'plans') + '/',
    () => resolve(homedir(), '.claude', 'plan.md'),
    () => resolve(homedir(), '.claude', 'PLAN.md'),
];
function isAllowedPath(resolvedPath) {
    return ALLOWED_PREFIXES.some((fn) => resolvedPath.startsWith(fn()));
}
const files = new Hono();
// POST /files/read - Read a file from the container filesystem
files.post('/read', async (c) => {
    const body = await c.req.json();
    if (!body.filePath) {
        return c.json({ message: 'filePath is required' }, 400);
    }
    const resolvedPath = resolve(body.filePath);
    if (!isAllowedPath(resolvedPath)) {
        return c.json({ message: 'Access denied: path outside allowed directories' }, 403);
    }
    try {
        const content = await readFile(resolvedPath, 'utf-8');
        return c.json({ filePath: resolvedPath, content });
    }
    catch (err) {
        if (err.code === 'ENOENT') {
            return c.json({ message: `File not found: ${resolvedPath}` }, 404);
        }
        const message = err instanceof Error ? err.message : String(err);
        return c.json({ message: `Error reading file: ${message}` }, 500);
    }
});
// GET /files/plans - List plan files
files.get('/plans', async (c) => {
    const plansDir = resolve(homedir(), '.claude', 'plans');
    try {
        const entries = await readdir(plansDir);
        const planFiles = entries
            .filter((f) => f.endsWith('.md'))
            .map((f) => ({ path: resolve(plansDir, f), name: f }));
        return c.json({ files: planFiles });
    }
    catch {
        return c.json({ files: [] });
    }
});
export default files;
//# sourceMappingURL=files.js.map
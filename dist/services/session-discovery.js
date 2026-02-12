import { readdir, stat } from 'node:fs/promises';
import { join, basename } from 'node:path';
import { homedir } from 'node:os';
/**
 * Scans ~/.claude/projects/ for .jsonl session files to discover resumable sessions.
 * Mirrors ClaudeSessionDiscovery.cs from the main app.
 */
export async function discoverSessions() {
    const projectsDir = join(homedir(), '.claude', 'projects');
    const sessions = [];
    try {
        await stat(projectsDir);
    }
    catch {
        return sessions;
    }
    try {
        const entries = await readdir(projectsDir, { withFileTypes: true });
        for (const entry of entries) {
            if (!entry.isDirectory())
                continue;
            const projectDir = join(projectsDir, entry.name);
            try {
                const files = await readdir(projectDir);
                for (const file of files) {
                    if (!file.endsWith('.jsonl'))
                        continue;
                    const filePath = join(projectDir, file);
                    const sessionId = basename(file, '.jsonl');
                    try {
                        const fileStat = await stat(filePath);
                        sessions.push({
                            sessionId,
                            filePath,
                            lastModified: fileStat.mtime.toISOString(),
                        });
                    }
                    catch {
                        // Skip files we can't stat
                    }
                }
            }
            catch {
                // Skip directories we can't read
            }
        }
    }
    catch {
        // Return empty if we can't read the projects dir
    }
    // Sort by most recent first
    sessions.sort((a, b) => b.lastModified.localeCompare(a.lastModified));
    return sessions;
}
//# sourceMappingURL=session-discovery.js.map
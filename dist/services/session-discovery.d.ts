export interface DiscoveredSession {
    sessionId: string;
    filePath: string;
    lastModified: string;
}
/**
 * Scans ~/.claude/projects/ for .jsonl session files to discover resumable sessions.
 * Mirrors ClaudeSessionDiscovery.cs from the main app.
 */
export declare function discoverSessions(): Promise<DiscoveredSession[]>;

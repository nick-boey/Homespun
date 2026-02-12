import type { SessionManager } from './session-manager.js';
export declare function formatSSE(event: string, data: unknown): string;
/**
 * Streams raw SDK messages as SSE-formatted strings.
 * Each SDK message is emitted with its `type` field as the SSE event name.
 * The C# consumer handles all content block assembly and question parsing.
 */
export declare function streamSessionEvents(sessionManager: SessionManager, sessionId: string): AsyncGenerator<string>;

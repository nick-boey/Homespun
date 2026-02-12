import { unstable_v2_createSession, type SDKMessage } from '@anthropic-ai/claude-agent-sdk';
import type { SessionInfo } from '../types/index.js';
type Session = ReturnType<typeof unstable_v2_createSession>;
export type SdkPermissionMode = 'default' | 'acceptEdits' | 'plan' | 'bypassPermissions';
export declare function mapPermissionMode(value: string | undefined): SdkPermissionMode;
interface WorkerSession {
    id: string;
    session: Session;
    conversationId?: string;
    mode: string;
    model: string;
    permissionMode: SdkPermissionMode;
    status: 'idle' | 'streaming' | 'closed';
    createdAt: Date;
    lastActivityAt: Date;
}
export declare class SessionManager {
    private sessions;
    create(opts: {
        prompt: string;
        model: string;
        mode: string;
        systemPrompt?: string;
        workingDirectory?: string;
        resumeSessionId?: string;
    }): Promise<WorkerSession>;
    send(sessionId: string, message: string, model?: string, permissionMode?: string): Promise<WorkerSession>;
    stream(sessionId: string): AsyncGenerator<SDKMessage>;
    close(sessionId: string): Promise<void>;
    get(sessionId: string): WorkerSession | undefined;
    list(): SessionInfo[];
    closeAll(): Promise<void>;
}
export {};

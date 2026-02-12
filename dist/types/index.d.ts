export interface StartSessionRequest {
    prompt: string;
    model: string;
    mode: 'Plan' | 'Build';
    systemPrompt?: string;
    resumeSessionId?: string;
    workingDirectory?: string;
}
export interface SendMessageRequest {
    message: string;
    model?: string;
    permissionMode?: 'Default' | 'AcceptEdits' | 'Plan' | 'BypassPermissions';
}
export interface AnswerQuestionRequest {
    answers: Record<string, string>;
}
export interface FileReadRequest {
    filePath: string;
}
export interface SessionInfo {
    sessionId: string;
    conversationId?: string;
    mode: string;
    model: string;
    status: 'idle' | 'streaming' | 'closed';
    createdAt: string;
    lastActivityAt: string;
}
export interface ContainerInfo {
    issueId: string;
    projectId: string;
    projectName: string;
    status: 'idle' | 'active';
}
export interface FileReadResponse {
    filePath: string;
    content: string;
}
export interface PlanFile {
    path: string;
    name: string;
}

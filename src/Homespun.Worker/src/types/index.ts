// Request DTOs matching the current AgentWorker contracts

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
}

export interface AnswerQuestionRequest {
  answers: Record<string, string>;
}

export interface FileReadRequest {
  filePath: string;
}

// Response types

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

// SSE event types matching the C# worker's SseEventTypes
export const SseEventTypes = {
  SessionStarted: 'SessionStarted',
  ContentBlockReceived: 'ContentBlockReceived',
  MessageReceived: 'MessageReceived',
  ResultReceived: 'ResultReceived',
  QuestionReceived: 'QuestionReceived',
  SessionEnded: 'SessionEnded',
  Error: 'Error',
} as const;

// SSE event data types

export interface SessionStartedData {
  sessionId: string;
  conversationId?: string;
}

export interface ContentBlockReceivedData {
  sessionId: string;
  type: 'Text' | 'Thinking' | 'ToolUse' | 'ToolResult';
  text?: string;
  toolName?: string;
  toolInput?: string;
  toolUseId?: string;
  toolSuccess?: boolean;
  index: number;
}

export interface MessageReceivedData {
  sessionId: string;
  role: 'User' | 'Assistant';
  content: ContentBlockReceivedData[];
}

export interface ResultReceivedData {
  sessionId: string;
  totalCostUsd: number;
  durationMs: number;
  conversationId?: string;
}

export interface QuestionOptionData {
  label: string;
  description: string;
}

export interface UserQuestionData {
  question: string;
  header: string;
  options: QuestionOptionData[];
  multiSelect: boolean;
}

export interface QuestionReceivedData {
  sessionId: string;
  questionId: string;
  toolUseId: string;
  questions: UserQuestionData[];
}

export interface SessionEndedData {
  sessionId: string;
  reason?: string;
}

export interface ErrorData {
  sessionId: string;
  message: string;
  code?: string;
  isRecoverable: boolean;
}

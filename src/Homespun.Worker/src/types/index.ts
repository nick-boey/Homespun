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
  permissionMode?: 'Default' | 'AcceptEdits' | 'Plan' | 'BypassPermissions';
}

export interface AnswerQuestionRequest {
  answers: Record<string, string>;
}

export interface ApprovePlanRequest {
  approved: boolean;
}

// SDK tool input types

export interface QuestionOption {
  label: string;
  description: string;
}

export interface UserQuestion {
  question: string;
  header: string;
  options: QuestionOption[];
  multiSelect: boolean;
}

export interface AskUserQuestionInput {
  questions: UserQuestion[];
  answers?: Record<string, string>;
}

export interface ExitPlanModeInput {
  plan?: string;
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

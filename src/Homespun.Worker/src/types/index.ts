// Request DTOs matching the current AgentWorker contracts

import type { WorkflowSessionContext, WorkflowCompleteInput, WorkflowSignalInput } from "../tools/workflow-tools.js";

// Re-export workflow types for convenience
export type { WorkflowSessionContext, WorkflowCompleteInput, WorkflowSignalInput };

export interface StartSessionRequest {
  prompt: string;
  model: string;
  mode: "Plan" | "Build";
  systemPrompt?: string;
  resumeSessionId?: string;
  workingDirectory?: string;
  /** Optional workflow context for sessions running as part of a workflow */
  workflowContext?: WorkflowSessionContext;
}

export interface SendMessageRequest {
  message: string;
  model?: string;
  mode?: "Plan" | "Build";
}

export interface AnswerQuestionRequest {
  answers: Record<string, string>;
}

export interface ApprovePlanRequest {
  approved: boolean;
  keepContext?: boolean;
  feedback?: string;
}

export interface ClearContextRequest {
  prompt: string;
  model: string;
  mode: "Plan" | "Build";
  systemPrompt?: string;
  workingDirectory?: string;
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

export type LastMessageType =
  | "system"
  | "assistant"
  | "user"
  | "stream_event"
  | "result"
  | "workflow_signal"
  | "question_pending"
  | "plan_pending"
  | "status_resumed"
  | "workflow_complete";

export interface SessionInfo {
  sessionId: string;
  conversationId?: string;
  mode: string;
  model: string;
  permissionMode: string;
  status: "idle" | "streaming" | "closed" | "error";
  createdAt: string;
  lastActivityAt: string;
  lastMessageType?: LastMessageType;
  lastMessageSubtype?: string;
}

export interface ContainerInfo {
  issueId: string;
  projectId: string;
  projectName: string;
  status: "idle" | "active";
}

export interface FileReadResponse {
  filePath: string;
  content: string;
}

export interface PlanFile {
  path: string;
  name: string;
}

export interface ActiveSessionResponse {
  hasActiveSession: boolean;
  sessionId?: string;
  status?: "idle" | "streaming" | "closed" | "error";
  mode?: string;
  model?: string;
  permissionMode?: string;
  hasPendingQuestion?: boolean;
  hasPendingPlanApproval?: boolean;
  lastActivityAt?: string;
  lastMessageType?: LastMessageType;
  lastMessageSubtype?: string;
}

/**
 * A2A Protocol Type Definitions
 *
 * Re-exports types from @a2a-js/sdk and defines additional helper types
 * for translating Claude SDK messages to A2A protocol.
 *
 * @see https://a2a-protocol.org/latest/specification/
 */

import type {
  Task,
  Message,
  TextPart,
  DataPart,
  FilePart,
  Part,
  TaskState,
  TaskStatus,
  TaskStatusUpdateEvent,
  TaskArtifactUpdateEvent,
  Artifact,
} from '@a2a-js/sdk';

// Re-export SDK types for convenience
export type {
  Task,
  Message,
  TextPart,
  DataPart,
  FilePart,
  Part,
  TaskState,
  TaskStatus,
  TaskStatusUpdateEvent,
  TaskArtifactUpdateEvent,
  Artifact,
};

/**
 * MIME types used in A2A DataPart for Claude-specific content.
 */
export const A2A_MIME_TYPES = {
  /** JSON data for structured content like tool inputs */
  JSON: 'application/json',
  /** Tool use content block */
  TOOL_USE: 'application/vnd.homespun.tool-use+json',
  /** Tool result content block */
  TOOL_RESULT: 'application/vnd.homespun.tool-result+json',
  /** Thinking/reasoning content */
  THINKING: 'application/vnd.homespun.thinking+json',
  /** Questions for user input */
  QUESTIONS: 'application/vnd.homespun.questions+json',
  /** Plan content */
  PLAN: 'application/vnd.homespun.plan+json',
  /** Session result metadata */
  RESULT: 'application/vnd.homespun.result+json',
} as const;

// Union type for all A2A stream events
export type A2AStreamEvent = Task | Message | TaskStatusUpdateEvent | TaskArtifactUpdateEvent;

// Helper to determine event kind
export type A2AEventKind = 'task' | 'message' | 'status-update' | 'artifact-update';

/**
 * Custom metadata we attach to A2A messages for Homespun-specific data
 */
export interface HomespunMessageMetadata {
  // Original Claude SDK message type
  sdkMessageType?: string;
  // For tool_use messages
  toolName?: string;
  toolUseId?: string;
  // For thinking blocks
  isThinking?: boolean;
  // For streaming content
  isStreaming?: boolean;
  parentToolUseId?: string;
}

/**
 * Custom metadata for questions and plan approvals
 */
export interface InputRequiredMetadata {
  // Type of input required
  inputType: 'question' | 'plan-approval';
  // For questions: the question data
  questions?: QuestionData[];
  // For plan approval: the plan content
  plan?: string;
}

export interface QuestionData {
  question: string;
  header: string;
  options: QuestionOption[];
  multiSelect: boolean;
}

export interface QuestionOption {
  label: string;
  description: string;
}

/**
 * Helper functions for creating A2A events
 */
export function createTask(
  id: string,
  contextId: string,
  state: TaskState,
  statusMessage?: Message
): Task {
  return {
    kind: 'task',
    id,
    contextId,
    status: {
      state,
      timestamp: new Date().toISOString(),
      message: statusMessage,
    },
  };
}

export function createMessage(
  messageId: string,
  role: 'user' | 'agent',
  parts: Part[],
  contextId: string,
  taskId?: string,
  metadata?: Record<string, unknown>
): Message {
  return {
    kind: 'message',
    messageId,
    role,
    parts,
    contextId,
    taskId,
    metadata,
  };
}

export function createTextPart(text: string, metadata?: Record<string, unknown>): TextPart {
  return {
    kind: 'text',
    text,
    metadata,
  };
}

export function createDataPart(data: Record<string, unknown>, metadata?: Record<string, unknown>): DataPart {
  return {
    kind: 'data',
    data,
    metadata,
  };
}

export function createTaskStatusUpdate(
  taskId: string,
  contextId: string,
  state: TaskState,
  isFinal: boolean,
  statusMessage?: Message,
  metadata?: Record<string, unknown>
): TaskStatusUpdateEvent {
  return {
    kind: 'status-update',
    taskId,
    contextId,
    status: {
      state,
      timestamp: new Date().toISOString(),
      message: statusMessage,
    },
    final: isFinal,
    metadata,
  };
}

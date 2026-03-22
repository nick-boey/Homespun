/**
 * SignalR type definitions for both hubs.
 * These types mirror the C# models in Homespun.Shared.
 */

// ============================================================================
// Session Types
// ============================================================================

export type SessionMode = 'plan' | 'build'

export type ClaudeSessionStatus =
  | 'starting'
  | 'runningHooks'
  | 'running'
  | 'waitingForInput'
  | 'waitingForQuestionAnswer'
  | 'waitingForPlanExecution'
  | 'stopped'
  | 'error'

export type ClaudeMessageRole = 'user' | 'assistant'

export type ClaudeContentType = 'text' | 'thinking' | 'toolUse' | 'toolResult'

export interface ToolResultData {
  // Generic tool result data - structure varies by tool
  [key: string]: unknown
}

export interface ClaudeMessageContent {
  type: ClaudeContentType
  text?: string
  thinking?: string
  toolName?: string
  toolInput?: string
  toolResult?: string
  toolSuccess?: boolean
  toolUseId?: string
  parsedToolResult?: ToolResultData
  isStreaming: boolean
  index: number
}

export interface ClaudeMessage {
  id: string
  sessionId: string
  role: ClaudeMessageRole
  content: ClaudeMessageContent[]
  createdAt: string
  isStreaming: boolean
}

export interface QuestionOption {
  label: string
  description: string
}

export interface UserQuestion {
  question: string
  header: string
  options: QuestionOption[]
  multiSelect: boolean
}

export interface PendingQuestion {
  id: string
  toolUseId: string
  questions: UserQuestion[]
  createdAt: string
}

export type SessionType = 'standard' | 'issueAgentModification' | 'issueAgentSystem'

export interface ClaudeSession {
  id: string
  entityId: string
  projectId: string
  workingDirectory: string
  model: string
  mode: SessionMode
  status: ClaudeSessionStatus
  createdAt: string
  lastActivityAt: string
  messages: ClaudeMessage[]
  errorMessage?: string
  conversationId?: string
  systemPrompt?: string
  totalCostUsd: number
  totalDurationMs: number
  sessionType?: SessionType
  pendingQuestion?: PendingQuestion
  planFilePath?: string
  planContent?: string
  hasPendingPlanApproval: boolean
  contextClearMarkers: string[]
}

// ============================================================================
// Notification Types
// ============================================================================

export type NotificationType = 'info' | 'warning' | 'actionRequired'

export interface NotificationDto {
  id: string
  type: NotificationType
  title: string
  message: string
  projectId?: string
  actionLabel?: string
  createdAt: string
  isDismissible: boolean
  deduplicationKey?: string
}

export type IssueChangeType = 'created' | 'updated' | 'deleted'

// ============================================================================
// AG-UI Event Types
// ============================================================================

export interface AGUIBaseEvent {
  type: string
  timestamp: number
}

export interface RunStartedEvent extends AGUIBaseEvent {
  type: 'RUN_STARTED'
  threadId: string
  runId: string
}

export interface RunFinishedEvent extends AGUIBaseEvent {
  type: 'RUN_FINISHED'
  threadId: string
  runId: string
  result?: unknown
}

export interface RunErrorEvent extends AGUIBaseEvent {
  type: 'RUN_ERROR'
  message: string
  code?: string
}

export interface TextMessageStartEvent extends AGUIBaseEvent {
  type: 'TEXT_MESSAGE_START'
  messageId: string
  role: string
}

export interface TextMessageContentEvent extends AGUIBaseEvent {
  type: 'TEXT_MESSAGE_CONTENT'
  messageId: string
  delta: string
}

export interface TextMessageEndEvent extends AGUIBaseEvent {
  type: 'TEXT_MESSAGE_END'
  messageId: string
}

export interface ToolCallStartEvent extends AGUIBaseEvent {
  type: 'TOOL_CALL_START'
  toolCallId: string
  toolCallName: string
  parentMessageId?: string
}

export interface ToolCallArgsEvent extends AGUIBaseEvent {
  type: 'TOOL_CALL_ARGS'
  toolCallId: string
  delta: string
}

export interface ToolCallEndEvent extends AGUIBaseEvent {
  type: 'TOOL_CALL_END'
  toolCallId: string
}

export interface ToolCallResultEvent extends AGUIBaseEvent {
  type: 'TOOL_CALL_RESULT'
  toolCallId: string
  content: string
  messageId?: string
  role: string
}

export interface StateSnapshotEvent extends AGUIBaseEvent {
  type: 'STATE_SNAPSHOT'
  snapshot: unknown
}

export interface StateDeltaEvent extends AGUIBaseEvent {
  type: 'STATE_DELTA'
  delta: unknown
}

export interface CustomEvent extends AGUIBaseEvent {
  type: 'CUSTOM'
  name: string
  value: unknown
}

export type AGUIEvent =
  | RunStartedEvent
  | RunFinishedEvent
  | RunErrorEvent
  | TextMessageStartEvent
  | TextMessageContentEvent
  | TextMessageEndEvent
  | ToolCallStartEvent
  | ToolCallArgsEvent
  | ToolCallEndEvent
  | ToolCallResultEvent
  | StateSnapshotEvent
  | StateDeltaEvent
  | CustomEvent

// Custom event names
export const AGUICustomEventName = {
  QuestionPending: 'QuestionPending',
  PlanPending: 'PlanPending',
  HookExecuted: 'HookExecuted',
  ContextCleared: 'ContextCleared',
} as const

export interface AGUIPlanPendingData {
  planContent: string
  planFilePath?: string
}

// ============================================================================
// Connection Status Types
// ============================================================================

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'

export interface HubConnectionState {
  status: ConnectionStatus
  error?: string
  lastConnectedAt?: Date
  reconnectAttempts: number
}

// Define content types for tool execution
export interface ClaudeMessageContent {
  contentType: 'text' | 'tool_use' | 'tool_result' | 'thinking'
  text?: string
  toolUseId?: string
  name?: string
  input?: Record<string, unknown>
  content?: string
  isError?: boolean
  toolResult?: string
}

export interface ClaudeMessage {
  id: string
  sessionId?: string
  role: 0 | 1 | 'User' | 'Assistant'
  content: ClaudeMessageContent[]
  createdAt: string
}

/**
 * Represents a single tool execution (tool use + optional result)
 */
export interface ToolExecution {
  /** The tool use content block */
  toolUse: ClaudeMessageContent
  /** Optional tool result (may be undefined if still running) */
  toolResult?: ClaudeMessageContent
  /** Whether this tool is currently running */
  isRunning: boolean
}

/**
 * Represents a group of consecutive tool executions from the same assistant message
 */
export interface ToolExecutionGroup {
  /** Unique identifier for the group */
  id: string
  /** Array of tool executions in this group */
  executions: ToolExecution[]
  /** Timestamp from the original assistant message */
  timestamp: string
  /** Original message IDs that were grouped */
  originalMessageIds: string[]
}

/**
 * Display item that can be either a regular message or a tool execution group
 */
export type MessageDisplayItem =
  | { type: 'message'; message: ClaudeMessage }
  | { type: 'toolGroup'; group: ToolExecutionGroup }

import type { ClaudeMessage, ClaudeMessageContent } from '@/api'

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

// Re-export for convenience
export type { ClaudeMessage, ClaudeMessageContent } from '@/api'
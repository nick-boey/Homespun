import type { ClaudeMessage } from '@/api/generated'

export interface SessionTodoItem {
  content: string
  activeForm: string
  status: 'pending' | 'in_progress' | 'completed'
}

interface TodoWriteWrapper {
  todos?: Array<{
    content?: string
    activeForm?: string
    status?: string
  }>
}

/**
 * Extracts the current todo list from session messages by parsing TodoWrite tool calls.
 * Returns the most recent state of todos (from the last TodoWrite call).
 */
export function parseTodosFromMessages(messages: ClaudeMessage[]): SessionTodoItem[] {
  // Find all TodoWrite tool use blocks across all messages
  const allTodoWrites = messages
    .flatMap(msg => msg.content || [])
    .filter(content =>
      content.type === 2 && // ToolUse
      content.toolName === 'TodoWrite'
    )

  if (allTodoWrites.length === 0) {
    return []
  }

  // Get the last TodoWrite to get the most recent todo state
  const lastTodoWrite = allTodoWrites[allTodoWrites.length - 1]

  if (!lastTodoWrite.toolInput) {
    return []
  }

  return parseTodoJson(lastTodoWrite.toolInput)
}

function parseTodoJson(json: string): SessionTodoItem[] {
  try {
    const wrapper: TodoWriteWrapper = JSON.parse(json)

    if (!wrapper.todos || wrapper.todos.length === 0) {
      return []
    }

    return wrapper.todos.map(todo => ({
      content: todo.content || '',
      activeForm: todo.activeForm || '',
      status: parseStatus(todo.status)
    }))
  } catch {
    // Return empty array on JSON parse error
    return []
  }
}

function parseStatus(status?: string): 'pending' | 'in_progress' | 'completed' {
  const normalized = status?.toLowerCase()

  switch (normalized) {
    case 'pending':
      return 'pending'
    case 'in_progress':
      return 'in_progress'
    case 'completed':
      return 'completed'
    default:
      return 'pending'
  }
}
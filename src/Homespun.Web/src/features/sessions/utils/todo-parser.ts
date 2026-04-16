import type { AGUIMessage } from './agui-reducer'

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
export function parseTodosFromMessages(messages: AGUIMessage[]): SessionTodoItem[] {
  const allTodoWrites = messages
    .flatMap((msg) => msg.content)
    .filter((block) => block.kind === 'toolUse' && block.toolName === 'TodoWrite')

  if (allTodoWrites.length === 0) {
    return []
  }

  const lastTodoWrite = allTodoWrites[allTodoWrites.length - 1]
  if (lastTodoWrite.kind !== 'toolUse' || !lastTodoWrite.input) {
    return []
  }

  return parseTodoJson(lastTodoWrite.input)
}

function parseTodoJson(json: string): SessionTodoItem[] {
  try {
    const wrapper: TodoWriteWrapper = JSON.parse(json)

    if (!wrapper.todos || wrapper.todos.length === 0) {
      return []
    }

    return wrapper.todos.map((todo) => ({
      content: todo.content || '',
      activeForm: todo.activeForm || '',
      status: parseStatus(todo.status),
    }))
  } catch {
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

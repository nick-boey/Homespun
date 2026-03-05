import { Circle, CheckCircle, Loader } from 'lucide-react'
import type { ClaudeSession } from '@/api/generated'
import { parseTodosFromMessages } from '../../utils/todo-parser'
import { cn } from '@/lib/utils'

interface SessionTodosTabProps {
  session: ClaudeSession
}

export function SessionTodosTab({ session }: SessionTodosTabProps) {
  const todos = parseTodosFromMessages(session.messages || [])
  const completedCount = todos.filter(t => t.status === 'completed').length

  if (todos.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-8 text-muted-foreground">
        <Circle className="h-12 w-12 mb-3 opacity-50" />
        <p>No tasks tracked in this session</p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2 text-sm">
        <span className="font-semibold text-green-600">{completedCount}</span>
        <span className="text-muted-foreground">/</span>
        <span className="font-semibold">{todos.length}</span>
        <span className="text-muted-foreground ml-1">tasks completed</span>
      </div>

      <div className="space-y-2">
        {todos.map((todo, index) => (
          <div
            key={index}
            className={cn(
              'flex items-start gap-3 p-3 rounded-lg border',
              todo.status === 'in_progress' && 'bg-yellow-50 dark:bg-yellow-950/10'
            )}
          >
            <div className="flex-shrink-0 mt-0.5">
              {todo.status === 'pending' && (
                <Circle className="h-4 w-4 text-muted-foreground" />
              )}
              {todo.status === 'in_progress' && (
                <Loader className="h-4 w-4 text-yellow-600 animate-spin" />
              )}
              {todo.status === 'completed' && (
                <CheckCircle className="h-4 w-4 text-green-600" />
              )}
            </div>

            <div className="flex-1 min-w-0">
              <p
                className={cn(
                  'text-sm',
                  todo.status === 'completed' && 'line-through text-muted-foreground'
                )}
              >
                {todo.content}
              </p>
              {todo.status === 'in_progress' && todo.activeForm && (
                <p className="text-xs text-muted-foreground italic mt-1">
                  {todo.activeForm}
                </p>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
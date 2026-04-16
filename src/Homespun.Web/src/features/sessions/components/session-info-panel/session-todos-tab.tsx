import { Circle, CheckCircle, Loader } from 'lucide-react'
import type { AGUIMessage } from '../../utils/agui-reducer'
import { parseTodosFromMessages } from '../../utils/todo-parser'
import { cn } from '@/lib/utils'

interface SessionTodosTabProps {
  messages: AGUIMessage[]
}

export function SessionTodosTab({ messages }: SessionTodosTabProps) {
  const todos = parseTodosFromMessages(messages)
  const completedCount = todos.filter((t) => t.status === 'completed').length

  if (todos.length === 0) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <Circle className="mb-3 h-12 w-12 opacity-50" />
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
              'flex items-start gap-3 rounded-lg border p-3',
              todo.status === 'in_progress' && 'bg-yellow-50 dark:bg-yellow-950/10'
            )}
          >
            <div className="mt-0.5 flex-shrink-0">
              {todo.status === 'pending' && <Circle className="text-muted-foreground h-4 w-4" />}
              {todo.status === 'in_progress' && (
                <Loader className="h-4 w-4 animate-spin text-yellow-600" />
              )}
              {todo.status === 'completed' && <CheckCircle className="h-4 w-4 text-green-600" />}
            </div>

            <div className="min-w-0 flex-1">
              <p
                className={cn(
                  'text-sm',
                  todo.status === 'completed' && 'text-muted-foreground line-through'
                )}
              >
                {todo.content}
              </p>
              {todo.status === 'in_progress' && todo.activeForm && (
                <p className="text-muted-foreground mt-1 text-xs italic">{todo.activeForm}</p>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

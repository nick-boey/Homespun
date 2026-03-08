import { cn } from '@/lib/utils'
import { CheckCircle, AlertCircle } from 'lucide-react'

interface WriteToolResultProps {
  content: string | unknown
  isError: boolean
  toolInput?: unknown
}

export function WriteToolResult({ content, isError, toolInput }: WriteToolResultProps) {
  const contentStr = typeof content === 'string' ? content : JSON.stringify(content, null, 2)

  if (isError) {
    return (
      <div className={cn('text-destructive border-destructive/50 rounded border p-2 text-sm')}>
        <div className="flex items-start gap-2">
          <AlertCircle className="mt-0.5 size-4" />
          <span>{contentStr}</span>
        </div>
      </div>
    )
  }

  // Extract file path from input if available
  const inputObj = toolInput as Record<string, unknown> | undefined
  const filePath = inputObj?.file_path || inputObj?.path
  const filePathStr = typeof filePath === 'string' ? filePath : undefined

  return (
    <div className="text-muted-foreground text-sm">
      <div className="flex items-start gap-2">
        <CheckCircle className="mt-0.5 size-4 text-green-600 dark:text-green-400" />
        <div>
          <span>{contentStr}</span>
          {filePathStr && (
            <div className="mt-1 text-xs">
              File: <span className="font-mono">{filePathStr}</span>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

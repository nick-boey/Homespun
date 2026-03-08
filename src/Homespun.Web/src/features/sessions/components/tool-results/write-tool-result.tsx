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
      <div className={cn('text-sm text-destructive p-2 rounded border border-destructive/50')}>
        <div className="flex items-start gap-2">
          <AlertCircle className="size-4 mt-0.5" />
          <span>{contentStr}</span>
        </div>
      </div>
    )
  }

  // Extract file path from input if available
  const filePath = (toolInput as Record<string, unknown>)?.file_path ||
                   (toolInput as Record<string, unknown>)?.path as string | undefined

  return (
    <div className="text-sm text-muted-foreground">
      <div className="flex items-start gap-2">
        <CheckCircle className="size-4 mt-0.5 text-green-600 dark:text-green-400" />
        <div>
          <span>{contentStr}</span>
          {filePath && (
            <div className="text-xs mt-1">
              File: <span className="font-mono">{filePath}</span>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
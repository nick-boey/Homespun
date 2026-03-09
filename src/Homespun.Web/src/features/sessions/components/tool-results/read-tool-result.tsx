import { cn } from '@/lib/utils'
import { CodeBlockCode } from '@/components/ui/code-block'

interface ReadToolResultProps {
  content: string | unknown
  isError: boolean
  toolInput?: unknown
}

export function ReadToolResult({ content, isError, toolInput }: ReadToolResultProps) {
  const contentStr = typeof content === 'string' ? content : JSON.stringify(content, null, 2)

  if (isError) {
    return (
      <div className={cn('text-destructive border-destructive/50 rounded border p-2 text-sm')}>
        {contentStr}
      </div>
    )
  }

  // Extract file path from input if available
  const filePath = (toolInput as Record<string, unknown>)?.path as string | undefined

  return (
    <div className="space-y-2">
      {filePath && (
        <div className="text-muted-foreground text-xs">
          File: <span className="font-mono">{filePath}</span>
        </div>
      )}
      <CodeBlockCode
        language="plaintext"
        code={contentStr}
        className="border-border max-h-96 overflow-auto rounded border"
      />
    </div>
  )
}

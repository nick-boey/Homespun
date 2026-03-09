import { cn } from '@/lib/utils'
import { CodeBlockCode } from '@/components/ui/code-block'

interface BashToolResultProps {
  content: string | unknown
  isError: boolean
  toolInput?: unknown
}

export function BashToolResult({ content, isError, toolInput }: BashToolResultProps) {
  const contentStr = typeof content === 'string' ? content : JSON.stringify(content, null, 2)

  // Extract command from input if available
  const command = (toolInput as Record<string, unknown>)?.command as string | undefined

  return (
    <div className="space-y-2">
      {command && (
        <div className="text-muted-foreground text-xs">
          Command: <span className="bg-muted rounded px-1 py-0.5 font-mono">{command}</span>
        </div>
      )}
      <CodeBlockCode
        language="bash"
        code={contentStr}
        className={cn(
          'max-h-96 overflow-auto rounded border',
          isError ? 'border-destructive/50' : 'border-border',
          isError && 'text-destructive'
        )}
      />
    </div>
  )
}

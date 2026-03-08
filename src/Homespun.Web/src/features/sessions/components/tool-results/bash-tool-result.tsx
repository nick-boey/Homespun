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
        <div className="text-xs text-muted-foreground">
          Command: <span className="font-mono bg-muted px-1 py-0.5 rounded">{command}</span>
        </div>
      )}
      <CodeBlockCode
        language="bash"
        code={contentStr}
        className={cn(
          'rounded border max-h-96 overflow-auto',
          isError ? 'border-destructive/50' : 'border-border',
          isError && 'text-destructive'
        )}
      />
    </div>
  )
}
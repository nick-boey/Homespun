import { cn } from '@/lib/utils'
import { CodeBlockCode } from '@/components/ui/code-block'
import { Search } from 'lucide-react'

interface GrepToolResultProps {
  content: string | unknown
  isError: boolean
  toolInput?: unknown
}

export function GrepToolResult({ content, isError, toolInput }: GrepToolResultProps) {
  const contentStr = typeof content === 'string' ? content : JSON.stringify(content, null, 2)

  if (isError) {
    return (
      <div className={cn('text-destructive border-destructive/50 rounded border p-2 text-sm')}>
        {contentStr}
      </div>
    )
  }

  // Extract search pattern from input if available
  const pattern = (toolInput as Record<string, unknown>)?.pattern as string | undefined
  const path = (toolInput as Record<string, unknown>)?.path as string | undefined

  // Count matches if content looks like grep output
  const lines = contentStr.split('\n').filter((line) => line.trim())
  const matchCount = lines.length

  return (
    <div className="space-y-2">
      <div className="text-muted-foreground flex items-center gap-2 text-xs">
        <Search className="size-3" />
        <span>
          {matchCount} {matchCount === 1 ? 'match' : 'matches'}
          {pattern && (
            <>
              {' '}
              for <span className="bg-muted rounded px-1 py-0.5 font-mono">{pattern}</span>
            </>
          )}
          {path && (
            <>
              {' '}
              in <span className="font-mono">{path}</span>
            </>
          )}
        </span>
      </div>

      <CodeBlockCode
        language="plaintext"
        code={contentStr}
        className="border-border max-h-96 overflow-auto rounded border"
      />
    </div>
  )
}

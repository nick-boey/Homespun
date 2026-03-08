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
      <div className={cn('text-sm text-destructive p-2 rounded border border-destructive/50')}>
        {contentStr}
      </div>
    )
  }

  // Extract search pattern from input if available
  const pattern = (toolInput as Record<string, unknown>)?.pattern as string | undefined
  const path = (toolInput as Record<string, unknown>)?.path as string | undefined

  // Count matches if content looks like grep output
  const lines = contentStr.split('\n').filter(line => line.trim())
  const matchCount = lines.length

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2 text-xs text-muted-foreground">
        <Search className="size-3" />
        <span>
          {matchCount} {matchCount === 1 ? 'match' : 'matches'}
          {pattern && <> for <span className="font-mono bg-muted px-1 py-0.5 rounded">{pattern}</span></>}
          {path && <> in <span className="font-mono">{path}</span></>}
        </span>
      </div>

      <CodeBlockCode
        language="plaintext"
        code={contentStr}
        className="rounded border border-border max-h-96 overflow-auto"
      />
    </div>
  )
}
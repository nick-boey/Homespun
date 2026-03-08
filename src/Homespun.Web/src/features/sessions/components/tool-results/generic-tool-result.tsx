import { cn } from '@/lib/utils'
import { CodeBlockCode } from '@/components/ui/code-block'

interface GenericToolResultProps {
  content: string | unknown
  isError: boolean
  toolInput?: unknown
}

export function GenericToolResult({ content, isError }: GenericToolResultProps) {
  const contentStr = typeof content === 'string' ? content : JSON.stringify(content, null, 2)

  if (isError) {
    return (
      <div className={cn('text-sm text-destructive p-2 rounded border border-destructive/50')}>
        <pre className="whitespace-pre-wrap break-words">{contentStr}</pre>
      </div>
    )
  }

  // Try to detect if content is JSON
  let isJson = false
  try {
    if (typeof content !== 'string') {
      isJson = true
    } else {
      JSON.parse(content)
      isJson = true
    }
  } catch {
    // Not JSON
  }

  return (
    <CodeBlockCode
      language={isJson ? 'json' : 'plaintext'}
      code={contentStr}
      className="rounded border border-border max-h-96 overflow-auto"
    />
  )
}
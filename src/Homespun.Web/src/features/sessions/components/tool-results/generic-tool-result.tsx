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
      <div className={cn('text-destructive border-destructive/50 rounded border p-2 text-sm')}>
        <pre className="break-words whitespace-pre-wrap">{contentStr}</pre>
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
      className="border-border max-h-96 overflow-auto rounded border"
    />
  )
}

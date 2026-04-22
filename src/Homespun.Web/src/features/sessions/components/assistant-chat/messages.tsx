import { ChevronRight } from 'lucide-react'
import { MessagePrimitive, useMessage, type ToolCallMessagePartProps } from '@assistant-ui/react'
import { useState } from 'react'

import { CodeBlock } from '@/components/tool-ui/code-block'
import { Markdown } from '@/components/ui/markdown'
import { cn } from '@/lib/utils'
import { useResponsiveProse } from '@/hooks/use-responsive-prose'

function TextPart({ text }: { text: string }) {
  const proseClass = useResponsiveProse({ includeBase: true })
  if (!text || !text.trim()) return null
  return <Markdown className={cn(proseClass, 'max-w-none break-words')}>{text}</Markdown>
}

function ReasoningPart({ text }: { text: string }) {
  if (!text || !text.trim()) return null
  return <div className="text-muted-foreground my-1 text-sm break-words italic">{text}</div>
}

function formatArgs(argsText: string | undefined, args: unknown): string {
  if (argsText && argsText.trim()) return argsText
  if (args == null) return ''
  try {
    return JSON.stringify(args, null, 2)
  } catch {
    return ''
  }
}

function formatResult(result: unknown): string {
  if (result == null) return ''
  if (typeof result === 'string') return result
  try {
    return JSON.stringify(result, null, 2)
  } catch {
    return String(result)
  }
}

/**
 * Fallback renderer for tool calls without a dedicated Toolkit entry
 * (e.g., `ToolSearch`, `Glob`, `TodoWrite`, `mcp__*`). Without this, the
 * assistant bubble would render empty and read as a "blank skeleton".
 * Shows a compact, collapsed card with the tool name and collapsible
 * args/result so the user can still see what happened.
 */
function ToolFallback({
  toolName,
  toolCallId,
  argsText,
  args,
  result,
  isError,
}: ToolCallMessagePartProps) {
  const [expanded, setExpanded] = useState(false)
  const argsDisplay = formatArgs(argsText, args)
  const resultDisplay = formatResult(result)

  return (
    <div
      className={cn(
        'my-2 overflow-hidden rounded border',
        isError ? 'border-destructive/50' : 'border-border'
      )}
    >
      <button
        type="button"
        onClick={() => setExpanded((v) => !v)}
        className="bg-muted/40 hover:bg-muted/70 flex w-full items-center gap-2 px-2 py-1 text-left text-xs"
      >
        <ChevronRight className={cn('size-3 transition-transform', expanded && 'rotate-90')} />
        <span className="font-mono">{toolName}</span>
        {isError && <span className="text-destructive">error</span>}
      </button>
      {expanded && (
        <div className="space-y-2 p-2">
          {argsDisplay && (
            <CodeBlock
              id={`${toolCallId}-fallback-args`}
              language="json"
              code={argsDisplay}
              lineNumbers="hidden"
            />
          )}
          {resultDisplay && (
            <CodeBlock
              id={`${toolCallId}-fallback-result`}
              language="text"
              code={resultDisplay}
              lineNumbers="hidden"
            />
          )}
        </div>
      )}
    </div>
  )
}

function useMessageId(): string {
  return useMessage((m) => m.id)
}

export function UserMessage() {
  const id = useMessageId()
  return (
    <div data-testid={`message-${id}`} className="flex w-full justify-end">
      <div
        data-testid={`message-content-${id}`}
        className="bg-primary text-primary-foreground max-w-[90%] min-w-0 overflow-hidden rounded-lg px-4 py-2 md:max-w-[80%]"
      >
        <MessagePrimitive.Parts components={{ Text: TextPart }} />
      </div>
    </div>
  )
}

export function AssistantMessage() {
  const id = useMessageId()
  return (
    <div data-testid={`message-${id}`} className="flex w-full justify-start">
      <div
        data-testid={`message-content-${id}`}
        className="bg-secondary text-secondary-foreground max-w-[90%] min-w-0 overflow-hidden rounded-lg px-4 py-2 md:max-w-[80%]"
      >
        <MessagePrimitive.Parts
          components={{
            Text: TextPart,
            Reasoning: ReasoningPart,
            tools: { Fallback: ToolFallback },
          }}
        />
      </div>
    </div>
  )
}

export function SystemMessage() {
  const id = useMessageId()
  return (
    <div data-testid={`message-${id}`} className="flex w-full justify-center">
      <div
        data-testid={`message-content-${id}`}
        className="bg-muted text-muted-foreground max-w-[90%] rounded-lg px-4 py-2 text-xs italic"
      >
        <MessagePrimitive.Parts components={{ Text: TextPart }} />
      </div>
    </div>
  )
}

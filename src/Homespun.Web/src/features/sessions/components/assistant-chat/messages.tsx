import { ChevronRight } from 'lucide-react'
import {
  AuiIf,
  MessagePrimitive,
  useMessage,
  type ReasoningMessagePartProps,
  type ToolCallMessagePartProps,
} from '@assistant-ui/react'
import { useState } from 'react'

import { CodeBlock } from '@/components/tool-ui/code-block'
import { Markdown } from '@/components/ui/markdown'
import { cn } from '@/lib/utils'
import { useResponsiveProse } from '@/hooks/use-responsive-prose'
import {
  ReasoningRoot,
  ReasoningTrigger,
  ReasoningContent,
  ReasoningText,
} from '@/components/assistant-ui/reasoning'
import { ToolGroup } from '@/components/assistant-ui/tool-group'

function TextPart({ text }: { text: string }) {
  const proseClass = useResponsiveProse({ includeBase: true })
  if (!text || !text.trim()) return null
  return <Markdown className={cn(proseClass, 'max-w-none break-words')}>{text}</Markdown>
}

/**
 * Reasoning content rendered with markdown so a thinking block reads as prose.
 *
 * `ReasoningGroup` (below) wraps consecutive reasoning parts in an
 * auto-collapsing surface; this component renders the part itself.
 */
function ReasoningPart({ text }: ReasoningMessagePartProps) {
  if (!text || !text.trim()) return null
  return (
    <Markdown className="prose-sm dark:prose-invert max-w-none text-sm leading-relaxed break-words">
      {text}
    </Markdown>
  )
}

/**
 * Wraps consecutive `reasoning` parts in a collapsible surface that auto-expands
 * while it's the only/last streaming part and collapses once a non-reasoning
 * part arrives. Mirrors the AUI registry's `ReasoningGroup` but renders our own
 * `<ReasoningPart>` markdown for the inner content.
 */
function ReasoningGroup({
  children,
  startIndex,
  endIndex,
}: {
  children?: React.ReactNode
  startIndex: number
  endIndex: number
}) {
  const isReasoningStreaming = useMessage((m) => {
    if (m.status?.type !== 'running') return false
    const lastIndex = m.content.length - 1
    if (lastIndex < 0) return false
    const last = m.content[lastIndex]
    if (last?.type !== 'reasoning') return false
    return lastIndex >= startIndex && lastIndex <= endIndex
  })

  return (
    <ReasoningRoot defaultOpen={isReasoningStreaming} variant="ghost">
      <ReasoningTrigger active={isReasoningStreaming} />
      <ReasoningContent aria-busy={isReasoningStreaming}>
        <ReasoningText>{children}</ReasoningText>
      </ReasoningContent>
    </ReasoningRoot>
  )
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
 * assistant area would render empty and read as a "blank skeleton".
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
    <div data-testid={`message-${id}`} data-role="user" className="flex w-full justify-end">
      <div
        data-testid={`message-content-${id}`}
        data-role="user"
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
    <div data-testid={`message-${id}`} data-role="assistant" className="flex w-full justify-start">
      <div
        data-testid={`message-content-${id}`}
        data-role="assistant"
        className="text-foreground max-w-full min-w-0 flex-1 overflow-hidden break-words"
      >
        <MessagePrimitive.Parts
          components={{
            Text: TextPart,
            Reasoning: ReasoningPart,
            ReasoningGroup,
            ToolGroup,
            tools: { Fallback: ToolFallback },
          }}
        />
        <AuiIf condition={(s) => s.message.status?.type === 'running'}>
          <span
            data-testid={`message-streaming-${id}`}
            aria-label="Assistant is responding"
            className="bg-foreground/40 ml-2 inline-block size-1.5 animate-pulse rounded-full align-middle"
          />
        </AuiIf>
      </div>
    </div>
  )
}

export function SystemMessage() {
  const id = useMessageId()
  return (
    <div data-testid={`message-${id}`} data-role="system" className="flex w-full justify-center">
      <div
        data-testid={`message-content-${id}`}
        data-role="system"
        className="bg-muted text-muted-foreground max-w-[90%] rounded-lg px-4 py-2 text-xs italic"
      >
        <MessagePrimitive.Parts components={{ Text: TextPart }} />
      </div>
    </div>
  )
}

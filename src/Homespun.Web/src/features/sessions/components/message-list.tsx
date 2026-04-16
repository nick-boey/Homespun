import { cn } from '@/lib/utils'
import { Markdown } from '@/components/ui/markdown'
import { Skeleton } from '@/components/ui/skeleton'
import { QuestionPanel } from '@/features/questions'
import { useResponsiveProse } from '@/hooks/use-responsive-prose'
import { Card, CardContent } from '@/components/ui/card'
import { Loader2 } from 'lucide-react'
import type { AGUIMessage, AGUIContentBlock } from '../utils/agui-reducer'
import type { PendingQuestion } from '@/types/signalr'
import { useMemo } from 'react'
import { aguiMessagesToDisplayItems } from '../utils/agui-to-display-items'
import { ToolExecutionGroupDisplay } from './tool-execution-group'

export interface MessageListProps {
  messages: AGUIMessage[]
  isLoading?: boolean
  className?: string
  pendingQuestion?: PendingQuestion
  onAnswerQuestion?: (answers: Record<string, string>) => Promise<void>
  isSubmittingAnswer?: boolean
  isProcessingAnswer?: boolean
}

export function MessageList({
  messages,
  isLoading,
  className,
  pendingQuestion,
  onAnswerQuestion,
  isSubmittingAnswer,
  isProcessingAnswer,
}: MessageListProps) {
  // Partition AG-UI messages into display items — chat bubbles for text/thinking
  // blocks and grouped tool-execution cards for tool_use blocks. Must run before any
  // early returns to keep hook order stable.
  const displayItems = useMemo(() => aguiMessagesToDisplayItems(messages), [messages])

  if (isLoading) {
    return <MessageListSkeleton />
  }

  if (messages.length === 0 && !pendingQuestion) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <p className="text-muted-foreground">No messages yet</p>
      </div>
    )
  }

  return (
    <div className={cn('flex flex-col gap-4 p-4', className)}>
      {displayItems.map((item) => {
        if (item.type === 'message') {
          const original = messages.find((m) => m.id === item.message.id)
          if (!original) return null
          return <MessageItem key={original.id} message={original} />
        }
        return (
          <div key={item.group.id} className="flex w-full justify-start">
            <div className="max-w-[90%] md:max-w-[80%]">
              <ToolExecutionGroupDisplay group={item.group} />
            </div>
          </div>
        )
      })}
      {pendingQuestion && onAnswerQuestion && (
        <div className="flex w-full justify-start">
          <div className="max-w-[90%]">
            <QuestionPanel
              pendingQuestion={pendingQuestion}
              onSubmit={onAnswerQuestion}
              isSubmitting={isSubmittingAnswer}
            />
          </div>
        </div>
      )}
      {isProcessingAnswer && (
        <div className="flex w-full justify-start">
          <Card className="max-w-[90%]">
            <CardContent className="flex items-center gap-2 p-4">
              <Loader2 className="h-4 w-4 animate-spin" />
              <span className="muted-foreground-text-sm">Processing your answer...</span>
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  )
}

interface MessageItemProps {
  message: AGUIMessage
}

function MessageItem({ message }: MessageItemProps) {
  // Tool-use blocks are rendered separately as grouped tool-execution cards. Only
  // text/thinking blocks belong in the chat bubble. Drop empty/whitespace-only blocks
  // (except text still streaming — keep so the streaming indicator has something to anchor).
  const renderableBlocks = message.content.filter((b): b is Exclude<AGUIContentBlock, never> => {
    if (b.kind === 'toolUse') return false
    if (b.kind === 'text') return b.text.trim().length > 0 || b.isStreaming
    if (b.kind === 'thinking') return b.text.trim().length > 0
    return false
  })

  if (renderableBlocks.length === 0) return null

  const isAssistant = message.role === 'assistant' || message.role === 'tool'
  const hasStreamingText = renderableBlocks.some((b) => b.kind === 'text' && b.isStreaming)

  return (
    <div
      data-testid={`message-${message.id}`}
      className={cn('flex w-full min-w-0 gap-3', isAssistant ? 'justify-start' : 'justify-end')}
    >
      <div
        className={cn(
          'flex max-w-[90%] min-w-0 flex-col gap-1 md:max-w-[80%]',
          isAssistant ? 'items-start' : 'items-end'
        )}
      >
        <div
          data-testid={`message-content-${message.id}`}
          className={cn(
            'max-w-full min-w-0 overflow-hidden rounded-lg px-4 py-2',
            isAssistant
              ? 'bg-secondary text-secondary-foreground'
              : 'bg-primary text-primary-foreground'
          )}
        >
          {renderableBlocks.map((block, index) => (
            <ContentBlock key={index} block={block} />
          ))}
          {hasStreamingText && (
            <span
              data-testid="streaming-indicator"
              className="ml-1 inline-block h-2 w-2 animate-pulse rounded-full bg-current"
            />
          )}
        </div>
        <span data-testid={`timestamp-${message.id}`} className="text-muted-foreground text-xs">
          {formatTimestamp(message.createdAt)}
        </span>
      </div>
    </div>
  )
}

interface ContentBlockProps {
  block: AGUIContentBlock
}

function ContentBlock({ block }: ContentBlockProps) {
  const responsiveProse = useResponsiveProse({ includeBase: true })

  if (block.kind === 'text') {
    return (
      <Markdown className={cn(responsiveProse, 'max-w-none break-words')}>{block.text}</Markdown>
    )
  }
  if (block.kind === 'thinking') {
    return <div className="text-muted-foreground my-1 text-sm break-words italic">{block.text}</div>
  }
  return null
}

function formatTimestamp(createdAt: number): string {
  const date = new Date(createdAt)
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function MessageListSkeleton() {
  return (
    <div data-testid="message-list-loading" className="flex flex-col gap-4 p-4">
      <div className="flex justify-end">
        <Skeleton className="h-12 w-48 rounded-lg" />
      </div>
      <div className="flex justify-start">
        <Skeleton className="h-24 w-64 rounded-lg" />
      </div>
      <div className="flex justify-end">
        <Skeleton className="h-12 w-56 rounded-lg" />
      </div>
    </div>
  )
}

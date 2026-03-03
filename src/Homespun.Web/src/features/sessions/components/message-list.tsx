import { cn } from '@/lib/utils'
import { Markdown } from '@/components/ui/markdown'
import { Skeleton } from '@/components/ui/skeleton'
import { QuestionPanel } from '@/features/questions'
import type { ClaudeMessage, ClaudeMessageContent, PendingQuestion } from '@/types/signalr'
import { useState } from 'react'

export interface MessageListProps {
  messages: ClaudeMessage[]
  isLoading?: boolean
  className?: string
  pendingQuestion?: PendingQuestion
  onAnswerQuestion?: (answers: Record<string, string>) => Promise<void>
  isSubmittingAnswer?: boolean
}

export function MessageList({
  messages,
  isLoading,
  className,
  pendingQuestion,
  onAnswerQuestion,
  isSubmittingAnswer,
}: MessageListProps) {
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
      {messages.map((message) => (
        <MessageItem key={message.id} message={message} />
      ))}
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
    </div>
  )
}

interface MessageItemProps {
  message: ClaudeMessage
}

function MessageItem({ message }: MessageItemProps) {
  const [isHovered, setIsHovered] = useState(false)
  const isUser = message.role === 'User'

  return (
    <div
      data-testid={`message-${message.id}`}
      className={cn('flex w-full gap-3', isUser ? 'justify-end' : 'justify-start')}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      <div className={cn('flex max-w-[80%] flex-col gap-1', isUser ? 'items-end' : 'items-start')}>
        <div
          data-testid={`message-content-${message.id}`}
          className={cn(
            'rounded-lg px-4 py-2',
            isUser ? 'bg-primary text-primary-foreground' : 'bg-secondary text-secondary-foreground'
          )}
        >
          {message.content.map((content, index) => (
            <ContentBlock key={index} content={content} isUser={isUser} />
          ))}
          {message.isStreaming && (
            <span
              data-testid="streaming-indicator"
              className="ml-1 inline-block h-2 w-2 animate-pulse rounded-full bg-current"
            />
          )}
        </div>
        {isHovered && (
          <span data-testid={`timestamp-${message.id}`} className="text-muted-foreground text-xs">
            {formatTimestamp(message.createdAt)}
          </span>
        )}
      </div>
    </div>
  )
}

interface ContentBlockProps {
  content: ClaudeMessageContent
  isUser: boolean
}

function ContentBlock({ content, isUser }: ContentBlockProps) {
  switch (content.type) {
    case 'Text':
      if (isUser) {
        return <span>{content.text}</span>
      }
      return <Markdown className="prose-sm max-w-none">{content.text ?? ''}</Markdown>

    case 'ToolUse':
      return (
        <div className="bg-muted/50 my-1 rounded border p-2 text-sm">
          <span className="font-mono text-xs">🔧 {content.toolName}</span>
        </div>
      )

    case 'ToolResult':
      return (
        <div className="bg-muted/50 my-1 rounded border p-2 text-sm">
          <span className="text-muted-foreground text-xs">Tool result</span>
        </div>
      )

    case 'Thinking':
      return <div className="text-muted-foreground my-1 text-sm italic">{content.thinking}</div>

    default:
      return null
  }
}

function formatTimestamp(isoString: string): string {
  const date = new Date(isoString)
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

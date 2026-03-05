import { cn } from '@/lib/utils'
import { Markdown } from '@/components/ui/markdown'
import { Skeleton } from '@/components/ui/skeleton'
import { QuestionPanel } from '@/features/questions'
import { Card, CardContent } from '@/components/ui/card'
import { Loader2 } from 'lucide-react'
import type {
  ClaudeMessage,
  ClaudeMessageContent,
  ClaudeContentType,
  ClaudeMessageRole,
  PendingQuestion,
} from '@/types/signalr'
import { useState } from 'react'

// Backend sends numeric enum values, but TypeScript types expect strings.
// These maps normalize both forms to the string representation.
const ContentTypeMap: Record<number | string, ClaudeContentType> = {
  0: 'Text',
  1: 'Thinking',
  2: 'ToolUse',
  3: 'ToolResult',
  Text: 'Text',
  Thinking: 'Thinking',
  ToolUse: 'ToolUse',
  ToolResult: 'ToolResult',
}

const RoleMap: Record<number | string, ClaudeMessageRole> = {
  0: 'User',
  1: 'Assistant',
  User: 'User',
  Assistant: 'Assistant',
}

function normalizeContentType(type: number | string): ClaudeContentType {
  return ContentTypeMap[type] ?? 'Text'
}

function normalizeRole(role: number | string): ClaudeMessageRole {
  return RoleMap[role] ?? 'User'
}

export interface MessageListProps {
  messages: ClaudeMessage[]
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
      {isProcessingAnswer && (
        <div className="flex w-full justify-start">
          <Card className="max-w-[90%]">
            <CardContent className="flex items-center gap-2 p-4">
              <Loader2 className="h-4 w-4 animate-spin" />
              <span className="text-sm text-muted-foreground">Processing your answer...</span>
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  )
}

interface MessageItemProps {
  message: ClaudeMessage
}

/**
 * Determines if a message should be displayed on the assistant side.
 * Tool result messages should appear on the assistant side even though
 * they technically have a "User" role from the backend.
 */
function isAssistantSideMessage(message: ClaudeMessage): boolean {
  const role = normalizeRole(message.role)
  if (role === 'Assistant') return true

  // Tool result messages should be displayed on the assistant side
  // They contain results from tool calls made by the assistant
  const hasOnlyToolResults = message.content.every(
    (c) => normalizeContentType(c.type) === 'ToolResult'
  )
  if (hasOnlyToolResults && message.content.length > 0) return true

  return false
}

function MessageItem({ message }: MessageItemProps) {
  const [isHovered, setIsHovered] = useState(false)
  const isAssistant = isAssistantSideMessage(message)

  return (
    <div
      data-testid={`message-${message.id}`}
      className={cn('flex w-full min-w-0 gap-3', isAssistant ? 'justify-start' : 'justify-end')}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      <div
        className={cn(
          'flex max-w-[80%] min-w-0 flex-col gap-1',
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
          {message.content.map((content, index) => (
            <ContentBlock key={index} content={content} isAssistant={isAssistant} />
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
  isAssistant: boolean
}

function ContentBlock({ content, isAssistant }: ContentBlockProps) {
  const contentType = normalizeContentType(content.type)

  switch (contentType) {
    case 'Text':
      // All text messages are rendered with Markdown for consistent styling
      return (
        <Markdown
          className={cn(
            'prose-sm max-w-none break-words',
            // User messages have inverted colors so we need prose-invert
            !isAssistant && 'prose-invert'
          )}
        >
          {content.text ?? ''}
        </Markdown>
      )

    case 'ToolUse':
      return (
        <div className="bg-muted/50 my-1 overflow-hidden rounded border p-2 text-sm">
          <span className="font-mono text-xs break-all">🔧 {content.toolName}</span>
        </div>
      )

    case 'ToolResult':
      return (
        <div className="bg-muted/50 my-1 overflow-hidden rounded border p-2 text-sm">
          <span className="text-muted-foreground text-xs">Tool result</span>
        </div>
      )

    case 'Thinking':
      return (
        <div className="text-muted-foreground my-1 text-sm break-words italic">
          {content.thinking}
        </div>
      )

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

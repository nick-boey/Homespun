import { cn } from '@/lib/utils'
import { Markdown } from '@/components/ui/markdown'
import { Skeleton } from '@/components/ui/skeleton'
import { QuestionPanel } from '@/features/questions'
import { useResponsiveProse } from '@/hooks/use-responsive-prose'
import { Card, CardContent } from '@/components/ui/card'
import { Loader2 } from 'lucide-react'
import type {
  ClaudeMessage,
  ClaudeMessageContent,
  ClaudeContentType,
  ClaudeMessageRole,
  PendingQuestion,
} from '@/types/signalr'
import { ClaudeContentType as ContentTypeEnum, ClaudeMessageRole as RoleEnum } from '@/api'
import { useMemo } from 'react'
import { groupToolExecutions } from '../utils/tool-execution-grouper'
import { convertSignalRMessages } from '../utils/signalr-message-adapter'
import { ToolExecutionGroupDisplay } from './tool-execution-group'

// Backend sends camelCase string enum values from both API and SignalR.
// These maps normalize all forms to camelCase for internal use.
const ContentTypeMap: Record<string, ClaudeContentType> = {
  // camelCase (canonical)
  [ContentTypeEnum.TEXT]: 'text',
  [ContentTypeEnum.THINKING]: 'thinking',
  [ContentTypeEnum.TOOL_USE]: 'toolUse',
  [ContentTypeEnum.TOOL_RESULT]: 'toolResult',
  // PascalCase (legacy fallback)
  Text: 'text',
  Thinking: 'thinking',
  ToolUse: 'toolUse',
  ToolResult: 'toolResult',
}

const RoleMap: Record<string, ClaudeMessageRole> = {
  // camelCase (canonical)
  [RoleEnum.USER]: 'user',
  [RoleEnum.ASSISTANT]: 'assistant',
  // PascalCase (legacy fallback)
  User: 'user',
  Assistant: 'assistant',
}

function normalizeContentType(type: string): ClaudeContentType {
  return ContentTypeMap[type] ?? 'text'
}

function normalizeRole(role: string): ClaudeMessageRole {
  return RoleMap[role] ?? 'user'
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
  // Process messages through the grouping utility - must be before early returns
  const displayItems = useMemo(() => {
    const convertedMessages = convertSignalRMessages(messages)
    return groupToolExecutions(convertedMessages)
  }, [messages])

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
      {displayItems.map((item, _index) => {
        if (item.type === 'message') {
          // Convert back to SignalR format for existing MessageItem
          const signalRMessage = messages.find((m) => m.id === item.message.id)
          if (signalRMessage) {
            return <MessageItem key={signalRMessage.id} message={signalRMessage} />
          }
          return null
        } else {
          // Render tool group
          return (
            <div key={item.group.id} className="flex w-full justify-start">
              <div className="max-w-[90%] md:max-w-[80%]">
                <ToolExecutionGroupDisplay group={item.group} />
              </div>
            </div>
          )
        }
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
  message: ClaudeMessage
}

/**
 * Determines if a message should be displayed on the assistant side.
 * Tool result messages should appear on the assistant side even though
 * they technically have a "User" role from the backend.
 */
function isAssistantSideMessage(message: ClaudeMessage): boolean {
  const role = normalizeRole(message.role)
  if (role === 'assistant') return true

  // Tool result messages should be displayed on the assistant side
  // They contain results from tool calls made by the assistant
  const hasOnlyToolResults = message.content.every(
    (c) => normalizeContentType(c.type) === 'toolResult'
  )
  if (hasOnlyToolResults && message.content.length > 0) return true

  return false
}

function MessageItem({ message }: MessageItemProps) {
  const isAssistant = isAssistantSideMessage(message)

  // Filter out tool-related content and empty text/thinking blocks
  const nonToolContent = message.content.filter((c) => {
    const type = normalizeContentType(c.type)
    if (type === 'toolUse' || type === 'toolResult') return false
    if (type === 'text' && !c.text?.trim() && !c.isStreaming) return false
    if (type === 'thinking' && !c.thinking?.trim()) return false
    return true
  })

  // Don't render if only tool content
  if (nonToolContent.length === 0) {
    return null
  }

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
          {nonToolContent.map((content, index) => (
            <ContentBlock key={index} content={content} />
          ))}
          {message.isStreaming && (
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
  content: ClaudeMessageContent
}

function ContentBlock({ content }: ContentBlockProps) {
  const contentType = normalizeContentType(content.type)
  const responsiveProse = useResponsiveProse({
    includeBase: true,
  })

  switch (contentType) {
    case 'text':
      // All text messages are rendered with Markdown for consistent styling
      return (
        <Markdown className={cn(responsiveProse, 'max-w-none break-words')}>
          {content.text ?? ''}
        </Markdown>
      )

    case 'toolUse':
      return (
        <div className="bg-muted/50 my-1 overflow-hidden rounded border p-2 text-sm">
          <span className="font-mono text-xs break-all">🔧 {content.toolName}</span>
        </div>
      )

    case 'toolResult':
      return (
        <div className="bg-muted/50 my-1 overflow-hidden rounded border p-2 text-sm">
          <span className="text-muted-foreground text-xs">Tool result</span>
        </div>
      )

    case 'thinking':
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

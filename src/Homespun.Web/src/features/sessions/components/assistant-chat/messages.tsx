import { MessagePrimitive, useMessage } from '@assistant-ui/react'

import { Markdown } from '@/components/ui/markdown'
import { cn } from '@/lib/utils'
import { useResponsiveProse } from '@/hooks/use-responsive-prose'

function TextPart({ text }: { text: string }) {
  const proseClass = useResponsiveProse({ includeBase: true })
  return <Markdown className={cn(proseClass, 'max-w-none break-words')}>{text}</Markdown>
}

function ReasoningPart({ text }: { text: string }) {
  return <div className="text-muted-foreground my-1 text-sm break-words italic">{text}</div>
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

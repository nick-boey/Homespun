import { useState, useEffect, useCallback, useRef } from 'react'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import type {
  ClaudeMessage,
  ClaudeMessageRole,
  TextMessageStartEvent,
  TextMessageContentEvent,
  TextMessageEndEvent,
  ToolCallStartEvent,
  ToolCallArgsEvent,
  ToolCallEndEvent,
  ToolCallResultEvent,
} from '@/types/signalr'

export interface UseSessionMessagesOptions {
  sessionId: string
  initialMessages: ClaudeMessage[]
}

export interface UseSessionMessagesResult {
  messages: ClaudeMessage[]
  addUserMessage: (text: string) => void
}

function capitalizeRole(role: string): ClaudeMessageRole {
  if (role.toLowerCase() === 'user') return 'User'
  return 'Assistant'
}

export function useSessionMessages({
  sessionId,
  initialMessages,
}: UseSessionMessagesOptions): UseSessionMessagesResult {
  const { connection } = useClaudeCodeHub()
  const [messages, setMessages] = useState<ClaudeMessage[]>(initialMessages)
  const messagesRef = useRef(messages)

  // Keep ref in sync with state
  useEffect(() => {
    messagesRef.current = messages
  }, [messages])

  // Update messages when initialMessages change
  useEffect(() => {
    setMessages(initialMessages)
  }, [initialMessages])

  // Handle AG-UI text message start
  const handleTextMessageStart = useCallback(
    (event: TextMessageStartEvent) => {
      const newMessage: ClaudeMessage = {
        id: event.messageId,
        sessionId,
        role: capitalizeRole(event.role),
        content: [{ type: 'Text', text: '', isStreaming: true, index: 0 }],
        createdAt: new Date(event.timestamp).toISOString(),
        isStreaming: true,
      }

      setMessages((prev) => [...prev, newMessage])
    },
    [sessionId]
  )

  // Handle AG-UI text message content (streaming delta)
  const handleTextMessageContent = useCallback((event: TextMessageContentEvent) => {
    setMessages((prev) => {
      const messageIndex = prev.findIndex((m) => m.id === event.messageId)
      if (messageIndex === -1) return prev

      const message = prev[messageIndex]
      const updatedContent = [...message.content]

      // Find the text content block or create one
      let textContentIndex = updatedContent.findIndex((c) => c.type === 'Text' && c.isStreaming)
      if (textContentIndex === -1) {
        textContentIndex = updatedContent.length
        updatedContent.push({ type: 'Text', text: '', isStreaming: true, index: textContentIndex })
      }

      // Append the delta
      updatedContent[textContentIndex] = {
        ...updatedContent[textContentIndex],
        text: (updatedContent[textContentIndex].text ?? '') + event.delta,
      }

      const updatedMessages = [...prev]
      updatedMessages[messageIndex] = {
        ...message,
        content: updatedContent,
      }

      return updatedMessages
    })
  }, [])

  // Handle AG-UI text message end
  const handleTextMessageEnd = useCallback((event: TextMessageEndEvent) => {
    setMessages((prev) => {
      const messageIndex = prev.findIndex((m) => m.id === event.messageId)
      if (messageIndex === -1) return prev

      const message = prev[messageIndex]
      const updatedContent = message.content.map((c) => ({
        ...c,
        isStreaming: false,
      }))

      const updatedMessages = [...prev]
      updatedMessages[messageIndex] = {
        ...message,
        content: updatedContent,
        isStreaming: false,
      }

      return updatedMessages
    })
  }, [])

  // Handle tool call start
  const handleToolCallStart = useCallback(
    (event: ToolCallStartEvent) => {
      setMessages((prev) => {
        // Find the parent message or create a new one
        let messageIndex = prev.findIndex((m) => m.id === event.parentMessageId)

        if (messageIndex === -1) {
          // Create a new assistant message for the tool use
          const newMessage: ClaudeMessage = {
            id: event.parentMessageId ?? `tool-${event.toolCallId}`,
            sessionId,
            role: 'Assistant',
            content: [],
            createdAt: new Date(event.timestamp).toISOString(),
            isStreaming: true,
          }
          messageIndex = prev.length
          prev = [...prev, newMessage]
        }

        const message = prev[messageIndex]
        const updatedContent = [
          ...message.content,
          {
            type: 'ToolUse' as const,
            toolName: event.toolCallName,
            toolUseId: event.toolCallId,
            isStreaming: true,
            index: message.content.length,
          },
        ]

        const updatedMessages = [...prev]
        updatedMessages[messageIndex] = {
          ...message,
          content: updatedContent,
        }

        return updatedMessages
      })
    },
    [sessionId]
  )

  // Handle tool call args (streaming)
  const handleToolCallArgs = useCallback((event: ToolCallArgsEvent) => {
    setMessages((prev) => {
      // Find message with this tool call
      const messageIndex = prev.findIndex((m) =>
        m.content.some((c) => c.type === 'ToolUse' && c.toolUseId === event.toolCallId)
      )
      if (messageIndex === -1) return prev

      const message = prev[messageIndex]
      const updatedContent = message.content.map((c) => {
        if (c.type === 'ToolUse' && c.toolUseId === event.toolCallId) {
          return {
            ...c,
            toolInput: (c.toolInput ?? '') + event.delta,
          }
        }
        return c
      })

      const updatedMessages = [...prev]
      updatedMessages[messageIndex] = {
        ...message,
        content: updatedContent,
      }

      return updatedMessages
    })
  }, [])

  // Handle tool call end
  const handleToolCallEnd = useCallback((event: ToolCallEndEvent) => {
    setMessages((prev) => {
      const messageIndex = prev.findIndex((m) =>
        m.content.some((c) => c.type === 'ToolUse' && c.toolUseId === event.toolCallId)
      )
      if (messageIndex === -1) return prev

      const message = prev[messageIndex]
      const updatedContent = message.content.map((c) => {
        if (c.type === 'ToolUse' && c.toolUseId === event.toolCallId) {
          return { ...c, isStreaming: false }
        }
        return c
      })

      const updatedMessages = [...prev]
      updatedMessages[messageIndex] = {
        ...message,
        content: updatedContent,
      }

      return updatedMessages
    })
  }, [])

  // Handle tool call result
  const handleToolCallResult = useCallback((event: ToolCallResultEvent) => {
    setMessages((prev) => {
      // Find message with this tool call
      const messageIndex = prev.findIndex((m) =>
        m.content.some((c) => c.type === 'ToolUse' && c.toolUseId === event.toolCallId)
      )
      if (messageIndex === -1) return prev

      const message = prev[messageIndex]
      const updatedContent = message.content.map((c) => {
        if (c.type === 'ToolUse' && c.toolUseId === event.toolCallId) {
          return {
            ...c,
            toolResult: event.content,
            isStreaming: false,
          }
        }
        return c
      })

      const updatedMessages = [...prev]
      updatedMessages[messageIndex] = {
        ...message,
        content: updatedContent,
      }

      return updatedMessages
    })
  }, [])

  // Add a user message locally
  const addUserMessage = useCallback(
    (text: string) => {
      const newMessage: ClaudeMessage = {
        id: `user-${Date.now()}`,
        sessionId,
        role: 'User',
        content: [{ type: 'Text', text, isStreaming: false, index: 0 }],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      }

      setMessages((prev) => [...prev, newMessage])
    },
    [sessionId]
  )

  // Register event handlers
  useEffect(() => {
    if (!connection) return

    connection.on('AGUITextMessageStart', handleTextMessageStart)
    connection.on('AGUITextMessageContent', handleTextMessageContent)
    connection.on('AGUITextMessageEnd', handleTextMessageEnd)
    connection.on('AGUIToolCallStart', handleToolCallStart)
    connection.on('AGUIToolCallArgs', handleToolCallArgs)
    connection.on('AGUIToolCallEnd', handleToolCallEnd)
    connection.on('AGUIToolCallResult', handleToolCallResult)

    return () => {
      connection.off('AGUITextMessageStart', handleTextMessageStart)
      connection.off('AGUITextMessageContent', handleTextMessageContent)
      connection.off('AGUITextMessageEnd', handleTextMessageEnd)
      connection.off('AGUIToolCallStart', handleToolCallStart)
      connection.off('AGUIToolCallArgs', handleToolCallArgs)
      connection.off('AGUIToolCallEnd', handleToolCallEnd)
      connection.off('AGUIToolCallResult', handleToolCallResult)
    }
  }, [
    connection,
    handleTextMessageStart,
    handleTextMessageContent,
    handleTextMessageEnd,
    handleToolCallStart,
    handleToolCallArgs,
    handleToolCallEnd,
    handleToolCallResult,
  ])

  return {
    messages,
    addUserMessage,
  }
}

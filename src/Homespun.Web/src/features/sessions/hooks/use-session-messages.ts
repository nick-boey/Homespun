import { useEffect, useCallback, useRef, useReducer } from 'react'
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
  isLoading?: boolean
}

export interface UseSessionMessagesResult {
  messages: ClaudeMessage[]
  addUserMessage: (text: string) => void
}

function capitalizeRole(role: string): ClaudeMessageRole {
  if (role.toLowerCase() === 'user') return 'User'
  return 'Assistant'
}

// Message state and actions
interface MessagesState {
  messages: ClaudeMessage[]
  sessionId: string
  hasReceivedInitialData: boolean
}

type MessagesAction =
  | { type: 'RESET'; sessionId: string }
  | { type: 'SET_INITIAL'; messages: ClaudeMessage[] }
  | { type: 'ADD_MESSAGE'; message: ClaudeMessage }
  | {
      type: 'UPDATE_MESSAGE'
      messageId: string
      updater: (msg: ClaudeMessage) => ClaudeMessage
    }
  | {
      type: 'ADD_OR_UPDATE_MESSAGE'
      messageId: string
      fallbackMessage: ClaudeMessage
      updater: (msg: ClaudeMessage) => ClaudeMessage
    }
  | {
      type: 'UPDATE_MESSAGE_BY_TOOL_ID'
      toolCallId: string
      updater: (msg: ClaudeMessage) => ClaudeMessage
    }

function messagesReducer(state: MessagesState, action: MessagesAction): MessagesState {
  switch (action.type) {
    case 'RESET':
      return { messages: [], sessionId: action.sessionId, hasReceivedInitialData: false }
    case 'SET_INITIAL':
      return { ...state, messages: action.messages, hasReceivedInitialData: true }
    case 'ADD_MESSAGE':
      return { ...state, messages: [...state.messages, action.message] }
    case 'UPDATE_MESSAGE': {
      const messageIndex = state.messages.findIndex((m) => m.id === action.messageId)
      if (messageIndex === -1) return state
      const updatedMessages = [...state.messages]
      updatedMessages[messageIndex] = action.updater(state.messages[messageIndex])
      return { ...state, messages: updatedMessages }
    }
    case 'ADD_OR_UPDATE_MESSAGE': {
      const messageIndex = state.messages.findIndex((m) => m.id === action.messageId)
      if (messageIndex === -1) {
        // Add the fallback message and then apply updater
        const newMessages = [...state.messages, action.fallbackMessage]
        newMessages[newMessages.length - 1] = action.updater(action.fallbackMessage)
        return { ...state, messages: newMessages }
      }
      const updatedMessages = [...state.messages]
      updatedMessages[messageIndex] = action.updater(state.messages[messageIndex])
      return { ...state, messages: updatedMessages }
    }
    case 'UPDATE_MESSAGE_BY_TOOL_ID': {
      const messageIndex = state.messages.findIndex((m) =>
        m.content.some((c) => c.type === 'ToolUse' && c.toolUseId === action.toolCallId)
      )
      if (messageIndex === -1) return state
      const updatedMessages = [...state.messages]
      updatedMessages[messageIndex] = action.updater(state.messages[messageIndex])
      return { ...state, messages: updatedMessages }
    }
    default:
      return state
  }
}

export function useSessionMessages({
  sessionId,
  initialMessages,
  isLoading = false,
}: UseSessionMessagesOptions): UseSessionMessagesResult {
  const { connection } = useClaudeCodeHub()
  const [state, dispatch] = useReducer(messagesReducer, {
    messages: [],
    sessionId,
    hasReceivedInitialData: false,
  })
  const messagesRef = useRef(state.messages)

  // Keep ref in sync with state
  useEffect(() => {
    messagesRef.current = state.messages
  }, [state.messages])

  // Handle session changes and initial data loading
  // This is a synchronization effect that responds to external data changes
  if (sessionId !== state.sessionId) {
    dispatch({ type: 'RESET', sessionId })
  } else if (!isLoading && !state.hasReceivedInitialData) {
    dispatch({ type: 'SET_INITIAL', messages: initialMessages })
  }

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

      dispatch({ type: 'ADD_MESSAGE', message: newMessage })
    },
    [sessionId]
  )

  // Handle AG-UI text message content (streaming delta)
  const handleTextMessageContent = useCallback((event: TextMessageContentEvent) => {
    dispatch({
      type: 'UPDATE_MESSAGE',
      messageId: event.messageId,
      updater: (message) => {
        const updatedContent = [...message.content]

        // Find the text content block or create one
        let textContentIndex = updatedContent.findIndex((c) => c.type === 'Text' && c.isStreaming)
        if (textContentIndex === -1) {
          textContentIndex = updatedContent.length
          updatedContent.push({
            type: 'Text',
            text: '',
            isStreaming: true,
            index: textContentIndex,
          })
        }

        // Append the delta
        updatedContent[textContentIndex] = {
          ...updatedContent[textContentIndex],
          text: (updatedContent[textContentIndex].text ?? '') + event.delta,
        }

        return { ...message, content: updatedContent }
      },
    })
  }, [])

  // Handle AG-UI text message end
  const handleTextMessageEnd = useCallback((event: TextMessageEndEvent) => {
    dispatch({
      type: 'UPDATE_MESSAGE',
      messageId: event.messageId,
      updater: (message) => {
        const updatedContent = message.content.map((c) => ({
          ...c,
          isStreaming: false,
        }))
        return { ...message, content: updatedContent, isStreaming: false }
      },
    })
  }, [])

  // Handle tool call start
  const handleToolCallStart = useCallback(
    (event: ToolCallStartEvent) => {
      const messageId = event.parentMessageId ?? `tool-${event.toolCallId}`
      const fallbackMessage: ClaudeMessage = {
        id: messageId,
        sessionId,
        role: 'Assistant',
        content: [],
        createdAt: new Date(event.timestamp).toISOString(),
        isStreaming: true,
      }

      dispatch({
        type: 'ADD_OR_UPDATE_MESSAGE',
        messageId,
        fallbackMessage,
        updater: (message) => ({
          ...message,
          content: [
            ...message.content,
            {
              type: 'ToolUse' as const,
              toolName: event.toolCallName,
              toolUseId: event.toolCallId,
              isStreaming: true,
              index: message.content.length,
            },
          ],
        }),
      })
    },
    [sessionId]
  )

  // Handle tool call args (streaming)
  const handleToolCallArgs = useCallback((event: ToolCallArgsEvent) => {
    dispatch({
      type: 'UPDATE_MESSAGE_BY_TOOL_ID',
      toolCallId: event.toolCallId,
      updater: (message) => ({
        ...message,
        content: message.content.map((c) => {
          if (c.type === 'ToolUse' && c.toolUseId === event.toolCallId) {
            return {
              ...c,
              toolInput: (c.toolInput ?? '') + event.delta,
            }
          }
          return c
        }),
      }),
    })
  }, [])

  // Handle tool call end
  const handleToolCallEnd = useCallback((event: ToolCallEndEvent) => {
    dispatch({
      type: 'UPDATE_MESSAGE_BY_TOOL_ID',
      toolCallId: event.toolCallId,
      updater: (message) => ({
        ...message,
        content: message.content.map((c) => {
          if (c.type === 'ToolUse' && c.toolUseId === event.toolCallId) {
            return { ...c, isStreaming: false }
          }
          return c
        }),
      }),
    })
  }, [])

  // Handle tool call result
  const handleToolCallResult = useCallback((event: ToolCallResultEvent) => {
    dispatch({
      type: 'UPDATE_MESSAGE_BY_TOOL_ID',
      toolCallId: event.toolCallId,
      updater: (message) => ({
        ...message,
        content: message.content.map((c) => {
          if (c.type === 'ToolUse' && c.toolUseId === event.toolCallId) {
            return {
              ...c,
              toolResult: event.content,
              isStreaming: false,
            }
          }
          return c
        }),
      }),
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

      dispatch({ type: 'ADD_MESSAGE', message: newMessage })
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
    messages: state.messages,
    addUserMessage,
  }
}

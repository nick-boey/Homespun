import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useMemo } from 'react'
import {
  AssistantRuntimeProvider,
  ThreadPrimitive,
  Tools,
  useAui,
  useExternalStoreRuntime,
  type AssistantRuntime,
} from '@assistant-ui/react'

import {
  applyEnvelope,
  initialAGUISessionState,
  type AGUISessionState,
} from '@/features/sessions/utils/agui-reducer'
import { envelopeFixtures } from '@/features/sessions/fixtures/envelopes'
import { convertAGUIMessage } from '@/features/sessions/runtime/convertAGUIMessage'
import type { SessionEventEnvelope } from '@/types/session-events'

import { AssistantMessage, SystemMessage, UserMessage } from './messages'

function fold(envelopes: SessionEventEnvelope[]): AGUISessionState {
  return envelopes.reduce<AGUISessionState>(applyEnvelope, initialAGUISessionState)
}

function useTestRuntime(state: AGUISessionState): AssistantRuntime {
  return useExternalStoreRuntime({
    messages: state.messages,
    isRunning: state.isRunning,
    convertMessage: convertAGUIMessage,
    onNew: async () => {},
    onAddToolResult: () => {},
  })
}

function MessagesHarness({ state }: { state: AGUISessionState }) {
  const runtime = useTestRuntime(state)
  const tools = useMemo(() => Tools({ toolkit: {} }), [])
  const aui = useAui({ tools })
  return (
    <AssistantRuntimeProvider runtime={runtime} aui={aui}>
      <ThreadPrimitive.Root>
        <ThreadPrimitive.Viewport>
          <ThreadPrimitive.Messages
            components={{
              UserMessage,
              AssistantMessage,
              SystemMessage,
            }}
          />
        </ThreadPrimitive.Viewport>
      </ThreadPrimitive.Root>
    </AssistantRuntimeProvider>
  )
}

function renderFixture(name: keyof typeof envelopeFixtures) {
  const state = fold(envelopeFixtures[name])
  return { state, ...render(<MessagesHarness state={state} />) }
}

describe('assistant-chat/messages', () => {
  describe('AssistantMessage — bubble removed', () => {
    it('does not wrap content in a bg-secondary / bg-card / bg-muted class', () => {
      const { state } = renderFixture('simpleTextTurn')
      const assistantMsg = state.messages.find((m) => m.role === 'assistant')!
      const content = screen.getByTestId(`message-content-${assistantMsg.id}`)

      // The bubble removal: assistant content must not have any of these
      // background-fill classes on its enclosing element.
      expect(content.className).not.toMatch(/\bbg-secondary\b/)
      expect(content.className).not.toMatch(/\bbg-card\b/)
      expect(content.className).not.toMatch(/\bbg-muted\b/)
    })

    it('renders text parts directly as markdown', () => {
      renderFixture('simpleTextTurn')
      // The fixture has assistant text "Sure — here's the file list."
      expect(screen.getByText(/here's the file list/i)).toBeInTheDocument()
    })
  })

  describe('UserMessage — bubble preserved', () => {
    it('keeps its bg-primary right-aligned bubble', () => {
      const { state } = renderFixture('simpleTextTurn')
      const userMsg = state.messages.find((m) => m.role === 'user')!
      const content = screen.getByTestId(`message-content-${userMsg.id}`)
      expect(content.className).toMatch(/\bbg-primary\b/)
      expect(content.className).toMatch(/text-primary-foreground/)
    })
  })

  describe('Reasoning surface', () => {
    it('exposes a collapsible disclosure trigger when reasoning + text are present', () => {
      // multiBlockTurn fixture: reasoning followed by text + tool call.
      renderFixture('multiBlockTurn')

      // Reasoning trigger exposes the disclosure as a button labelled
      // "Reasoning" (from the AUI reasoning component).
      const trigger = screen.getByRole('button', { name: /reasoning/i })
      expect(trigger).toBeInTheDocument()
    })

    it('renders the reasoning content text verbatim once the disclosure is expanded', async () => {
      renderFixture('multiBlockTurn')
      // Reasoning collapses by default once a non-reasoning part appears.
      // Expand it to assert the source text is rendered verbatim.
      const trigger = screen.getByRole('button', { name: /reasoning/i })
      await userEvent.click(trigger)
      expect(await screen.findByText(/Considering the shape of the problem/i)).toBeInTheDocument()
    })
  })

  describe('SystemMessage — chip preserved', () => {
    it('keeps the centered bg-muted italic chip', () => {
      // Build a synthetic state with a system message inline.
      const env: SessionEventEnvelope[] = [
        ...envelopeFixtures.simpleTextTurn.slice(0, 1),
        {
          seq: 99,
          sessionId: 'story-session',
          eventId: 'sys-1',
          event: {
            type: 'TEXT_MESSAGE_START',
            messageId: 'sys-msg',
            role: 'system',
            timestamp: 0,
          },
        },
        {
          seq: 100,
          sessionId: 'story-session',
          eventId: 'sys-2',
          event: {
            type: 'TEXT_MESSAGE_CONTENT',
            messageId: 'sys-msg',
            delta: 'system note',
            timestamp: 1,
          },
        },
        {
          seq: 101,
          sessionId: 'story-session',
          eventId: 'sys-3',
          event: { type: 'TEXT_MESSAGE_END', messageId: 'sys-msg', timestamp: 2 },
        },
      ]
      const state = fold(env)
      render(<MessagesHarness state={state} />)
      const sysMsg = state.messages.find((m) => m.role === 'system')!
      const content = screen.getByTestId(`message-content-${sysMsg.id}`)
      expect(content.className).toMatch(/\bbg-muted\b/)
      expect(content.className).toMatch(/italic/)
    })
  })
})

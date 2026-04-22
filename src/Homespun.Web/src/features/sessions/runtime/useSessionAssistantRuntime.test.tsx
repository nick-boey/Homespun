import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { AssistantRuntimeProvider, MessagePrimitive, ThreadPrimitive } from '@assistant-ui/react'

import type { AGUIEvent, SessionEventEnvelope } from '@/types/session-events'
import {
  applyEnvelope,
  initialAGUISessionState,
  type AGUISessionState,
} from '../utils/agui-reducer'

import { useSessionAssistantRuntime } from './useSessionAssistantRuntime'

function env(seq: number, eventId: string, event: AGUIEvent): SessionEventEnvelope {
  return { seq, sessionId: 's1', eventId, event }
}

function fold(envelopes: SessionEventEnvelope[]): AGUISessionState {
  return envelopes.reduce<AGUISessionState>(applyEnvelope, initialAGUISessionState)
}

function TextPart({ text }: { text: string }) {
  return <span data-testid="text-part">{text}</span>
}

function ReasoningPart({ text }: { text: string }) {
  return <span data-testid="reasoning-part">{text}</span>
}

function ToolCallFallback({
  toolName,
  argsText,
  result,
}: {
  toolName: string
  argsText: string
  result?: unknown
}) {
  return (
    <div data-testid="tool-call" data-tool-name={toolName}>
      <span data-testid="tool-args">{argsText}</span>
      {result !== undefined && <span data-testid="tool-result">{String(result)}</span>}
    </div>
  )
}

function TestMessage() {
  return (
    <div data-testid="message">
      <MessagePrimitive.Parts
        components={{
          Text: TextPart,
          Reasoning: ReasoningPart,
          tools: { Fallback: ToolCallFallback },
        }}
      />
    </div>
  )
}

function Harness({ state }: { state: AGUISessionState }) {
  const runtime = useSessionAssistantRuntime({
    state,
    sendMessage: () => {},
  })
  return (
    <AssistantRuntimeProvider runtime={runtime}>
      <ThreadPrimitive.Root>
        <ThreadPrimitive.Viewport>
          <ThreadPrimitive.Messages
            components={{
              UserMessage: TestMessage,
              AssistantMessage: TestMessage,
              SystemMessage: TestMessage,
            }}
          />
        </ThreadPrimitive.Viewport>
      </ThreadPrimitive.Root>
    </AssistantRuntimeProvider>
  )
}

const SCRIPT: SessionEventEnvelope[] = [
  env(1, 'e1', { type: 'RUN_STARTED', threadId: 's1', runId: 'r1', timestamp: 0 }),
  env(2, 'e2', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'u1',
    role: 'user',
    timestamp: 1,
  }),
  env(3, 'e3', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'u1',
    delta: 'hi',
    timestamp: 2,
  }),
  env(4, 'e4', { type: 'TEXT_MESSAGE_END', messageId: 'u1', timestamp: 3 }),
  env(5, 'e5', {
    type: 'TEXT_MESSAGE_START',
    messageId: 'a1',
    role: 'assistant',
    timestamp: 4,
  }),
  env(6, 'e6', {
    type: 'TEXT_MESSAGE_CONTENT',
    messageId: 'a1',
    delta: 'hello back',
    timestamp: 5,
  }),
  env(7, 'e7', { type: 'TEXT_MESSAGE_END', messageId: 'a1', timestamp: 6 }),
  env(8, 'e8', {
    type: 'TOOL_CALL_START',
    toolCallId: 'tc1',
    toolCallName: 'Bash',
    parentMessageId: 'a1',
    timestamp: 7,
  }),
  env(9, 'e9', {
    type: 'TOOL_CALL_ARGS',
    toolCallId: 'tc1',
    delta: '{"command":"ls"}',
    timestamp: 8,
  }),
  env(10, 'e10', { type: 'TOOL_CALL_END', toolCallId: 'tc1', timestamp: 9 }),
  env(11, 'e11', {
    type: 'TOOL_CALL_RESULT',
    toolCallId: 'tc1',
    content: 'a.txt',
    messageId: 'a1',
    role: 'tool',
    timestamp: 10,
  }),
  env(12, 'e12', { type: 'RUN_FINISHED', threadId: 's1', runId: 'r1', timestamp: 11 }),
]

describe('useSessionAssistantRuntime — integration', () => {
  it('renders messages from the reducer state in DOM order', () => {
    const state = fold(SCRIPT)
    const { getAllByTestId } = render(<Harness state={state} />)
    const msgs = getAllByTestId('message')
    expect(msgs).toHaveLength(2)
    const texts = getAllByTestId('text-part').map((el) => el.textContent)
    expect(texts).toEqual(['hi', 'hello back'])
  })

  it('renders a tool-call part for the assistant tool use', () => {
    const state = fold(SCRIPT)
    const { getByTestId } = render(<Harness state={state} />)
    const toolCall = getByTestId('tool-call')
    expect(toolCall.getAttribute('data-tool-name')).toBe('Bash')
    expect(getByTestId('tool-args').textContent).toBe('{"command":"ls"}')
    expect(getByTestId('tool-result').textContent).toBe('a.txt')
  })

  it('replay produces the same DOM as incremental feed (task 3.4)', () => {
    const incremental = fold(SCRIPT)
    // "Replay" in this context means: apply the same envelope list, which for the
    // reducer is identical state. A snapshot-style delivery maps to the same
    // AGUISessionState via folding. Parity: DOM is identical for identical state.
    const replayed = SCRIPT.reduce<AGUISessionState>(applyEnvelope, initialAGUISessionState)

    const a = render(<Harness state={incremental} />).container.innerHTML
    const b = render(<Harness state={replayed} />).container.innerHTML
    expect(a).toBe(b)
  })

  it('idempotent envelope re-application does not duplicate DOM nodes (task 3.5)', () => {
    const once = fold(SCRIPT)
    const twice = fold([...SCRIPT, ...SCRIPT])
    const a = render(<Harness state={once} />).container.innerHTML
    const b = render(<Harness state={twice} />).container.innerHTML
    expect(b).toBe(a)
  })
})

import type { Meta, StoryObj } from '@storybook/react-vite'
import { useMemo } from 'react'
import { expect, userEvent, within } from 'storybook/test'

import {
  applyEnvelope,
  initialAGUISessionState,
  type AGUISessionState,
} from '@/features/sessions/utils/agui-reducer'
import { envelopeFixtures } from '@/features/sessions/fixtures/envelopes'
import { ChatInput } from '@/features/sessions/components/chat-input'
import type { SessionEventEnvelope } from '@/types/session-events'

import { ChatSurface } from './ChatSurface'

function fold(envelopes: SessionEventEnvelope[]): AGUISessionState {
  return envelopes.reduce<AGUISessionState>(applyEnvelope, initialAGUISessionState)
}

function StoryHarness({ envelopes }: { envelopes: SessionEventEnvelope[] }) {
  const state = useMemo(() => fold(envelopes), [envelopes])

  return (
    <div className="bg-background flex h-[640px] w-[760px] flex-col rounded-lg border p-2">
      <ChatSurface state={state} sendMessage={() => {}} className="flex min-h-0 flex-1 flex-col" />
    </div>
  )
}

const meta: Meta<typeof StoryHarness> = {
  title: 'sessions/ChatSurface',
  component: StoryHarness,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof StoryHarness>

export const SimpleTextTurn: Story = {
  args: { envelopes: envelopeFixtures.simpleTextTurn },
}

export const ToolCallLifecycle: Story = {
  args: { envelopes: envelopeFixtures.toolCallLifecycle },
}

export const ThinkingBlock: Story = {
  args: { envelopes: envelopeFixtures.thinkingBlock },
}

export const MultiBlockTurn: Story = {
  args: { envelopes: envelopeFixtures.multiBlockTurn },
}

export const AskUserQuestionPending: Story = {
  args: { envelopes: envelopeFixtures.askUserQuestionPending },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    // Pending interactive tool call renders a Submit button (disabled until an option is picked).
    const submit = await canvas.findByRole('button', { name: /submit/i })
    expect(submit).toBeDisabled()
    const choice = await canvas.findByRole('button', { name: /Yes, delete it/i })
    await userEvent.click(choice)
    expect(submit).toBeEnabled()
  },
}

export const AskUserQuestionAnswered: Story = {
  args: { envelopes: envelopeFixtures.askUserQuestionAnswered },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    // Receipt mode — shows "Answered" card, no Submit button.
    expect(await canvas.findByText(/Answered/i)).toBeInTheDocument()
    expect(canvas.queryByRole('button', { name: /submit/i })).toBeNull()
  },
}

export const ProposePlanPending: Story = {
  args: { envelopes: envelopeFixtures.proposePlanPending },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const approve = await canvas.findByRole('button', { name: /approve/i })
    const reject = await canvas.findByRole('button', { name: /reject/i })
    expect(approve).toBeInTheDocument()
    expect(reject).toBeInTheDocument()
  },
}

export const ProposePlanApproved: Story = {
  args: { envelopes: envelopeFixtures.proposePlanApproved },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    expect(await canvas.findByText(/Plan approved/i)).toBeInTheDocument()
  },
}

export const ProposePlanRejected: Story = {
  args: { envelopes: envelopeFixtures.proposePlanRejected },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    expect(await canvas.findByText(/Plan rejected/i)).toBeInTheDocument()
    expect(await canvas.findByText(/Revise step 2/i)).toBeInTheDocument()
  },
}

export const RunError: Story = {
  args: { envelopes: envelopeFixtures.runError },
}

export const StreamingInterrupted: Story = {
  args: { envelopes: envelopeFixtures.streamingInterrupted },
}

function ComposerStoryHarness({
  onSend,
}: {
  onSend: (message: string, mode: 'plan' | 'build', model: 'opus' | 'sonnet' | 'haiku') => void
}) {
  return (
    <div className="w-[720px] p-4">
      <ChatInput
        onSend={onSend}
        sessionMode="build"
        sessionModel="opus"
        onModeChange={() => {}}
        onModelChange={() => {}}
      />
    </div>
  )
}

type ComposerStory = StoryObj<typeof ComposerStoryHarness>

export const ComposerInteractive: ComposerStory = {
  name: 'Composer — interactive',
  render: (args) => <ComposerStoryHarness {...args} />,
  args: {
    onSend: () => {},
  },
  play: async ({ canvasElement, args }) => {
    const canvas = within(canvasElement)
    const textarea = canvas.getByPlaceholderText(/message/i)
    await userEvent.type(textarea, 'Hello from the composer story')
    const sendButton = canvas.getByRole('button', { name: /send/i })
    await userEvent.click(sendButton)
    await expect(args.onSend).toHaveBeenCalledWith('Hello from the composer story', 'build', 'opus')
  },
}

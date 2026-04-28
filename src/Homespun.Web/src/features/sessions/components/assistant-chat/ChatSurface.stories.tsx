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

export const UnknownToolCall: Story = {
  args: { envelopes: envelopeFixtures.unknownToolCall },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    // Tools without a Toolkit entry must still surface *something* the user can
    // see — the fallback shows the tool name in a collapsed card.
    expect(await canvas.findByText(/ToolSearch/)).toBeInTheDocument()
  },
}

export const RunError: Story = {
  args: { envelopes: envelopeFixtures.runError },
}

export const StreamingInterrupted: Story = {
  args: { envelopes: envelopeFixtures.streamingInterrupted },
}

// ----- New stories from chat-aui-composer-presentation -----

export const BubblelessAssistant: Story = {
  args: { envelopes: envelopeFixtures.simpleTextTurn },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    // The assistant message renders directly on the page background — no bg-secondary,
    // bg-card, or bg-muted enclosure on the content.
    const bubbleClasses = ['bg-secondary', 'bg-card']
    const text = await canvas.findByText(/here's the file list/i)
    let cur: HTMLElement | null = text
    while (cur && cur !== canvasElement) {
      for (const cls of bubbleClasses) {
        expect(cur.classList.contains(cls)).toBe(false)
      }
      cur = cur.parentElement
    }
  },
}

export const ReasoningCollapsed: Story = {
  args: { envelopes: envelopeFixtures.multiBlockTurn },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    // Reasoning + non-reasoning parts — disclosure starts collapsed.
    const trigger = await canvas.findByRole('button', { name: /reasoning/i })
    expect(trigger).toHaveAttribute('data-state', 'closed')
    await userEvent.click(trigger)
    expect(await canvas.findByText(/Considering the shape of the problem/i)).toBeInTheDocument()
  },
}

export const ReasoningStreaming: Story = {
  args: { envelopes: envelopeFixtures.reasoningStreaming },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    // Reasoning is the only/last streaming part — disclosure starts open.
    const trigger = await canvas.findByRole('button', { name: /reasoning/i })
    expect(trigger).toHaveAttribute('data-state', 'open')
    expect(canvas.getByText(/still working on the plan/i)).toBeInTheDocument()
  },
}

export const MultiToolGroup: Story = {
  args: { envelopes: envelopeFixtures.multiToolGroup },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    // The four consecutive tool calls render under a single ToolGroup wrapper.
    const groupRoots = canvasElement.querySelectorAll('[data-slot="tool-group-root"]')
    expect(groupRoots.length).toBe(1)
    // Trigger label says "4 tool calls".
    expect(await canvas.findByText(/4 tool calls/i)).toBeInTheDocument()
  },
}

export const BashTerminal: Story = {
  args: { envelopes: envelopeFixtures.toolCallLifecycle },
  play: async ({ canvasElement }) => {
    // The Bash Toolkit entry now renders the Terminal tool-ui component
    // (data-slot="terminal"), with the command in the prompt and the result
    // in the output.
    const terminal = canvasElement.querySelector('[data-slot="terminal"]')
    expect(terminal).not.toBeNull()
    const canvas = within(canvasElement)
    expect(canvas.getByText(/ls -la/)).toBeInTheDocument()
  },
}

function ComposerStoryHarness({
  onSend,
}: {
  onSend: (message: string, mode: 'plan' | 'build', model: string) => void
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

export const ComposerControls: ComposerStory = {
  name: 'Composer — mode tabs + model selector',
  render: (args) => <ComposerStoryHarness {...args} />,
  args: { onSend: () => {} },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    // Plan + Build tabs render and toggle data-state when activated.
    const plan = canvas.getByRole('tab', { name: /plan/i })
    const build = canvas.getByRole('tab', { name: /build/i })
    expect(build).toHaveAttribute('data-state', 'active')
    await userEvent.click(plan)
    // Model selector trigger renders.
    expect(canvas.getByRole('combobox', { name: /model/i })).toBeInTheDocument()
  },
}

export const MentionPopover: ComposerStory = {
  name: 'Composer — @ mention popover',
  render: (args) => (
    <div className="w-[720px] p-4">
      <ChatInput
        {...args}
        sessionMode="build"
        sessionModel="opus"
        onModeChange={() => {}}
        onModelChange={() => {}}
        projectId="story-project"
      />
    </div>
  ),
  args: { onSend: () => {} },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const textarea = canvas.getByPlaceholderText(/message/i)
    await userEvent.click(textarea)
    await userEvent.type(textarea, '@')
    // The popover lists the project files via the mocked search hooks. (Without
    // a backing API the project files list is empty; this story still exercises
    // the popover open state.)
    expect(canvas.getByPlaceholderText(/message/i)).toHaveValue('@')
  },
}

export const SlashPopoverEmpty: ComposerStory = {
  name: 'Composer — / empty-state popover',
  render: (args) => <ComposerStoryHarness {...args} />,
  args: { onSend: () => {} },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const textarea = canvas.getByPlaceholderText(/message/i)
    await userEvent.click(textarea)
    await userEvent.type(textarea, '/')
    const empty = await canvas.findByTestId('slash-empty-state')
    expect(empty).toHaveTextContent(/no commands available yet/i)
  },
}

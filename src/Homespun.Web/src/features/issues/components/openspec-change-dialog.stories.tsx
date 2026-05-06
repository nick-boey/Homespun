import type { Meta, StoryObj } from '@storybook/react-vite'
import { BranchPresence, ChangePhase, type IssueOpenSpecState } from '@/api'
import { OpenSpecChangeDialog } from './openspec-change-dialog'

const meta: Meta<typeof OpenSpecChangeDialog> = {
  title: 'features/issues/OpenSpecChangeDialog',
  component: OpenSpecChangeDialog,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof OpenSpecChangeDialog>

const trigger = (
  <button
    type="button"
    className="cursor-pointer text-2xl text-red-500 hover:opacity-80"
    aria-label="Open phase tree"
  >
    ◐
  </button>
)

const threePhaseState: IssueOpenSpecState = {
  branchState: BranchPresence.WITH_CHANGE,
  changeState: ChangePhase.INCOMPLETE,
  changeName: 'rework-pipeline',
  schemaName: 'demo',
  phases: [
    {
      name: 'Discovery',
      done: 2,
      total: 2,
      tasks: [
        { description: 'Audit existing pipeline', done: true },
        { description: 'Identify bottlenecks', done: true },
      ],
    },
    {
      name: 'Implementation',
      done: 1,
      total: 3,
      tasks: [
        { description: 'Write new module', done: true },
        { description: 'Wire up tests', done: false },
        { description: 'Migrate consumers', done: false },
      ],
    },
    {
      name: 'Rollout',
      done: 0,
      total: 2,
      tasks: [
        { description: 'Deploy to staging', done: false },
        { description: 'Cut over production', done: false },
      ],
    },
  ],
  orphans: null,
}

export const Default: Story = {
  args: { state: threePhaseState, trigger },
  play: async ({ canvasElement }) => {
    const button = canvasElement.querySelector('button[aria-label="Open phase tree"]')
    if (button instanceof HTMLElement) button.click()
  },
}

export const NoPhases: Story = {
  args: {
    state: {
      branchState: BranchPresence.WITH_CHANGE,
      changeState: ChangePhase.READY_TO_APPLY,
      changeName: 'add-feature-x',
      phases: [],
    },
    trigger,
  },
  play: async ({ canvasElement }) => {
    const button = canvasElement.querySelector('button[aria-label="Open phase tree"]')
    if (button instanceof HTMLElement) button.click()
  },
}

export const Archived: Story = {
  args: {
    state: {
      branchState: BranchPresence.WITH_CHANGE,
      changeState: ChangePhase.ARCHIVED,
      changeName: 'shipped-feature',
      phases: [
        {
          name: 'Done',
          done: 1,
          total: 1,
          tasks: [{ description: 'Ship it', done: true }],
        },
      ],
    },
    trigger,
  },
  play: async ({ canvasElement }) => {
    const button = canvasElement.querySelector('button[aria-label="Open phase tree"]')
    if (button instanceof HTMLElement) button.click()
  },
}

import type { Meta, StoryObj } from '@storybook/react-vite'
import type { TaskGraphPhaseRenderLine } from '../services'
import { TaskGraphPhaseSvg } from './task-graph-phase-svg'

function phaseLine(overrides?: Partial<TaskGraphPhaseRenderLine>): TaskGraphPhaseRenderLine {
  return {
    type: 'phase',
    phaseId: 'issue-1::phase::Design',
    parentIssueId: 'issue-1',
    lane: 1,
    phaseName: 'Design',
    done: 1,
    total: 3,
    tasks: [
      { description: 'Draft wireframes', done: true },
      { description: 'Get design review', done: false },
      { description: 'Finalise specs', done: false },
    ],
    ...overrides,
  }
}

const meta: Meta<typeof TaskGraphPhaseSvg> = {
  title: 'features/issues/TaskGraphPhase/PhaseSvg',
  component: TaskGraphPhaseSvg,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof TaskGraphPhaseSvg>

export const Default: Story = {
  args: {
    line: phaseLine(),
    maxLanes: 3,
  },
}

export const Complete: Story = {
  args: {
    line: phaseLine({ done: 3, total: 3 }),
    maxLanes: 3,
  },
}

export const FirstLane: Story = {
  args: {
    line: phaseLine({ lane: 0 }),
    maxLanes: 2,
  },
}

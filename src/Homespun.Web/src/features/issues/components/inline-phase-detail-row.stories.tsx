import type { Meta, StoryObj } from '@storybook/react-vite'
import type { TaskGraphPhaseRenderLine } from '../services'
import { InlinePhaseDetailRow } from './inline-phase-detail-row'

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

const meta: Meta<typeof InlinePhaseDetailRow> = {
  title: 'features/issues/TaskGraphPhase/InlinePhaseDetailRow',
  component: InlinePhaseDetailRow,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof InlinePhaseDetailRow>

export const Default: Story = {
  args: {
    line: phaseLine(),
    maxLanes: 3,
  },
}

export const AllDone: Story = {
  args: {
    line: phaseLine({
      done: 3,
      total: 3,
      tasks: [
        { description: 'Draft wireframes', done: true },
        { description: 'Get design review', done: true },
        { description: 'Finalise specs', done: true },
      ],
    }),
    maxLanes: 3,
  },
}

export const EmptyPhase: Story = {
  args: {
    line: phaseLine({ done: 0, total: 0, tasks: [] }),
    maxLanes: 3,
  },
}

export const ManyTasks: Story = {
  args: {
    line: phaseLine({
      done: 3,
      total: 8,
      tasks: Array.from({ length: 8 }, (_, i) => ({
        description: `Task ${i + 1}: ${i < 3 ? 'completed work item' : 'pending work item'}`,
        done: i < 3,
      })),
    }),
    maxLanes: 3,
  },
}

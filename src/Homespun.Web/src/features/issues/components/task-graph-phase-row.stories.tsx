import type { Meta, StoryObj } from '@storybook/react-vite'
import type { TaskGraphPhaseRenderLine } from '../services'
import { TaskGraphPhaseRow } from './task-graph-phase-row'

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

const meta: Meta<typeof TaskGraphPhaseRow> = {
  title: 'features/issues/TaskGraphPhase/PhaseRow',
  component: TaskGraphPhaseRow,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof TaskGraphPhaseRow>

export const Default: Story = {
  args: {
    line: phaseLine(),
    maxLanes: 3,
    isSelected: false,
    isExpanded: false,
  },
}

export const Selected: Story = {
  args: {
    line: phaseLine(),
    maxLanes: 3,
    isSelected: true,
    isExpanded: false,
  },
}

export const Expanded: Story = {
  args: {
    line: phaseLine(),
    maxLanes: 3,
    isSelected: false,
    isExpanded: true,
  },
}

export const Complete: Story = {
  args: {
    line: phaseLine({ done: 3, total: 3 }),
    maxLanes: 3,
    isSelected: false,
    isExpanded: false,
  },
}

export const SelectedAndExpanded: Story = {
  args: {
    line: phaseLine(),
    maxLanes: 3,
    isSelected: true,
    isExpanded: true,
  },
}

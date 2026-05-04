import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { TaskGraphPhaseRenderLine } from '../services'
import { InlinePhaseDetailRow } from './inline-phase-detail-row'

function phaseLine(overrides?: Partial<TaskGraphPhaseRenderLine>): TaskGraphPhaseRenderLine {
  return {
    type: 'phase',
    phaseId: 'issue-1::phase::Design',
    parentIssueId: 'issue-1',
    lane: 1,
    phaseName: 'Design',
    done: 2,
    total: 3,
    tasks: [
      { description: 'First task', done: true },
      { description: 'Second task', done: true },
      { description: 'Third task', done: false },
    ],
    ...overrides,
  }
}

describe('InlinePhaseDetailRow', () => {
  it('renders all three task descriptions', () => {
    render(<InlinePhaseDetailRow line={phaseLine()} maxLanes={3} />)
    expect(screen.getByText('First task')).toBeInTheDocument()
    expect(screen.getByText('Second task')).toBeInTheDocument()
    expect(screen.getByText('Third task')).toBeInTheDocument()
  })

  it('applies line-through to done tasks', () => {
    render(<InlinePhaseDetailRow line={phaseLine()} maxLanes={3} />)
    expect(screen.getByText('First task')).toHaveClass('line-through')
    expect(screen.getByText('Second task')).toHaveClass('line-through')
    expect(screen.getByText('Third task')).not.toHaveClass('line-through')
  })

  it('renders filled checkbox SVG for done tasks', () => {
    const { container } = render(<InlinePhaseDetailRow line={phaseLine()} maxLanes={3} />)
    const checkboxSvgs = container.querySelectorAll('li svg')
    // First two tasks are done — their SVGs have filled rects
    const firstCheckbox = checkboxSvgs[0]
    const secondCheckbox = checkboxSvgs[1]
    const thirdCheckbox = checkboxSvgs[2]
    expect(firstCheckbox.querySelector('rect[fill="#22c55e"]')).not.toBeNull()
    expect(secondCheckbox.querySelector('rect[fill="#22c55e"]')).not.toBeNull()
    // Third task is not done — outline-only rect (fill="none")
    expect(thirdCheckbox.querySelector('rect[fill="none"]')).not.toBeNull()
  })

  it('task list has max-height and overflow-y-auto classes for scrollability', () => {
    render(<InlinePhaseDetailRow line={phaseLine()} maxLanes={3} />)
    const list = screen.getByTestId('phase-task-list')
    expect(list).toHaveClass('max-h-[400px]')
    expect(list).toHaveClass('overflow-y-auto')
  })

  it('has role="region" and aria-label for accessibility', () => {
    render(<InlinePhaseDetailRow line={phaseLine()} maxLanes={3} />)
    const region = screen.getByRole('region', { name: 'Phase tasks' })
    expect(region).toBeInTheDocument()
  })

  it('renders "No tasks in this phase" when task list is empty', () => {
    render(<InlinePhaseDetailRow line={phaseLine({ tasks: [], done: 0, total: 0 })} maxLanes={3} />)
    expect(screen.getByText('No tasks in this phase.')).toBeInTheDocument()
  })

  it('sets data-phase-id attribute', () => {
    render(<InlinePhaseDetailRow line={phaseLine()} maxLanes={3} />)
    const region = screen.getByTestId('inline-phase-detail-row')
    expect(region).toHaveAttribute('data-phase-id', 'issue-1::phase::Design')
  })
})

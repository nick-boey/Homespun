import { describe, it, expect } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PhaseRollupBadges } from './phase-rollup'
import type { PhaseSummary } from '@/api/generated/types.gen'

const PHASES: PhaseSummary[] = [
  {
    name: '1. Design',
    done: 2,
    total: 2,
    tasks: [
      { description: 'Write proposal', done: true },
      { description: 'Write design', done: true },
    ],
  },
  {
    name: '2. Implement',
    done: 1,
    total: 3,
    tasks: [
      { description: 'Add service', done: true },
      { description: 'Add controller', done: false },
      { description: 'Add tests', done: false },
    ],
  },
]

describe('PhaseRollupBadges', () => {
  it('renders nothing when phases empty', () => {
    const { container } = render(<PhaseRollupBadges changeName="x" phases={[]} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders one badge per phase with done/total', () => {
    render(<PhaseRollupBadges changeName="my-change" phases={PHASES} />)
    const badges = screen.getAllByTestId('phase-badge')
    expect(badges).toHaveLength(2)
    expect(badges[0]).toHaveTextContent('1. Design: 2/2')
    expect(badges[1]).toHaveTextContent('2. Implement: 1/3')
  })

  it('complete phase badge uses green styling', () => {
    render(<PhaseRollupBadges changeName="my-change" phases={PHASES} />)
    const badges = screen.getAllByTestId('phase-badge')
    expect(badges[0].className).toMatch(/green/)
    expect(badges[1].className).not.toMatch(/green/)
  })

  it('clicking a badge opens the detail modal with leaf tasks', async () => {
    const user = userEvent.setup()
    render(<PhaseRollupBadges changeName="my-change" phases={PHASES} />)

    await user.click(screen.getAllByTestId('phase-badge')[1])

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('2. Implement')).toBeInTheDocument()
    expect(within(dialog).getByText('1/3 tasks complete', { exact: false })).toBeInTheDocument()

    const taskItems = within(dialog).getAllByTestId('phase-task')
    expect(taskItems).toHaveLength(3)
    expect(taskItems[0]).toHaveAttribute('data-done', 'true')
    expect(taskItems[1]).toHaveAttribute('data-done', 'false')
  })
})

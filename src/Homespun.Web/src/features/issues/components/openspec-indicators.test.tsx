import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { OpenSpecIndicators } from './openspec-indicators'
import { BranchPresence, ChangePhase } from '@/api'

describe('OpenSpecIndicators', () => {
  it('renders nothing when state is null', () => {
    const { container } = render(<OpenSpecIndicators state={null} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('branch gray when no branch', () => {
    render(
      <OpenSpecIndicators
        state={{ branchState: BranchPresence.NONE, changeState: ChangePhase.NONE }}
      />
    )
    const branch = screen.getByTestId('openspec-branch-symbol')
    expect(branch).toHaveAttribute('data-branch-state', BranchPresence.NONE)
    expect(branch.className).toMatch(/bg-muted-foreground/)
    expect(screen.queryByTestId('openspec-change-symbol')).not.toBeInTheDocument()
  })

  it('branch white when branch exists but no change', () => {
    render(
      <OpenSpecIndicators
        state={{ branchState: BranchPresence.EXISTS, changeState: ChangePhase.NONE }}
      />
    )
    const branch = screen.getByTestId('openspec-branch-symbol')
    expect(branch).toHaveAttribute('data-branch-state', BranchPresence.EXISTS)
    expect(branch.className).toMatch(/bg-white/)
  })

  it('branch amber and red ◐ when change incomplete', () => {
    render(
      <OpenSpecIndicators
        state={{
          branchState: BranchPresence.WITH_CHANGE,
          changeState: ChangePhase.INCOMPLETE,
        }}
      />
    )
    const branch = screen.getByTestId('openspec-branch-symbol')
    expect(branch.className).toMatch(/bg-amber-500/)
    const change = screen.getByTestId('openspec-change-symbol')
    expect(change).toHaveTextContent('◐')
    expect(change.className).toMatch(/text-red-500/)
  })

  it('amber ◐ when ready-to-apply', () => {
    render(
      <OpenSpecIndicators
        state={{
          branchState: BranchPresence.WITH_CHANGE,
          changeState: ChangePhase.READY_TO_APPLY,
        }}
      />
    )
    const change = screen.getByTestId('openspec-change-symbol')
    expect(change).toHaveTextContent('◐')
    expect(change.className).toMatch(/text-amber-500/)
  })

  it('green ● when ready-to-archive', () => {
    render(
      <OpenSpecIndicators
        state={{
          branchState: BranchPresence.WITH_CHANGE,
          changeState: ChangePhase.READY_TO_ARCHIVE,
        }}
      />
    )
    const change = screen.getByTestId('openspec-change-symbol')
    expect(change).toHaveTextContent('●')
    expect(change.className).toMatch(/text-green-500/)
  })

  it('blue ✓ when archived', () => {
    render(
      <OpenSpecIndicators
        state={{
          branchState: BranchPresence.WITH_CHANGE,
          changeState: ChangePhase.ARCHIVED,
        }}
      />
    )
    const change = screen.getByTestId('openspec-change-symbol')
    expect(change).toHaveTextContent('✓')
    expect(change.className).toMatch(/text-blue-500/)
  })
})

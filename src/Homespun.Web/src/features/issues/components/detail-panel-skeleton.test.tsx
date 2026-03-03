import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { DetailPanelSkeleton } from './detail-panel-skeleton'

describe('DetailPanelSkeleton', () => {
  it('renders skeleton with correct test id', () => {
    render(<DetailPanelSkeleton />)

    expect(screen.getByTestId('detail-panel-skeleton')).toBeInTheDocument()
  })

  it('renders multiple skeleton elements with animate-pulse', () => {
    render(<DetailPanelSkeleton />)

    const skeletons = screen
      .getByTestId('detail-panel-skeleton')
      .querySelectorAll('[data-slot="skeleton"]')
    expect(skeletons.length).toBeGreaterThanOrEqual(8)

    skeletons.forEach((skeleton) => {
      expect(skeleton).toHaveClass('animate-pulse')
    })
  })

  it('applies custom className', () => {
    render(<DetailPanelSkeleton className="custom-class" />)

    expect(screen.getByTestId('detail-panel-skeleton')).toHaveClass('custom-class')
  })

  it('shows branch skeleton by default', () => {
    render(<DetailPanelSkeleton />)

    // Branch section has 3 skeletons (label, code, button)
    const container = screen.getByTestId('detail-panel-skeleton')
    const branchSection = container.querySelector('.flex.items-center.gap-2')
    expect(branchSection).toBeInTheDocument()
  })

  it('hides branch skeleton when showBranch is false', () => {
    const { container } = render(<DetailPanelSkeleton showBranch={false} />)

    // Should have fewer skeleton elements
    const skeletons = container.querySelectorAll('[data-slot="skeleton"]')
    const { container: containerWithBranch } = render(<DetailPanelSkeleton showBranch={true} />)
    const skeletonsWithBranch = containerWithBranch.querySelectorAll('[data-slot="skeleton"]')

    expect(skeletons.length).toBeLessThan(skeletonsWithBranch.length)
  })

  it('shows PR skeleton when showPr is true', () => {
    const { container: containerWithPr } = render(<DetailPanelSkeleton showPr={true} />)
    const { container: containerWithoutPr } = render(<DetailPanelSkeleton showPr={false} />)

    const skeletonsWithPr = containerWithPr.querySelectorAll('[data-slot="skeleton"]')
    const skeletonsWithoutPr = containerWithoutPr.querySelectorAll('[data-slot="skeleton"]')

    expect(skeletonsWithPr.length).toBeGreaterThan(skeletonsWithoutPr.length)
  })

  it('shows agent status skeleton when showAgentStatus is true', () => {
    const { container: containerWithAgent } = render(<DetailPanelSkeleton showAgentStatus={true} />)
    const { container: containerWithoutAgent } = render(
      <DetailPanelSkeleton showAgentStatus={false} />
    )

    const skeletonsWithAgent = containerWithAgent.querySelectorAll('[data-slot="skeleton"]')
    const skeletonsWithoutAgent = containerWithoutAgent.querySelectorAll('[data-slot="skeleton"]')

    expect(skeletonsWithAgent.length).toBeGreaterThan(skeletonsWithoutAgent.length)
  })
})

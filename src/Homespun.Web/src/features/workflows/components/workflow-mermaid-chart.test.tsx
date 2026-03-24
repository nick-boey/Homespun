import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { WorkflowMermaidChart } from './workflow-mermaid-chart'
import type { WorkflowStep, StepExecution } from '@/api/generated/types.gen'

const mockRender = vi.fn()

vi.mock('mermaid', () => ({
  default: {
    initialize: vi.fn(),
    render: (...args: unknown[]) => mockRender(...args),
  },
}))

function makeStep(
  overrides: Partial<WorkflowStep> & { id: string; name: string }
): WorkflowStep {
  return { stepType: 'serverAction', ...overrides }
}

describe('WorkflowMermaidChart', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockRender.mockResolvedValue({
      svg: '<svg><g class="node" id="flowchart-a-0"><rect/></g><g class="node" id="flowchart-b-1"><rect/></g></svg>',
    })
  })

  it('shows empty state when no steps are provided', () => {
    render(<WorkflowMermaidChart steps={[]} />)
    expect(screen.getByTestId('mermaid-empty')).toBeInTheDocument()
    expect(screen.getByText('No workflow steps to display.')).toBeInTheDocument()
  })

  it('renders the mermaid chart container', async () => {
    const steps = [makeStep({ id: 'a', name: 'A' })]
    render(<WorkflowMermaidChart steps={steps} />)

    await waitFor(() => {
      expect(mockRender).toHaveBeenCalled()
    })

    expect(screen.getByTestId('mermaid-chart')).toBeInTheDocument()
  })

  it('calls mermaid.render with generated syntax', async () => {
    const steps = [
      makeStep({ id: 'a', name: 'A', stepType: 'agent' }),
      makeStep({ id: 'b', name: 'B', stepType: 'gate' }),
    ]

    render(<WorkflowMermaidChart steps={steps} />)

    await waitFor(() => {
      expect(mockRender).toHaveBeenCalledWith(
        expect.stringContaining('mermaid-'),
        expect.stringContaining('flowchart TD')
      )
    })

    const syntax = mockRender.mock.calls[0][1] as string
    expect(syntax).toContain('a("A")')
    expect(syntax).toContain('b{"B"}')
    expect(syntax).toContain('a --> b')
  })

  it('passes step executions for status highlighting', async () => {
    const steps = [makeStep({ id: 'a', name: 'A' })]
    const stepExecutions: StepExecution[] = [{ stepId: 'a', status: 'running' }]

    render(<WorkflowMermaidChart steps={steps} stepExecutions={stepExecutions} />)

    await waitFor(() => {
      expect(mockRender).toHaveBeenCalled()
    })

    const syntax = mockRender.mock.calls[0][1] as string
    expect(syntax).toContain('style a fill:#3b82f6,color:#fff')
  })

  it('renders SVG content into the container', async () => {
    const steps = [makeStep({ id: 'a', name: 'A' })]

    render(<WorkflowMermaidChart steps={steps} />)

    await waitFor(() => {
      const chart = screen.getByTestId('mermaid-chart')
      expect(chart.querySelector('svg')).toBeInTheDocument()
    })
  })

  it('emits click events when a step node is clicked', async () => {
    const user = userEvent.setup()
    const onStepClick = vi.fn()
    const steps = [makeStep({ id: 'a', name: 'A' })]

    render(<WorkflowMermaidChart steps={steps} onStepClick={onStepClick} />)

    await waitFor(() => {
      expect(screen.getByTestId('mermaid-chart').querySelector('svg')).toBeInTheDocument()
    })

    const nodeEl = screen.getByTestId('mermaid-chart').querySelector('.node')
    expect(nodeEl).not.toBeNull()
    await user.click(nodeEl!)

    expect(onStepClick).toHaveBeenCalledWith('a')
  })

  it('shows error state when mermaid.render fails', async () => {
    mockRender.mockRejectedValue(new Error('Parse error'))
    const steps = [makeStep({ id: 'a', name: 'A' })]

    render(<WorkflowMermaidChart steps={steps} />)

    await waitFor(() => {
      expect(screen.getByTestId('mermaid-error')).toBeInTheDocument()
    })

    expect(screen.getByText(/Parse error/)).toBeInTheDocument()
  })

  it('re-renders when steps change', async () => {
    const steps1 = [makeStep({ id: 'a', name: 'A' })]
    const steps2 = [makeStep({ id: 'a', name: 'A' }), makeStep({ id: 'b', name: 'B' })]

    const { rerender } = render(<WorkflowMermaidChart steps={steps1} />)

    await waitFor(() => {
      expect(mockRender).toHaveBeenCalledTimes(1)
    })

    rerender(<WorkflowMermaidChart steps={steps2} />)

    await waitFor(() => {
      expect(mockRender).toHaveBeenCalledTimes(2)
    })
  })
})

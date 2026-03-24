import { describe, it, expect } from 'vitest'
import { generateMermaidSyntax } from './generate-mermaid'
import type { WorkflowStep, StepExecution } from '@/api/generated/types.gen'

function makeStep(overrides: Partial<WorkflowStep> & { id: string; name: string }): WorkflowStep {
  return { stepType: 'serverAction', ...overrides }
}

describe('generateMermaidSyntax', () => {
  it('generates flowchart header', () => {
    const result = generateMermaidSyntax({ steps: [] })
    expect(result).toBe('flowchart TD')
  })

  it('renders agent steps as rounded rectangles', () => {
    const steps = [makeStep({ id: 'a1', name: 'Run Agent', stepType: 'agent' })]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('a1("Run Agent")')
  })

  it('renders gate steps as diamonds', () => {
    const steps = [makeStep({ id: 'g1', name: 'Check Gate', stepType: 'gate' })]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('g1{"Check Gate"}')
  })

  it('renders serverAction steps as hexagons', () => {
    const steps = [makeStep({ id: 's1', name: 'Deploy', stepType: 'serverAction' })]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('s1{{"Deploy"}}')
  })

  it('connects sequential steps with arrows when no transitions defined', () => {
    const steps = [makeStep({ id: 'a', name: 'A' }), makeStep({ id: 'b', name: 'B' })]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('a --> b')
  })

  it('renders onSuccess transitions with success label', () => {
    const steps = [
      makeStep({
        id: 'a',
        name: 'A',
        onSuccess: { type: 'nextStep' },
      }),
      makeStep({ id: 'b', name: 'B' }),
    ]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('a -->|"success"| b')
  })

  it('renders onFailure goToStep transitions with fail label', () => {
    const steps = [
      makeStep({
        id: 'a',
        name: 'A',
        onFailure: { type: 'goToStep', targetStepId: 'b' },
      }),
      makeStep({ id: 'b', name: 'B' }),
    ]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('a -.->|"fail"| b')
  })

  it('renders goToStep onSuccess transitions as dashed arrows', () => {
    const steps = [
      makeStep({
        id: 'a',
        name: 'A',
        onSuccess: { type: 'goToStep', targetStepId: 'c' },
      }),
      makeStep({ id: 'b', name: 'B' }),
      makeStep({ id: 'c', name: 'C' }),
    ]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('a -.->|"success"| c')
  })

  it('renders retry transitions as self-referencing dashed arrows', () => {
    const steps = [
      makeStep({
        id: 'a',
        name: 'A',
        maxRetries: 3,
        onFailure: { type: 'retry' },
      }),
      makeStep({ id: 'b', name: 'B' }),
    ]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('a -.->|"retry (max 3)"| a')
  })

  it('does not connect steps when onSuccess is exit', () => {
    const steps = [
      makeStep({
        id: 'a',
        name: 'A',
        onSuccess: { type: 'exit' },
      }),
      makeStep({ id: 'b', name: 'B' }),
    ]
    const result = generateMermaidSyntax({ steps })
    expect(result).not.toContain('a --> b')
    expect(result).not.toContain('a -->|"success"| b')
  })

  it('connects to next step by default when only onFailure is defined', () => {
    const steps = [
      makeStep({
        id: 'a',
        name: 'A',
        onFailure: { type: 'goToStep', targetStepId: 'a' },
      }),
      makeStep({ id: 'b', name: 'B' }),
    ]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('a --> b')
    expect(result).toContain('a -.->|"fail"| a')
  })

  it('skips steps with missing id or name', () => {
    const steps: WorkflowStep[] = [
      { id: null, name: 'No ID' },
      { id: 'x', name: null },
    ]
    const result = generateMermaidSyntax({ steps })
    expect(result).toBe('flowchart TD')
  })

  it('skips transitions to non-existent steps', () => {
    const steps = [
      makeStep({
        id: 'a',
        name: 'A',
        onSuccess: { type: 'goToStep', targetStepId: 'missing' },
      }),
    ]
    const result = generateMermaidSyntax({ steps })
    expect(result).not.toContain('missing')
  })

  it('escapes quotes in names', () => {
    const steps = [makeStep({ id: 'a', name: 'Say "hello"' })]
    const result = generateMermaidSyntax({ steps })
    expect(result).toContain('Say #quot;hello#quot;')
    expect(result).not.toContain('Say "hello"')
  })

  describe('execution status highlighting', () => {
    const steps = [makeStep({ id: 'a', name: 'A' }), makeStep({ id: 'b', name: 'B' })]

    it('applies gray fill for pending status', () => {
      const stepExecutions: StepExecution[] = [{ stepId: 'a', status: 'pending' }]
      const result = generateMermaidSyntax({ steps, stepExecutions })
      expect(result).toContain('style a fill:#9ca3af,color:#fff')
    })

    it('applies blue fill for running status', () => {
      const stepExecutions: StepExecution[] = [{ stepId: 'a', status: 'running' }]
      const result = generateMermaidSyntax({ steps, stepExecutions })
      expect(result).toContain('style a fill:#3b82f6,color:#fff')
    })

    it('applies green fill for completed status', () => {
      const stepExecutions: StepExecution[] = [{ stepId: 'a', status: 'completed' }]
      const result = generateMermaidSyntax({ steps, stepExecutions })
      expect(result).toContain('style a fill:#22c55e,color:#fff')
    })

    it('applies red fill for failed status', () => {
      const stepExecutions: StepExecution[] = [{ stepId: 'a', status: 'failed' }]
      const result = generateMermaidSyntax({ steps, stepExecutions })
      expect(result).toContain('style a fill:#ef4444,color:#fff')
    })

    it('applies dashed border for skipped status', () => {
      const stepExecutions: StepExecution[] = [{ stepId: 'a', status: 'skipped' }]
      const result = generateMermaidSyntax({ steps, stepExecutions })
      expect(result).toContain(
        'style a fill:#f3f4f6,stroke:#9ca3af,stroke-dasharray:5 5,color:#6b7280'
      )
    })

    it('applies yellow fill for waitingForInput status', () => {
      const stepExecutions: StepExecution[] = [{ stepId: 'a', status: 'waitingForInput' }]
      const result = generateMermaidSyntax({ steps, stepExecutions })
      expect(result).toContain('style a fill:#eab308,color:#fff')
    })

    it('applies styles to multiple steps with different statuses', () => {
      const stepExecutions: StepExecution[] = [
        { stepId: 'a', status: 'completed' },
        { stepId: 'b', status: 'running' },
      ]
      const result = generateMermaidSyntax({ steps, stepExecutions })
      expect(result).toContain('style a fill:#22c55e,color:#fff')
      expect(result).toContain('style b fill:#3b82f6,color:#fff')
    })

    it('does not add styles when no stepExecutions provided', () => {
      const result = generateMermaidSyntax({ steps })
      expect(result).not.toContain('style')
    })
  })

  it('generates a complete workflow chart', () => {
    const steps: WorkflowStep[] = [
      makeStep({ id: 'verify', name: 'Verify Plan', stepType: 'agent' }),
      makeStep({ id: 'implement', name: 'Implement', stepType: 'agent' }),
      makeStep({
        id: 'review',
        name: 'Review',
        stepType: 'gate',
        onSuccess: { type: 'goToStep', targetStepId: 'merge' },
        onFailure: { type: 'goToStep', targetStepId: 'implement' },
      }),
      makeStep({
        id: 'merge',
        name: 'CI Merge',
        stepType: 'serverAction',
        onSuccess: { type: 'exit' },
        onFailure: { type: 'goToStep', targetStepId: 'implement' },
      }),
    ]
    const stepExecutions: StepExecution[] = [
      { stepId: 'verify', status: 'completed' },
      { stepId: 'implement', status: 'running' },
    ]

    const result = generateMermaidSyntax({ steps, stepExecutions })

    expect(result).toMatchInlineSnapshot(`
      "flowchart TD
          verify("Verify Plan")
          implement("Implement")
          review{"Review"}
          merge{{"CI Merge"}}
          verify --> implement
          implement --> review
          review -.->|"success"| merge
          review -.->|"fail"| implement
          merge -.->|"fail"| implement
          style verify fill:#22c55e,color:#fff
          style implement fill:#3b82f6,color:#fff"
    `)
  })
})

import { useCallback, useEffect, useRef, useState } from 'react'
import mermaid from 'mermaid'
import type { WorkflowStep, StepExecution } from '@/api/generated/types.gen'
import { generateMermaidSyntax } from './generate-mermaid'

export interface WorkflowMermaidChartProps {
  steps: WorkflowStep[]
  stepExecutions?: StepExecution[]
  onStepClick?: (stepId: string) => void
}

let mermaidInitialized = false

function initMermaid() {
  if (mermaidInitialized) return
  mermaid.initialize({
    startOnLoad: false,
    theme: 'dark',
    securityLevel: 'loose',
    flowchart: {
      htmlLabels: true,
      useMaxWidth: true,
    },
  })
  mermaidInitialized = true
}

export function WorkflowMermaidChart({
  steps,
  stepExecutions,
  onStepClick,
}: WorkflowMermaidChartProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [error, setError] = useState<string | null>(null)

  const handleStepClick = useCallback(
    (stepId: string) => {
      onStepClick?.(stepId)
    },
    [onStepClick]
  )

  useEffect(() => {
    if (!containerRef.current) return
    if (steps.length === 0) return

    initMermaid()

    const syntax = generateMermaidSyntax({ steps, stepExecutions })
    const id = `mermaid-${Date.now()}`

    let cancelled = false

    mermaid
      .render(id, syntax)
      .then(({ svg }) => {
        if (cancelled || !containerRef.current) return
        containerRef.current.innerHTML = svg
        setError(null)

        const svgEl = containerRef.current.querySelector('svg')
        if (svgEl) {
          const nodeElements = svgEl.querySelectorAll('.node')
          nodeElements.forEach((el) => {
            const stepId = el.id?.replace(/^flowchart-/, '').replace(/-\d+$/, '')
            if (stepId) {
              el.setAttribute('style', `${el.getAttribute('style') || ''}; cursor: pointer;`)
              el.addEventListener('click', () => handleStepClick(stepId))
            }
          })
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setError(err instanceof Error ? err.message : 'Failed to render chart')
      })

    return () => {
      cancelled = true
    }
  }, [steps, stepExecutions, handleStepClick])

  if (steps.length === 0) {
    return (
      <div className="border-border rounded-lg border p-8 text-center" data-testid="mermaid-empty">
        <p className="text-muted-foreground">No workflow steps to display.</p>
      </div>
    )
  }

  if (error) {
    return (
      <div
        className="border-destructive bg-destructive/10 rounded-lg border p-4"
        data-testid="mermaid-error"
      >
        <p className="text-destructive text-sm">Failed to render workflow chart: {error}</p>
      </div>
    )
  }

  return (
    <div
      ref={containerRef}
      className="mermaid-chart overflow-auto rounded-lg"
      data-testid="mermaid-chart"
    />
  )
}

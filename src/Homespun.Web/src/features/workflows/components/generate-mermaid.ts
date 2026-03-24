import type { WorkflowStep, StepExecution, StepExecutionStatus } from '@/api/generated/types.gen'

export interface MermaidGenerationOptions {
  steps: WorkflowStep[]
  stepExecutions?: StepExecution[]
}

function escapeLabel(label: string): string {
  return label.replace(/"/g, '#quot;')
}

function getStepShape(step: WorkflowStep): { open: string; close: string } {
  switch (step.stepType) {
    case 'agent':
      return { open: '("', close: '")' }
    case 'gate':
      return { open: '{"', close: '"}' }
    case 'serverAction':
      return { open: '{{"', close: '"}}' }
    default:
      return { open: '["', close: '"]' }
  }
}

function getStatusStyle(status: StepExecutionStatus): string | null {
  switch (status) {
    case 'pending':
      return 'fill:#9ca3af,color:#fff'
    case 'running':
      return 'fill:#3b82f6,color:#fff'
    case 'completed':
      return 'fill:#22c55e,color:#fff'
    case 'failed':
      return 'fill:#ef4444,color:#fff'
    case 'skipped':
      return 'fill:#f3f4f6,stroke:#9ca3af,stroke-dasharray:5 5,color:#6b7280'
    case 'waitingForInput':
      return 'fill:#eab308,color:#fff'
    default:
      return null
  }
}

function getTransitionArrow(type?: string): string {
  if (type === 'goToStep') return '-.->'
  if (type === 'retry') return '-.->'
  return '-->'
}

function getTransitionLabel(type?: string, step?: WorkflowStep): string {
  if (type === 'retry' && step?.maxRetries) {
    return `|"retry (max ${step.maxRetries})"|`
  }
  return ''
}

export function generateMermaidSyntax(options: MermaidGenerationOptions): string {
  const { steps, stepExecutions } = options
  const lines: string[] = ['flowchart TD']

  const stepIds = new Set(steps.map((s) => s.id))

  for (const step of steps) {
    if (!step.id || !step.name) continue
    const shape = getStepShape(step)
    lines.push(`    ${step.id}${shape.open}${escapeLabel(step.name)}${shape.close}`)
  }

  for (let i = 0; i < steps.length; i++) {
    const step = steps[i]
    if (!step.id) continue

    const hasOnSuccess = step.onSuccess && step.onSuccess.type !== 'exit'
    const hasOnFailure = step.onFailure && step.onFailure.type !== 'exit'
    const nextStep = i + 1 < steps.length ? steps[i + 1] : null

    if (hasOnSuccess) {
      const target =
        step.onSuccess!.type === 'goToStep'
          ? step.onSuccess!.targetStepId
          : step.onSuccess!.type === 'retry'
            ? step.id
            : nextStep?.id
      if (target && stepIds.has(target)) {
        const arrow = getTransitionArrow(step.onSuccess!.type)
        const label = getTransitionLabel(step.onSuccess!.type, step)
        lines.push(`    ${step.id} ${arrow}${label ? label : '|"success"|'} ${target}`)
      }
    } else if (hasOnFailure) {
      if (nextStep?.id && stepIds.has(nextStep.id)) {
        lines.push(`    ${step.id} --> ${nextStep.id}`)
      }
    } else if (!step.onSuccess && !step.onFailure) {
      if (nextStep?.id && stepIds.has(nextStep.id)) {
        lines.push(`    ${step.id} --> ${nextStep.id}`)
      }
    }

    if (hasOnFailure) {
      const target =
        step.onFailure!.type === 'goToStep'
          ? step.onFailure!.targetStepId
          : step.onFailure!.type === 'retry'
            ? step.id
            : null
      if (target && stepIds.has(target)) {
        const arrow = getTransitionArrow(step.onFailure!.type)
        const label = getTransitionLabel(step.onFailure!.type, step)
        lines.push(`    ${step.id} ${arrow}${label ? label : '|"fail"|'} ${target}`)
      }
    }
  }

  if (stepExecutions && stepExecutions.length > 0) {
    const executionMap = new Map(stepExecutions.map((se) => [se.stepId, se.status]))
    for (const step of steps) {
      if (!step.id) continue
      const status = executionMap.get(step.id)
      if (status) {
        const style = getStatusStyle(status)
        if (style) {
          lines.push(`    style ${step.id} ${style}`)
        }
      }
    }
  }

  return lines.join('\n')
}

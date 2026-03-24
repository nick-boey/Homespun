import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { WorkflowExecutionView } from './workflow-execution-view'
import { Workflows } from '@/api'
import type { WorkflowExecution, WorkflowStep, StepExecution } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Workflows: {
    getApiExecutionsByExecutionId: vi.fn(),
    getApiWorkflowsByWorkflowId: vi.fn(),
    postApiExecutionsByExecutionIdCancel: vi.fn(),
    postApiExecutionsByExecutionIdStepsByStepIdSignal: vi.fn(),
  },
}))

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
    ...props
  }: {
    children: React.ReactNode
    to: string
    params?: Record<string, string>
    [key: string]: unknown
  }) => {
    let href = to
    if (params) {
      for (const [key, value] of Object.entries(params)) {
        href = href.replace(`$${key}`, value)
      }
    }
    return (
      <a href={href} {...props}>
        {children}
      </a>
    )
  },
}))

vi.mock('@/hooks/use-telemetry', () => ({
  useTelemetry: () => ({
    trackEvent: vi.fn(),
    trackPageView: vi.fn(),
    trackException: vi.fn(),
    trackDependency: vi.fn(),
  }),
}))

// Mock the useWorkflowExecution hook
const mockUseWorkflowExecution = vi.fn()
vi.mock('../hooks/use-workflow-execution', () => ({
  useWorkflowExecution: (...args: unknown[]) => mockUseWorkflowExecution(...args),
}))

// Mock mermaid to avoid rendering issues in tests
vi.mock('mermaid', () => ({
  default: {
    initialize: vi.fn(),
    render: vi.fn().mockResolvedValue({ svg: '<svg></svg>' }),
  },
}))

const mockSteps: WorkflowStep[] = [
  {
    id: 'step-1',
    name: 'Build',
    stepType: 'agent',
    prompt: 'Run build',
    sessionMode: 'build',
    onSuccess: { type: 'nextStep' },
    onFailure: { type: 'exit' },
  },
  {
    id: 'step-2',
    name: 'Deploy Approval',
    stepType: 'gate',
    onSuccess: { type: 'nextStep' },
    onFailure: { type: 'exit' },
  },
  {
    id: 'step-3',
    name: 'Deploy',
    stepType: 'serverAction',
    onSuccess: { type: 'exit' },
    onFailure: { type: 'exit' },
  },
]

const mockStepExecutions: StepExecution[] = [
  {
    stepId: 'step-1',
    stepIndex: 0,
    status: 'completed',
    retryCount: 0,
    startedAt: '2026-01-01T12:00:00Z',
    completedAt: '2026-01-01T12:05:00Z',
    durationMs: 300000,
    sessionId: 'session-abc',
  },
  {
    stepId: 'step-2',
    stepIndex: 1,
    status: 'running',
    retryCount: 0,
    startedAt: '2026-01-01T12:05:00Z',
  },
  {
    stepId: 'step-3',
    stepIndex: 2,
    status: 'pending',
    retryCount: 0,
  },
]

const mockExecution: WorkflowExecution = {
  id: 'exec-1',
  workflowId: 'wf-1',
  workflowVersion: 1,
  projectId: 'proj-1',
  status: 'running',
  trigger: { type: 'manual' },
  stepExecutions: mockStepExecutions,
  currentStepIndex: 1,
  createdAt: '2026-01-01T12:00:00Z',
  startedAt: '2026-01-01T12:00:00Z',
  triggeredBy: 'user',
}

function createDefaultHookReturn() {
  return {
    stepStatuses: {
      'step-1': { status: 'completed' as const, stepIndex: 0 },
      'step-2': { status: 'running' as const, stepIndex: 1 },
      'step-3': { status: 'pending' as const, stepIndex: 2 },
    },
    workflowStatus: null as string | null,
    workflowError: null as string | null,
    pendingGate: null as { stepId: string; gateName: string } | null,
    connectionStatus: 'connected' as const,
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('WorkflowExecutionView', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    // Default mock: execution data loads successfully
    ;(Workflows.getApiExecutionsByExecutionId as ReturnType<typeof vi.fn>).mockResolvedValue({
      data: mockExecution,
    })
    ;(Workflows.getApiWorkflowsByWorkflowId as ReturnType<typeof vi.fn>).mockResolvedValue({
      data: { id: 'wf-1', projectId: 'proj-1', title: 'Build Pipeline', steps: mockSteps },
    })

    mockUseWorkflowExecution.mockReturnValue(createDefaultHookReturn())
  })

  it('renders step timeline with step names and statuses', async () => {
    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('Build')).toBeInTheDocument()
    })

    expect(screen.getByText('Deploy Approval')).toBeInTheDocument()
    expect(screen.getByText('Deploy')).toBeInTheDocument()
  })

  it('shows cancel button for running workflows', async () => {
    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument()
    })
  })

  it('calls cancel API when cancel button clicked', async () => {
    const user = userEvent.setup()
    const cancelMock = Workflows.postApiExecutionsByExecutionIdCancel as ReturnType<typeof vi.fn>
    cancelMock.mockResolvedValueOnce({})

    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: /cancel/i }))

    await waitFor(() => {
      expect(cancelMock).toHaveBeenCalledWith(
        expect.objectContaining({
          path: { executionId: 'exec-1' },
        })
      )
    })
  })

  it('shows gate approval card when GatePending event received', async () => {
    mockUseWorkflowExecution.mockReturnValue({
      ...createDefaultHookReturn(),
      pendingGate: { stepId: 'step-2', gateName: 'Deploy Approval' },
    })

    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByTestId('gate-approval-card')).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: /approve/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /reject/i })).toBeInTheDocument()
  })

  it('calls signal API with approved status when approve clicked', async () => {
    const user = userEvent.setup()
    const signalMock = Workflows.postApiExecutionsByExecutionIdStepsByStepIdSignal as ReturnType<
      typeof vi.fn
    >
    signalMock.mockResolvedValueOnce({})

    mockUseWorkflowExecution.mockReturnValue({
      ...createDefaultHookReturn(),
      pendingGate: { stepId: 'step-2', gateName: 'Deploy Approval' },
    })

    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /approve/i })).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: /approve/i }))

    await waitFor(() => {
      expect(signalMock).toHaveBeenCalledWith(
        expect.objectContaining({
          path: { executionId: 'exec-1', stepId: 'step-2' },
          body: expect.objectContaining({ status: 'approved' }),
        })
      )
    })
  })

  it('calls signal API with rejected status when reject clicked', async () => {
    const user = userEvent.setup()
    const signalMock = Workflows.postApiExecutionsByExecutionIdStepsByStepIdSignal as ReturnType<
      typeof vi.fn
    >
    signalMock.mockResolvedValueOnce({})

    mockUseWorkflowExecution.mockReturnValue({
      ...createDefaultHookReturn(),
      pendingGate: { stepId: 'step-2', gateName: 'Deploy Approval' },
    })

    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /reject/i })).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: /reject/i }))

    await waitFor(() => {
      expect(signalMock).toHaveBeenCalledWith(
        expect.objectContaining({
          path: { executionId: 'exec-1', stepId: 'step-2' },
          body: expect.objectContaining({ status: 'rejected' }),
        })
      )
    })
  })

  it('shows agent session link for agent steps with sessionId', async () => {
    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('Build')).toBeInTheDocument()
    })

    const sessionLink = screen.getByTestId('session-link-step-1')
    expect(sessionLink).toBeInTheDocument()
    expect(sessionLink).toHaveAttribute('href', '/sessions/session-abc')
  })

  it('shows duration for completed steps', async () => {
    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('Build')).toBeInTheDocument()
    })

    expect(screen.getByText('5m 0s')).toBeInTheDocument()
  })

  it('shows workflow error message when workflow fails', async () => {
    mockUseWorkflowExecution.mockReturnValue({
      ...createDefaultHookReturn(),
      workflowStatus: 'failed',
      workflowError: 'Pipeline failed at deploy step',
    })

    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('Pipeline failed at deploy step')).toBeInTheDocument()
    })
  })

  it('hides cancel button when workflow is completed', async () => {
    mockUseWorkflowExecution.mockReturnValue({
      ...createDefaultHookReturn(),
      workflowStatus: 'completed',
    })
    ;(Workflows.getApiExecutionsByExecutionId as ReturnType<typeof vi.fn>).mockResolvedValue({
      data: { ...mockExecution, status: 'completed' },
    })

    render(<WorkflowExecutionView executionId="exec-1" projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('Build')).toBeInTheDocument()
    })

    expect(screen.queryByRole('button', { name: /cancel/i })).not.toBeInTheDocument()
  })
})

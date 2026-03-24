import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { WorkflowDetail } from './workflow-detail'
import { Workflows } from '@/api'
import type { WorkflowDefinition, ExecutionSummary } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Workflows: {
    getApiWorkflowsByWorkflowId: vi.fn(),
    getApiWorkflowsByWorkflowIdExecutions: vi.fn(),
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

const mockWorkflow: WorkflowDefinition = {
  id: 'wf-1',
  projectId: 'proj-1',
  title: 'Build Pipeline',
  description: 'Runs CI build for the project',
  nodes: [
    { id: 'n1', label: 'Start', type: 'start' },
    { id: 'n2', label: 'Build', type: 'agent' },
    { id: 'n3', label: 'End', type: 'end' },
  ],
  edges: [
    { id: 'e1', source: 'n1', target: 'n2' },
    { id: 'e2', source: 'n2', target: 'n3' },
  ],
  enabled: true,
  version: 3,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-15T00:00:00Z',
}

const mockExecutions: ExecutionSummary[] = [
  {
    id: 'exec-abc12345',
    workflowId: 'wf-1',
    workflowTitle: 'Build Pipeline',
    status: 'completed',
    triggerType: 'manual',
    createdAt: '2026-01-15T10:00:00Z',
    startedAt: '2026-01-15T10:00:01Z',
    completedAt: '2026-01-15T10:05:00Z',
    durationMs: 299000,
    triggeredBy: 'user',
  },
  {
    id: 'exec-def67890',
    workflowId: 'wf-1',
    workflowTitle: 'Build Pipeline',
    status: 'failed',
    triggerType: 'event',
    createdAt: '2026-01-14T08:00:00Z',
    startedAt: '2026-01-14T08:00:01Z',
    completedAt: '2026-01-14T08:02:00Z',
    durationMs: 119000,
    triggeredBy: 'system',
    errorMessage: 'Build step failed',
  },
]

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

describe('WorkflowDetail', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows loading state', () => {
    const workflowMock = Workflows.getApiWorkflowsByWorkflowId as Mock
    const executionsMock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock

    workflowMock.mockReturnValue(new Promise(() => {}))
    executionsMock.mockReturnValue(new Promise(() => {}))

    render(<WorkflowDetail projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByTestId('workflow-detail-loading')).toBeInTheDocument()
  })

  it('shows workflow not found when error', async () => {
    const workflowMock = Workflows.getApiWorkflowsByWorkflowId as Mock
    const executionsMock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock

    workflowMock.mockResolvedValueOnce({ error: { detail: 'Not found' } })
    executionsMock.mockResolvedValueOnce({ data: { executions: [], totalCount: 0 } })

    render(<WorkflowDetail projectId="proj-1" workflowId="wf-missing" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByTestId('workflow-not-found')).toBeInTheDocument()
    })

    expect(screen.getByText('Workflow Not Found')).toBeInTheDocument()
  })

  it('displays workflow header with title and status', async () => {
    const workflowMock = Workflows.getApiWorkflowsByWorkflowId as Mock
    const executionsMock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock

    workflowMock.mockResolvedValueOnce({ data: mockWorkflow })
    executionsMock.mockResolvedValueOnce({
      data: { executions: mockExecutions, totalCount: 2 },
    })

    render(<WorkflowDetail projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByTestId('workflow-title')).toHaveTextContent('Build Pipeline')
    })

    expect(screen.getByTestId('workflow-description')).toHaveTextContent(
      'Runs CI build for the project'
    )
    expect(screen.getByText('Enabled')).toBeInTheDocument()
  })

  it('displays editor and executions tabs', async () => {
    const workflowMock = Workflows.getApiWorkflowsByWorkflowId as Mock
    const executionsMock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock

    workflowMock.mockResolvedValueOnce({ data: mockWorkflow })
    executionsMock.mockResolvedValueOnce({
      data: { executions: mockExecutions, totalCount: 2 },
    })

    render(<WorkflowDetail projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /editor/i })).toBeInTheDocument()
    })

    expect(screen.getByRole('tab', { name: /executions/i })).toBeInTheDocument()
  })

  it('shows editor placeholder with node and edge counts', async () => {
    const workflowMock = Workflows.getApiWorkflowsByWorkflowId as Mock
    const executionsMock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock

    workflowMock.mockResolvedValueOnce({ data: mockWorkflow })
    executionsMock.mockResolvedValueOnce({
      data: { executions: [], totalCount: 0 },
    })

    render(<WorkflowDetail projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByTestId('workflow-editor-placeholder')).toBeInTheDocument()
    })

    expect(screen.getByText(/3 nodes/)).toBeInTheDocument()
    expect(screen.getByText(/2 edges/)).toBeInTheDocument()
    expect(screen.getByText(/Version 3/)).toBeInTheDocument()
  })

  it('shows empty state when no executions', async () => {
    const user = (await import('@testing-library/user-event')).default.setup()
    const workflowMock = Workflows.getApiWorkflowsByWorkflowId as Mock
    const executionsMock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock

    workflowMock.mockResolvedValueOnce({ data: mockWorkflow })
    executionsMock.mockResolvedValueOnce({
      data: { executions: [], totalCount: 0 },
    })

    render(<WorkflowDetail projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /executions/i })).toBeInTheDocument()
    })

    await user.click(screen.getByRole('tab', { name: /executions/i }))

    await waitFor(() => {
      expect(screen.getByTestId('executions-empty')).toBeInTheDocument()
    })
  })

  it('displays execution history in executions tab', async () => {
    const user = (await import('@testing-library/user-event')).default.setup()
    const workflowMock = Workflows.getApiWorkflowsByWorkflowId as Mock
    const executionsMock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock

    workflowMock.mockResolvedValueOnce({ data: mockWorkflow })
    executionsMock.mockResolvedValueOnce({
      data: { executions: mockExecutions, totalCount: 2 },
    })

    render(<WorkflowDetail projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /executions/i })).toBeInTheDocument()
    })

    await user.click(screen.getByRole('tab', { name: /executions/i }))

    await waitFor(() => {
      expect(screen.getByTestId('executions-table')).toBeInTheDocument()
    })

    expect(screen.getByText('exec-abc')).toBeInTheDocument()
    expect(screen.getByText('exec-def')).toBeInTheDocument()
  })

  it('shows disabled badge for disabled workflow', async () => {
    const workflowMock = Workflows.getApiWorkflowsByWorkflowId as Mock
    const executionsMock = Workflows.getApiWorkflowsByWorkflowIdExecutions as Mock

    workflowMock.mockResolvedValueOnce({
      data: { ...mockWorkflow, enabled: false },
    })
    executionsMock.mockResolvedValueOnce({
      data: { executions: [], totalCount: 0 },
    })

    render(<WorkflowDetail projectId="proj-1" workflowId="wf-1" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByText('Disabled')).toBeInTheDocument()
    })
  })
})

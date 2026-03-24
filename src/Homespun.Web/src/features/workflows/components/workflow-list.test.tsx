import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { WorkflowList } from './workflow-list'
import { Workflows } from '@/api'
import type { WorkflowSummary } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Workflows: {
    getApiProjectsByProjectIdWorkflows: vi.fn(),
    deleteApiWorkflowsByWorkflowId: vi.fn(),
    postApiWorkflowsByWorkflowIdExecute: vi.fn(),
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

const mockWorkflows: WorkflowSummary[] = [
  {
    id: 'wf-1',
    title: 'Build Pipeline',
    description: 'Runs CI build',
    enabled: true,
    triggerType: 'manual',
    nodeCount: 3,
    version: 1,
    updatedAt: '2026-01-01T00:00:00Z',
    lastExecutionStatus: 'completed',
    lastExecutedAt: '2026-01-01T12:00:00Z',
  },
  {
    id: 'wf-2',
    title: 'Deploy Pipeline',
    description: 'Deploys to production',
    enabled: false,
    triggerType: 'event',
    nodeCount: 5,
    version: 2,
    updatedAt: '2026-01-02T00:00:00Z',
    lastExecutionStatus: 'failed',
    lastExecutedAt: '2026-01-02T12:00:00Z',
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

describe('WorkflowList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows loading skeletons while fetching', () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockReturnValue(new Promise(() => {}))

    render(<WorkflowList projectId="proj-1" />, { wrapper: createWrapper() })

    expect(screen.getByTestId('workflow-list-loading')).toBeInTheDocument()
  })

  it('displays workflows in a table', async () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockResolvedValueOnce({
      data: { workflows: mockWorkflows, totalCount: 2 },
    })

    render(<WorkflowList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Build Pipeline')).toBeInTheDocument()
    })

    expect(screen.getByText('Deploy Pipeline')).toBeInTheDocument()
    expect(screen.getByText('Runs CI build')).toBeInTheDocument()
    expect(screen.getByText('completed')).toBeInTheDocument()
    expect(screen.getByText('failed')).toBeInTheDocument()
    expect(screen.getByText('Disabled')).toBeInTheDocument()
  })

  it('shows empty state when no workflows exist', async () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockResolvedValueOnce({
      data: { workflows: [], totalCount: 0 },
    })

    render(<WorkflowList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByTestId('workflow-list-empty')).toBeInTheDocument()
    })

    expect(screen.getByText(/no workflows yet/i)).toBeInTheDocument()
  })

  it('shows error state with retry button when fetch fails', async () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockRejectedValueOnce(new Error('Network error'))

    render(<WorkflowList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText(/error/i)).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
  })

  it('shows delete confirmation dialog', async () => {
    const user = userEvent.setup()
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockResolvedValueOnce({
      data: { workflows: mockWorkflows, totalCount: 2 },
    })

    render(<WorkflowList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Build Pipeline')).toBeInTheDocument()
    })

    // Open the dropdown for the first workflow
    const actionButtons = screen.getAllByRole('button', { name: /workflow actions/i })
    await user.click(actionButtons[0])

    // Click delete
    const deleteItem = screen.getByText('Delete')
    await user.click(deleteItem)

    // Confirm dialog appears
    expect(screen.getByText(/are you sure you want to delete/i)).toBeInTheDocument()
    expect(screen.getByText(/"Build Pipeline"/)).toBeInTheDocument()
  })

  it('deletes workflow when confirmed', async () => {
    const user = userEvent.setup()
    const listMock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    const deleteMock = Workflows.deleteApiWorkflowsByWorkflowId as Mock

    listMock.mockResolvedValue({
      data: { workflows: mockWorkflows, totalCount: 2 },
    })
    deleteMock.mockResolvedValueOnce({})

    render(<WorkflowList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Build Pipeline')).toBeInTheDocument()
    })

    // Open dropdown and click delete
    const actionButtons = screen.getAllByRole('button', { name: /workflow actions/i })
    await user.click(actionButtons[0])
    await user.click(screen.getByText('Delete'))

    // Confirm deletion
    const confirmButton = screen.getByRole('button', { name: /^delete$/i })
    await user.click(confirmButton)

    await waitFor(() => {
      expect(deleteMock).toHaveBeenCalledWith({ path: { workflowId: 'wf-1' } })
    })
  })

  it('links workflow names to detail page', async () => {
    const mock = Workflows.getApiProjectsByProjectIdWorkflows as Mock
    mock.mockResolvedValueOnce({
      data: { workflows: mockWorkflows, totalCount: 2 },
    })

    render(<WorkflowList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Build Pipeline')).toBeInTheDocument()
    })

    const link = screen.getByText('Build Pipeline').closest('a')
    expect(link).toHaveAttribute('href', '/projects/proj-1/workflows/wf-1')
  })
})

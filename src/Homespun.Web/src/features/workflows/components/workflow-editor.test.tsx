import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { WorkflowEditor } from './workflow-editor'
import { Workflows, AgentPrompts } from '@/api'
import type { WorkflowStep } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Workflows: {
    putApiWorkflowsByWorkflowId: vi.fn(),
  },
  AgentPrompts: {
    getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
  },
}))

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: { children: React.ReactNode; [key: string]: unknown }) => (
    <a {...props}>{children}</a>
  ),
}))

vi.mock('mermaid', () => ({
  default: {
    initialize: vi.fn(),
    render: vi.fn().mockResolvedValue({ svg: '<svg data-testid="mock-mermaid-svg"></svg>' }),
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

const mockSteps: WorkflowStep[] = [
  {
    id: 'step-1',
    name: 'Init',
    stepType: 'serverAction',
    onSuccess: { type: 'nextStep' },
    onFailure: { type: 'exit' },
  },
  {
    id: 'step-2',
    name: 'Build',
    stepType: 'agent',
    prompt: 'Build the project',
    sessionMode: 'build',
    onSuccess: { type: 'nextStep' },
    onFailure: { type: 'retry' },
    maxRetries: 2,
    retryDelaySeconds: 30,
  },
  {
    id: 'step-3',
    name: 'Approve',
    stepType: 'gate',
    onSuccess: { type: 'nextStep' },
    onFailure: { type: 'exit' },
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

describe('WorkflowEditor', () => {
  const defaultProps = {
    workflowId: 'wf-1',
    projectId: 'proj-1',
    initialSteps: mockSteps,
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId).mockResolvedValue({
      data: [],
    } as never)
  })

  it('renders step list with all steps', () => {
    render(<WorkflowEditor {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('step-list')).toBeInTheDocument()
    expect(screen.getByText('Init')).toBeInTheDocument()
    expect(screen.getByText('Build')).toBeInTheDocument()
    expect(screen.getByText('Approve')).toBeInTheDocument()
  })

  it('renders add step button', () => {
    render(<WorkflowEditor {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('add-step-button')).toBeInTheDocument()
  })

  it('adds a new step when add button is clicked', async () => {
    const user = userEvent.setup()
    render(<WorkflowEditor {...defaultProps} />, { wrapper: createWrapper() })

    await user.click(screen.getByTestId('add-step-button'))

    await waitFor(() => {
      const stepList = screen.getByTestId('step-list')
      const items = within(stepList).getAllByTestId(/^step-item-/)
      expect(items).toHaveLength(4)
    })
  })

  it('removes a step with confirmation', async () => {
    const user = userEvent.setup()
    render(<WorkflowEditor {...defaultProps} />, { wrapper: createWrapper() })

    // Click on a step to select it
    await user.click(screen.getByText('Build'))

    await waitFor(() => {
      expect(screen.getByTestId('remove-step-button')).toBeInTheDocument()
    })

    await user.click(screen.getByTestId('remove-step-button'))

    // Confirmation dialog should appear
    await waitFor(() => {
      expect(screen.getByTestId('confirm-remove-dialog')).toBeInTheDocument()
    })

    await user.click(screen.getByTestId('confirm-remove-button'))

    await waitFor(() => {
      expect(screen.queryByText('Build')).not.toBeInTheDocument()
    })
  })

  it('selects a step and shows settings card', async () => {
    const user = userEvent.setup()
    render(<WorkflowEditor {...defaultProps} />, { wrapper: createWrapper() })

    await user.click(screen.getByText('Build'))

    await waitFor(() => {
      expect(screen.getByTestId('step-settings-card')).toBeInTheDocument()
    })
  })

  it('reorders steps when move buttons are clicked', async () => {
    const user = userEvent.setup()
    render(<WorkflowEditor {...defaultProps} />, { wrapper: createWrapper() })

    // Select second step
    await user.click(screen.getByText('Build'))

    await waitFor(() => {
      expect(screen.getByTestId('move-step-up')).toBeInTheDocument()
    })

    await user.click(screen.getByTestId('move-step-up'))

    await waitFor(() => {
      const stepList = screen.getByTestId('step-list')
      const items = within(stepList).getAllByTestId(/^step-item-/)
      expect(items[0]).toHaveTextContent('Build')
      expect(items[1]).toHaveTextContent('Init')
    })
  })

  it('calls save API with correct payload', async () => {
    const user = userEvent.setup()
    const updateMock = Workflows.putApiWorkflowsByWorkflowId as Mock
    updateMock.mockResolvedValueOnce({ data: { id: 'wf-1' } })

    render(<WorkflowEditor {...defaultProps} />, { wrapper: createWrapper() })

    await user.click(screen.getByTestId('save-workflow-button'))

    await waitFor(() => {
      expect(updateMock).toHaveBeenCalledWith(
        expect.objectContaining({
          path: { workflowId: 'wf-1' },
          body: expect.objectContaining({
            projectId: 'proj-1',
            steps: mockSteps,
          }),
        })
      )
    })
  })

  it('shows mermaid chart at the bottom', () => {
    render(<WorkflowEditor {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('mermaid-chart')).toBeInTheDocument()
  })

  it('renders empty state when no steps', () => {
    render(<WorkflowEditor {...defaultProps} initialSteps={[]} />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByTestId('add-step-button')).toBeInTheDocument()
  })
})

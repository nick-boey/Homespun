import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CreateWorkflowDialog } from './create-workflow-dialog'
import type { WorkflowTemplateSummary } from '@/api/generated/types.gen'

const mockCreateMutateAsync = vi.fn()
const mockTemplateMutateAsync = vi.fn()
const mockNavigate = vi.fn()

const mockTemplates: WorkflowTemplateSummary[] = [
  { id: 'tpl-1', title: 'CI Pipeline', description: 'Basic CI template', stepCount: 3 },
  { id: 'tpl-2', title: 'Deploy Flow', description: 'Deploy to production', stepCount: 5 },
]

vi.mock('../hooks/use-workflows', () => ({
  useCreateWorkflow: () => ({
    mutateAsync: mockCreateMutateAsync,
    isPending: false,
  }),
  useWorkflowTemplates: () => ({
    templates: mockTemplates,
    isLoading: false,
    isError: false,
  }),
  useCreateFromTemplate: () => ({
    mutateAsync: mockTemplateMutateAsync,
    isPending: false,
  }),
}))

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
}))

describe('CreateWorkflowDialog', () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    projectId: 'proj-1',
  }

  beforeEach(() => {
    vi.clearAllMocks()
    mockCreateMutateAsync.mockResolvedValue({ id: 'wf-new' })
    mockTemplateMutateAsync.mockResolvedValue({ id: 'wf-from-tpl' })
  })

  it('renders nothing when closed', () => {
    render(<CreateWorkflowDialog {...defaultProps} open={false} />)
    expect(screen.queryByText('Create Workflow')).not.toBeInTheDocument()
  })

  it('renders dialog with blank mode by default', () => {
    render(<CreateWorkflowDialog {...defaultProps} />)
    expect(screen.getByText('Create Workflow')).toBeInTheDocument()
    expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
  })

  it('shows validation error when title is empty on submit', async () => {
    const user = userEvent.setup()
    render(<CreateWorkflowDialog {...defaultProps} />)

    await user.click(screen.getByRole('button', { name: /create$/i }))

    expect(screen.getByText(/title is required/i)).toBeInTheDocument()
    expect(mockCreateMutateAsync).not.toHaveBeenCalled()
  })

  it('creates blank workflow with title and description', async () => {
    const user = userEvent.setup()
    render(<CreateWorkflowDialog {...defaultProps} />)

    await user.type(screen.getByLabelText(/title/i), 'My Workflow')
    await user.type(screen.getByLabelText(/description/i), 'A description')
    await user.click(screen.getByRole('button', { name: /create$/i }))

    expect(mockCreateMutateAsync).toHaveBeenCalledWith({
      title: 'My Workflow',
      description: 'A description',
    })
  })

  it('navigates to new workflow on successful blank creation', async () => {
    const user = userEvent.setup()
    render(<CreateWorkflowDialog {...defaultProps} />)

    await user.type(screen.getByLabelText(/title/i), 'My Workflow')
    await user.click(screen.getByRole('button', { name: /create$/i }))

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/projects/$projectId/workflows/$workflowId',
        params: { projectId: 'proj-1', workflowId: 'wf-new' },
      })
    })
  })

  it('closes dialog on successful creation', async () => {
    const user = userEvent.setup()
    render(<CreateWorkflowDialog {...defaultProps} />)

    await user.type(screen.getByLabelText(/title/i), 'My Workflow')
    await user.click(screen.getByRole('button', { name: /create$/i }))

    await waitFor(() => {
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false)
    })
  })

  it('switches to template mode and shows templates', async () => {
    const user = userEvent.setup()
    render(<CreateWorkflowDialog {...defaultProps} />)

    await user.click(screen.getByRole('tab', { name: /from template/i }))

    expect(screen.getByText('CI Pipeline')).toBeInTheDocument()
    expect(screen.getByText('Deploy Flow')).toBeInTheDocument()
    expect(screen.getByText('Basic CI template')).toBeInTheDocument()
  })

  it('creates workflow from template when clicking a template card', async () => {
    const user = userEvent.setup()
    render(<CreateWorkflowDialog {...defaultProps} />)

    await user.click(screen.getByRole('tab', { name: /from template/i }))
    await user.click(screen.getByText('CI Pipeline'))

    expect(mockTemplateMutateAsync).toHaveBeenCalledWith('tpl-1')
  })

  it('navigates to new workflow on successful template creation', async () => {
    const user = userEvent.setup()
    render(<CreateWorkflowDialog {...defaultProps} />)

    await user.click(screen.getByRole('tab', { name: /from template/i }))
    await user.click(screen.getByText('CI Pipeline'))

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/projects/$projectId/workflows/$workflowId',
        params: { projectId: 'proj-1', workflowId: 'wf-from-tpl' },
      })
    })
  })

  it('cancels without creating', async () => {
    const user = userEvent.setup()
    render(<CreateWorkflowDialog {...defaultProps} />)

    await user.click(screen.getByRole('button', { name: /cancel/i }))

    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false)
    expect(mockCreateMutateAsync).not.toHaveBeenCalled()
  })
})

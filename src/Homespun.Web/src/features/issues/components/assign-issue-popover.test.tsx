import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { AssignIssueDialog } from './assign-issue-popover'

// Mock the hooks
vi.mock('../hooks/use-project-assignees', () => ({
  useProjectAssignees: vi.fn(),
}))

vi.mock('../hooks/use-issue', () => ({
  useIssue: vi.fn(),
}))

vi.mock('../hooks/use-update-issue', () => ({
  useUpdateIssue: vi.fn(),
}))

import { useProjectAssignees } from '../hooks/use-project-assignees'
import { useIssue } from '../hooks/use-issue'
import { useUpdateIssue } from '../hooks/use-update-issue'

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

const defaultProps = {
  open: true,
  onOpenChange: vi.fn(),
  projectId: 'test-project',
  issueId: 'test-issue',
}

function renderDialog(props = {}) {
  return render(createElement(AssignIssueDialog, { ...defaultProps, ...props }), {
    wrapper: createWrapper(),
  })
}

describe('AssignIssueDialog', () => {
  const mockMutate = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()

    vi.mocked(useProjectAssignees).mockReturnValue({
      assignees: ['alice@example.com', 'bob@example.com'],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })

    vi.mocked(useIssue).mockReturnValue({
      issue: {
        id: 'test-issue',
        title: 'Test Issue',
        assignedTo: null,
      },
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as never)

    vi.mocked(useUpdateIssue).mockReturnValue({
      mutate: mockMutate,
      isPending: false,
      isError: false,
      error: null,
    } as never)
  })

  it('renders dialog content when open', () => {
    renderDialog()
    expect(screen.getByText('Assign Issue')).toBeInTheDocument()
  })

  it('shows current assignee value', () => {
    vi.mocked(useIssue).mockReturnValue({
      issue: {
        id: 'test-issue',
        title: 'Test Issue',
        assignedTo: 'alice@example.com',
      },
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as never)

    renderDialog()
    expect(screen.getByRole('combobox')).toHaveTextContent('alice@example.com')
  })

  it('updates issue when assignee is selected', async () => {
    const user = userEvent.setup()
    renderDialog()

    await user.click(screen.getByRole('combobox'))
    await user.click(screen.getByText('alice@example.com'))

    expect(mockMutate).toHaveBeenCalledWith({
      issueId: 'test-issue',
      data: {
        projectId: 'test-project',
        assignedTo: 'alice@example.com',
      },
    })
  })

  it('calls onOpenChange when update succeeds', async () => {
    const onOpenChange = vi.fn()

    // Mock successful mutation with onSuccess callback
    vi.mocked(useUpdateIssue).mockImplementation(
      (options) =>
        ({
          mutate: vi.fn(() => {
            options?.onSuccess?.({ id: 'test-issue', title: 'Test Issue' } as never)
          }),
          isPending: false,
          isError: false,
          error: null,
        }) as never
    )

    const user = userEvent.setup()
    renderDialog({ onOpenChange })

    await user.click(screen.getByRole('combobox'))
    await user.click(screen.getByText('alice@example.com'))

    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false)
    })
  })

  it('shows error message when update fails', () => {
    vi.mocked(useUpdateIssue).mockReturnValue({
      mutate: mockMutate,
      isPending: false,
      isError: true,
      error: { message: 'Failed to update assignee' },
    } as never)

    renderDialog()
    expect(screen.getByText('Failed to update assignee')).toBeInTheDocument()
  })

  it('disables combobox while mutation is pending', () => {
    vi.mocked(useUpdateIssue).mockReturnValue({
      mutate: mockMutate,
      isPending: true,
      isError: false,
      error: null,
    } as never)

    renderDialog()
    expect(screen.getByRole('combobox')).toBeDisabled()
  })
})

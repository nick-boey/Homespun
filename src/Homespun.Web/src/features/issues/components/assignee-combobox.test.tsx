import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { AssigneeCombobox } from './assignee-combobox'

// Mock the useProjectAssignees hook
vi.mock('../hooks/use-project-assignees', () => ({
  useProjectAssignees: vi.fn(),
}))

import { useProjectAssignees } from '../hooks/use-project-assignees'

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

const defaultProps = {
  projectId: 'test-project',
  value: null as string | null,
  onChange: vi.fn(),
}

function renderCombobox(props = {}) {
  return render(createElement(AssigneeCombobox, { ...defaultProps, ...props }), {
    wrapper: createWrapper(),
  })
}

describe('AssigneeCombobox', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useProjectAssignees).mockReturnValue({
      assignees: ['alice@example.com', 'bob@example.com'],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })
  })

  it('renders with placeholder when no value', () => {
    renderCombobox()
    expect(screen.getByRole('combobox')).toHaveTextContent('Select assignee...')
  })

  it('renders with current value when set', () => {
    renderCombobox({ value: 'alice@example.com' })
    expect(screen.getByRole('combobox')).toHaveTextContent('alice@example.com')
  })

  it('opens dropdown on click', async () => {
    const user = userEvent.setup()
    renderCombobox()

    await user.click(screen.getByRole('combobox'))
    expect(screen.getByPlaceholderText('Search or enter email...')).toBeInTheDocument()
  })

  it('shows assignees list when opened', async () => {
    const user = userEvent.setup()
    renderCombobox()

    await user.click(screen.getByRole('combobox'))
    expect(screen.getByText('alice@example.com')).toBeInTheDocument()
    expect(screen.getByText('bob@example.com')).toBeInTheDocument()
  })

  it('filters assignees based on input', async () => {
    const user = userEvent.setup()
    renderCombobox()

    await user.click(screen.getByRole('combobox'))
    await user.type(screen.getByPlaceholderText('Search or enter email...'), 'alice')

    expect(screen.getByText('alice@example.com')).toBeInTheDocument()
    expect(screen.queryByText('bob@example.com')).not.toBeInTheDocument()
  })

  it('calls onChange when assignee is selected', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    renderCombobox({ onChange })

    await user.click(screen.getByRole('combobox'))
    await user.click(screen.getByText('alice@example.com'))

    expect(onChange).toHaveBeenCalledWith('alice@example.com')
  })

  it('shows loading state', async () => {
    vi.mocked(useProjectAssignees).mockReturnValue({
      assignees: [],
      isLoading: true,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })

    const user = userEvent.setup()
    renderCombobox()

    await user.click(screen.getByRole('combobox'))
    // Loading state should be shown (Loader component)
    expect(document.querySelector('[class*="animate"]')).toBeInTheDocument()
  })

  it('shows clear button when value is set', () => {
    renderCombobox({ value: 'alice@example.com' })
    expect(screen.getByLabelText('Clear assignee')).toBeInTheDocument()
  })

  it('calls onChange with null when clear button is clicked', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    renderCombobox({ value: 'alice@example.com', onChange })

    await user.click(screen.getByLabelText('Clear assignee'))
    expect(onChange).toHaveBeenCalledWith(null)
  })

  it('allows custom email entry when valid email is typed', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    renderCombobox({ onChange })

    await user.click(screen.getByRole('combobox'))
    await user.type(screen.getByPlaceholderText('Search or enter email...'), 'newuser@test.com')

    // Should show "Use" option for custom email
    expect(screen.getByText(/Use "newuser@test.com"/)).toBeInTheDocument()

    await user.click(screen.getByText(/Use "newuser@test.com"/))
    expect(onChange).toHaveBeenCalledWith('newuser@test.com')
  })

  it('is disabled when disabled prop is true', () => {
    renderCombobox({ disabled: true })
    expect(screen.getByRole('combobox')).toBeDisabled()
  })
})

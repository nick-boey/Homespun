import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { SecretsList } from './secrets-list'
import { Secrets } from '@/api'
import type { SecretInfo } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Secrets: {
    getApiProjectsByProjectIdSecrets: vi.fn(),
    postApiProjectsByProjectIdSecrets: vi.fn(),
    putApiProjectsByProjectIdSecretsByName: vi.fn(),
    deleteApiProjectsByProjectIdSecretsByName: vi.fn(),
  },
}))

const mockSecrets: SecretInfo[] = [
  {
    name: 'API_KEY',
    lastModified: '2024-01-15T10:30:00Z',
  },
  {
    name: 'DATABASE_URL',
    lastModified: '2024-01-14T08:00:00Z',
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

describe('SecretsList', () => {
  const defaultProps = {
    projectId: 'project-1',
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders loading skeleton initially', () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    mockGetSecrets.mockReturnValue(new Promise(() => {}))

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByRole('heading', { name: /secrets/i })).toBeInTheDocument()
  })

  it('renders secrets list when loaded', async () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    mockGetSecrets.mockResolvedValueOnce({ data: { secrets: mockSecrets } })

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('API_KEY')).toBeInTheDocument()
    })

    expect(screen.getByText('DATABASE_URL')).toBeInTheDocument()
  })

  it('renders empty state when no secrets exist', async () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    mockGetSecrets.mockResolvedValueOnce({ data: { secrets: [] } })

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('No secrets configured')).toBeInTheDocument()
    })
  })

  it('shows add secret button', async () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    mockGetSecrets.mockResolvedValueOnce({ data: { secrets: mockSecrets } })

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /add secret/i })).toBeInTheDocument()
    })
  })

  it('opens add secret dialog when add button is clicked', async () => {
    const user = userEvent.setup()
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    mockGetSecrets.mockResolvedValueOnce({ data: { secrets: mockSecrets } })

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /add secret/i })).toBeInTheDocument()
    })

    const addButton = screen.getByRole('button', { name: /add secret/i })
    await user.click(addButton)

    // Check for dialog heading
    expect(screen.getByRole('heading', { name: 'Add Secret' })).toBeInTheDocument()
  })

  it('renders security notice about masked values', async () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    mockGetSecrets.mockResolvedValueOnce({ data: { secrets: mockSecrets } })

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText(/secret values are never displayed/i)).toBeInTheDocument()
    })
  })

  it('handles error state', async () => {
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    mockGetSecrets.mockResolvedValueOnce({
      error: { detail: 'Something went wrong' },
    })

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      // Check for the error title
      expect(screen.getByText('Failed to fetch secrets')).toBeInTheDocument()
    })

    // Check for the error detail message
    expect(screen.getByText('Something went wrong')).toBeInTheDocument()
  })

  it('creates a new secret when form is submitted', async () => {
    const user = userEvent.setup()
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    const mockPostSecrets = Secrets.postApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>

    mockGetSecrets.mockResolvedValue({ data: { secrets: mockSecrets } })
    mockPostSecrets.mockResolvedValueOnce({})

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /add secret/i })).toBeInTheDocument()
    })

    const addButton = screen.getByRole('button', { name: /add secret/i })
    await user.click(addButton)

    const nameInput = screen.getByLabelText(/name/i)
    const valueInput = screen.getByLabelText(/value/i)

    await user.type(nameInput, 'NEW_SECRET')
    await user.type(valueInput, 'secret-value')

    const submitButton = screen.getByRole('button', { name: /add/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(mockPostSecrets).toHaveBeenCalledWith({
        path: { projectId: 'project-1' },
        body: { name: 'NEW_SECRET', value: 'secret-value' },
      })
    })
  })

  it('deletes a secret when confirmed', async () => {
    const user = userEvent.setup()
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    const mockDeleteSecrets = Secrets.deleteApiProjectsByProjectIdSecretsByName as ReturnType<
      typeof vi.fn
    >

    mockGetSecrets.mockResolvedValue({ data: { secrets: mockSecrets } })
    mockDeleteSecrets.mockResolvedValueOnce({})

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('API_KEY')).toBeInTheDocument()
    })

    // Click the first delete button
    const deleteButtons = screen.getAllByRole('button', { name: /delete/i })
    await user.click(deleteButtons[0])

    // Confirm deletion
    const confirmButton = screen.getByRole('button', { name: 'Delete' })
    await user.click(confirmButton)

    await waitFor(() => {
      expect(mockDeleteSecrets).toHaveBeenCalledWith({
        path: { projectId: 'project-1', name: 'API_KEY' },
      })
    })
  })

  it('opens edit dialog when edit button is clicked', async () => {
    const user = userEvent.setup()
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    mockGetSecrets.mockResolvedValue({ data: { secrets: mockSecrets } })

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('API_KEY')).toBeInTheDocument()
    })

    const editButtons = screen.getAllByRole('button', { name: /edit/i })
    await user.click(editButtons[0])

    expect(screen.getByText('Update Secret')).toBeInTheDocument()
    // The dialog description should mention the secret name
    expect(screen.getByText(/Update the value for API_KEY/)).toBeInTheDocument()
  })

  it('updates a secret when edit form is submitted', async () => {
    const user = userEvent.setup()
    const mockGetSecrets = Secrets.getApiProjectsByProjectIdSecrets as ReturnType<typeof vi.fn>
    const mockPutSecrets = Secrets.putApiProjectsByProjectIdSecretsByName as ReturnType<
      typeof vi.fn
    >

    mockGetSecrets.mockResolvedValue({ data: { secrets: mockSecrets } })
    mockPutSecrets.mockResolvedValueOnce({})

    render(<SecretsList {...defaultProps} />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('API_KEY')).toBeInTheDocument()
    })

    const editButtons = screen.getAllByRole('button', { name: /edit/i })
    await user.click(editButtons[0])

    const valueInput = screen.getByLabelText(/new value/i)
    await user.type(valueInput, 'new-secret-value')

    const submitButton = screen.getByRole('button', { name: /update/i })
    await user.click(submitButton)

    await waitFor(() => {
      expect(mockPutSecrets).toHaveBeenCalledWith({
        path: { projectId: 'project-1', name: 'API_KEY' },
        body: { value: 'new-secret-value' },
      })
    })
  })
})

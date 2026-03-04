import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { BaseBranchSelector } from './base-branch-selector'
import { Clones } from '@/api'
import type { BranchInfo } from '@/api'

vi.mock('@/api', () => ({
  Clones: {
    getApiClonesBranches: vi.fn(),
  },
}))

describe('BaseBranchSelector', () => {
  let queryClient: QueryClient
  const mockOnChange = vi.fn()

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    vi.clearAllMocks()
  })

  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )

  it('shows loading state while fetching branches', () => {
    const mockGet = Clones.getApiClonesBranches as Mock
    mockGet.mockReturnValue(new Promise(() => {}))

    render(<BaseBranchSelector repoPath="/path/to/repo" value="" onChange={mockOnChange} />, {
      wrapper,
    })

    expect(screen.getByText('Loading branches...')).toBeInTheDocument()
  })

  it('renders with selected branch value', async () => {
    const mockBranches: BranchInfo[] = [
      { shortName: 'main', name: 'refs/heads/main' },
      { shortName: 'develop', name: 'refs/heads/develop' },
    ]

    vi.mocked(Clones.getApiClonesBranches).mockResolvedValue({
      data: mockBranches,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Clones.getApiClonesBranches>>)

    render(
      <BaseBranchSelector
        repoPath="/path/to/repo"
        defaultBranch="main"
        value="main"
        onChange={mockOnChange}
      />,
      { wrapper }
    )

    await waitFor(() => {
      expect(screen.queryByText('Loading branches...')).not.toBeInTheDocument()
    })

    // The combobox should be rendered with the selected value
    const trigger = screen.getByRole('combobox')
    expect(trigger).toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-label', 'Select base branch')

    // The selected value should be displayed
    expect(screen.getByText('main')).toBeInTheDocument()
    expect(screen.getByText('(default)')).toBeInTheDocument()
  })

  it('shows error state when fetching fails', async () => {
    vi.mocked(Clones.getApiClonesBranches).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'Failed' },
    } as Awaited<ReturnType<typeof Clones.getApiClonesBranches>>)

    render(<BaseBranchSelector repoPath="/path/to/repo" value="" onChange={mockOnChange} />, {
      wrapper,
    })

    await waitFor(() => {
      expect(screen.getByText('Failed to load branches')).toBeInTheDocument()
    })
  })

  it('does not fetch when repoPath is undefined', () => {
    render(<BaseBranchSelector repoPath={undefined} value="" onChange={mockOnChange} />, {
      wrapper,
    })

    expect(Clones.getApiClonesBranches).not.toHaveBeenCalled()
  })
})

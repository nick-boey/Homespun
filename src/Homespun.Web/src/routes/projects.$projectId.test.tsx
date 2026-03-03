import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createRouter, createMemoryHistory } from '@tanstack/react-router'
import { routeTree } from '@/routeTree.gen'
import { Projects } from '@/api'
import { BreadcrumbProvider } from '@/hooks/use-breadcrumbs'

vi.mock('@/api', () => ({
  Projects: {
    getApiProjectsById: vi.fn(),
  },
}))

function createTestRouter(initialPath: string) {
  const memoryHistory = createMemoryHistory({
    initialEntries: [initialPath],
  })
  return createRouter({
    routeTree,
    history: memoryHistory,
  })
}

function renderWithProviders(initialPath: string) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })

  const router = createTestRouter(initialPath)

  return render(
    <QueryClientProvider client={queryClient}>
      <BreadcrumbProvider>
        <RouterProvider router={router} />
      </BreadcrumbProvider>
    </QueryClientProvider>
  )
}

describe('ProjectLayout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('displays loading state while fetching project', async () => {
    vi.mocked(Projects.getApiProjectsById).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof Projects.getApiProjectsById>
    )

    renderWithProviders('/projects/test-id')

    await waitFor(() => {
      expect(screen.getByTestId('project-loading')).toBeInTheDocument()
    })
  })

  it('displays project name and tabs when loaded', { timeout: 20000 }, async () => {
    vi.mocked(Projects.getApiProjectsById).mockResolvedValue({
      data: {
        id: 'test-id',
        name: 'My Awesome Project',
        localPath: '/path',
        defaultBranch: 'main',
      },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Projects.getApiProjectsById>>)

    renderWithProviders('/projects/test-id')

    // Wait for project name
    await waitFor(
      () => {
        expect(screen.getByRole('heading', { name: 'My Awesome Project' })).toBeInTheDocument()
      },
      { timeout: 15000 }
    )

    // Verify all tabs are present
    expect(screen.getByRole('link', { name: 'Issues' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Branches' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Prompts' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Secrets' })).toBeInTheDocument()

    // There are two Settings links (one in sidebar, one in tabs)
    const settingsLinks = screen.getAllByRole('link', { name: 'Settings' })
    expect(
      settingsLinks.some((link) => link.getAttribute('href') === '/projects/test-id/settings')
    ).toBe(true)

    // Verify quick actions button is present
    expect(screen.getByRole('button', { name: 'Project actions' })).toBeInTheDocument()

    // Issues tab should be active by default
    const issuesLink = screen.getByRole('link', { name: 'Issues' })
    expect(issuesLink).toHaveClass('border-primary')
  })

  it('displays 404 error when project not found', { timeout: 20000 }, async () => {
    vi.mocked(Projects.getApiProjectsById).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 404 }),
      request: new Request('http://test'),
      error: { detail: 'Project not found' },
    } as Awaited<ReturnType<typeof Projects.getApiProjectsById>>)

    renderWithProviders('/projects/nonexistent')

    await waitFor(
      () => {
        expect(screen.getByTestId('project-not-found')).toBeInTheDocument()
      },
      { timeout: 15000 }
    )
    expect(screen.getByText('Project Not Found')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Try Again/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Go Home' })).toBeInTheDocument()
  })
})

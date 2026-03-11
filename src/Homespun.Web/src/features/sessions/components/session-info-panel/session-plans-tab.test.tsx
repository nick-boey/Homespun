import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { SessionPlansTab } from './session-plans-tab'
import { usePlanFiles, usePlanContent } from '@/features/sessions/hooks'
import type { PlanFileInfo } from '@/api/generated'
import { createMockSession } from '@/test/test-utils'

// Mock clipboard API
Object.assign(navigator, {
  clipboard: {
    writeText: vi.fn().mockResolvedValue(undefined),
  },
})

vi.mock('@/features/sessions/hooks', () => ({
  usePlanFiles: vi.fn(),
  usePlanContent: vi.fn(),
}))

describe('SessionPlansTab', () => {
  const mockSession = createMockSession({
    workingDirectory: '/path/to/project',
  })

  const sessionNoDir = createMockSession({
    workingDirectory: undefined,
  })

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows empty state when no working directory', () => {
    vi.mocked(usePlanFiles).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    render(<SessionPlansTab session={sessionNoDir} />)

    expect(screen.getByText('No working directory for this session')).toBeInTheDocument()
  })

  it('shows loading state', () => {
    vi.mocked(usePlanFiles).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    const { container } = render(<SessionPlansTab session={mockSession} />)

    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0)
  })

  it('shows error state', () => {
    vi.mocked(usePlanFiles).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof usePlanFiles>)

    render(<SessionPlansTab session={mockSession} />)

    expect(screen.getByText('Failed to load plan files')).toBeInTheDocument()
  })

  it('shows empty state when no plan files', () => {
    vi.mocked(usePlanFiles).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    render(<SessionPlansTab session={mockSession} />)

    expect(screen.getByText('No plan files in this session')).toBeInTheDocument()
  })

  it('shows plan files with preview', () => {
    const mockPlans: PlanFileInfo[] = [
      {
        fileName: 'plan-20240312.md',
        filePath: '/path/to/project/.claude/plans/plan-20240312.md',
        lastModified: new Date().toISOString(), // Recent
        fileSizeBytes: 2048,
        preview: '# Implementation Plan\n\nThis plan outlines the steps...',
      },
      {
        fileName: 'plan-feature-auth.md',
        filePath: '/path/to/project/.claude/plans/plan-feature-auth.md',
        lastModified: new Date(Date.now() - 3600 * 1000).toISOString(), // 1 hour ago
        fileSizeBytes: 4096,
        preview: '# Authentication Feature\n\n## Overview\n...',
      },
    ]

    vi.mocked(usePlanFiles).mockReturnValue({
      data: mockPlans,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    vi.mocked(usePlanContent).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanContent>)

    render(<SessionPlansTab session={mockSession} />)

    // Summary
    expect(screen.getByText('2 plan files found')).toBeInTheDocument()

    // File names
    expect(screen.getByText('plan-20240312.md')).toBeInTheDocument()
    expect(screen.getByText('plan-feature-auth.md')).toBeInTheDocument()

    // Time and size
    expect(screen.getByText('Just now')).toBeInTheDocument()
    expect(screen.getByText('1 hour ago')).toBeInTheDocument()
    expect(screen.getByText('2.0 KB')).toBeInTheDocument()
    expect(screen.getByText('4.0 KB')).toBeInTheDocument()

    // Previews
    expect(
      screen.getByText('# Implementation Plan This plan outlines the steps...')
    ).toBeInTheDocument()
    expect(screen.getByText('# Authentication Feature ## Overview ...')).toBeInTheDocument()
  })

  it('expands and shows plan content', async () => {
    const mockPlans: PlanFileInfo[] = [
      {
        fileName: 'plan-test.md',
        filePath: '/path/to/project/.claude/plans/plan-test.md',
        lastModified: new Date().toISOString(),
        fileSizeBytes: 1024,
        preview: 'Preview text',
      },
    ]

    const mockContent = `# Full Plan Content

This is the complete plan with multiple lines.

## Section 1
Details here...

## Section 2
More details...`

    vi.mocked(usePlanFiles).mockReturnValue({
      data: mockPlans,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    vi.mocked(usePlanContent).mockReturnValue({
      data: mockContent,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanContent>)

    render(<SessionPlansTab session={mockSession} />)

    // Click to expand
    const expandButton = screen.getByRole('button', { name: /plan-test\.md/i })
    fireEvent.click(expandButton)

    // Should show content
    expect(screen.getByText('Plan content')).toBeInTheDocument()
    expect(screen.getByText(/Full Plan Content/)).toBeInTheDocument()
    expect(screen.getByText(/This is the complete plan/)).toBeInTheDocument()
  })

  it('shows loading state when fetching plan content', () => {
    const mockPlans: PlanFileInfo[] = [
      {
        fileName: 'plan-test.md',
        filePath: '/path/to/project/.claude/plans/plan-test.md',
        lastModified: new Date().toISOString(),
        fileSizeBytes: 1024,
      },
    ]

    vi.mocked(usePlanFiles).mockReturnValue({
      data: mockPlans,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    vi.mocked(usePlanContent).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof usePlanContent>)

    const { container } = render(<SessionPlansTab session={mockSession} />)

    // Click to expand
    const expandButton = screen.getByRole('button', { name: /plan-test\.md/i })
    fireEvent.click(expandButton)

    // Should show loading skeletons
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0)
  })

  it('copies file path to clipboard', async () => {
    const mockPlans: PlanFileInfo[] = [
      {
        fileName: 'plan-test.md',
        filePath: '/path/to/project/.claude/plans/plan-test.md',
        lastModified: new Date().toISOString(),
        fileSizeBytes: 1024,
      },
    ]

    vi.mocked(usePlanFiles).mockReturnValue({
      data: mockPlans,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    render(<SessionPlansTab session={mockSession} />)

    // Find and click the copy button
    const copyButtons = screen.getAllByRole('button')
    const copyButton = copyButtons.find((btn) =>
      btn.querySelector('svg')?.classList.contains('h-3')
    )

    if (copyButton) {
      fireEvent.click(copyButton)

      await waitFor(() => {
        expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
          '/path/to/project/.claude/plans/plan-test.md'
        )
      })
    }
  })

  it('formats file sizes correctly', () => {
    const mockPlans: PlanFileInfo[] = [
      {
        fileName: 'tiny.md',
        filePath: '/path/tiny.md',
        lastModified: new Date().toISOString(),
        fileSizeBytes: 512,
      },
      {
        fileName: 'large.md',
        filePath: '/path/large.md',
        lastModified: new Date().toISOString(),
        fileSizeBytes: 1024 * 1024 * 2.5, // 2.5 MB
      },
      {
        fileName: 'huge.md',
        filePath: '/path/huge.md',
        lastModified: new Date().toISOString(),
        fileSizeBytes: 1024 * 1024 * 1024 * 1.2, // 1.2 GB
      },
    ]

    vi.mocked(usePlanFiles).mockReturnValue({
      data: mockPlans,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    render(<SessionPlansTab session={mockSession} />)

    expect(screen.getByText('512.0 B')).toBeInTheDocument()
    expect(screen.getByText('2.5 MB')).toBeInTheDocument()
    expect(screen.getByText('1.2 GB')).toBeInTheDocument()
  })

  it('formats relative times correctly', () => {
    const now = new Date()

    const mockPlans: PlanFileInfo[] = [
      {
        fileName: 'just-now.md',
        filePath: '/path/just-now.md',
        lastModified: new Date(now.getTime() - 30 * 1000).toISOString(), // 30 seconds ago
        fileSizeBytes: 1024,
      },
      {
        fileName: 'minutes-ago.md',
        filePath: '/path/minutes-ago.md',
        lastModified: new Date(now.getTime() - 45 * 60 * 1000).toISOString(), // 45 minutes ago
        fileSizeBytes: 1024,
      },
      {
        fileName: 'hours-ago.md',
        filePath: '/path/hours-ago.md',
        lastModified: new Date(now.getTime() - 5 * 60 * 60 * 1000).toISOString(), // 5 hours ago
        fileSizeBytes: 1024,
      },
      {
        fileName: 'days-ago.md',
        filePath: '/path/days-ago.md',
        lastModified: new Date(now.getTime() - 3 * 24 * 60 * 60 * 1000).toISOString(), // 3 days ago
        fileSizeBytes: 1024,
      },
    ]

    vi.mocked(usePlanFiles).mockReturnValue({
      data: mockPlans,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof usePlanFiles>)

    render(<SessionPlansTab session={mockSession} />)

    expect(screen.getByText('Just now')).toBeInTheDocument()
    expect(screen.getByText('45 minutes ago')).toBeInTheDocument()
    expect(screen.getByText('5 hours ago')).toBeInTheDocument()
    expect(screen.getByText('3 days ago')).toBeInTheDocument()
  })
})

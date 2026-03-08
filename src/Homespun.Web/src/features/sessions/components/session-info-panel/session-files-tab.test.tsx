import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SessionFilesTab } from './session-files-tab'
import { useChangedFiles } from '@/features/sessions/hooks'
import type { FileChangeInfo } from '@/api/generated'
import { createMockSession } from '@/test/test-utils'

vi.mock('@/features/sessions/hooks', () => ({
  useChangedFiles: vi.fn(),
}))

describe('SessionFilesTab', () => {
  const mockSession = createMockSession({
    workingDirectory: '/path/to/project',
  })

  const sessionNoDir = createMockSession({
    workingDirectory: null,
  })

  it('shows empty state when no working directory', () => {
    vi.mocked(useChangedFiles).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useChangedFiles>)

    render(<SessionFilesTab session={sessionNoDir} />)

    expect(screen.getByText('No working directory for this session')).toBeInTheDocument()
  })

  it('shows loading state', () => {
    vi.mocked(useChangedFiles).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof useChangedFiles>)

    const { container } = render(<SessionFilesTab session={mockSession} />)

    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0)
  })

  it('shows error state', () => {
    vi.mocked(useChangedFiles).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof useChangedFiles>)

    render(<SessionFilesTab session={mockSession} />)

    expect(screen.getByText('Failed to load changed files')).toBeInTheDocument()
  })

  it('shows empty state when no files changed', () => {
    vi.mocked(useChangedFiles).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useChangedFiles>)

    render(<SessionFilesTab session={mockSession} />)

    expect(screen.getByText('No file changes in this session')).toBeInTheDocument()
  })

  it('shows changed files grouped by status', () => {
    const mockFiles: FileChangeInfo[] = [
      {
        filePath: 'src/components/Button.tsx',
        additions: 25,
        deletions: 5,
        status: 1, // Modified
      },
      {
        filePath: 'src/components/NewComponent.tsx',
        additions: 100,
        deletions: 0,
        status: 0, // Added
      },
      {
        filePath: 'src/components/AnotherNewComponent.tsx',
        additions: 50,
        deletions: 0,
        status: 0, // Added
      },
      {
        filePath: 'src/components/OldComponent.tsx',
        additions: 0,
        deletions: 75,
        status: 2, // Deleted
      },
      {
        filePath: 'src/components/RenamedComponent.tsx',
        additions: 10,
        deletions: 10,
        status: 3, // Renamed
      },
    ]

    vi.mocked(useChangedFiles).mockReturnValue({
      data: mockFiles,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useChangedFiles>)

    render(<SessionFilesTab session={mockSession} />)

    // Summary
    expect(screen.getByText('5 files changed')).toBeInTheDocument()

    // Status headers
    expect(screen.getByText('Added (2)')).toBeInTheDocument()
    expect(screen.getByText('Modified (1)')).toBeInTheDocument()
    expect(screen.getByText('Deleted (1)')).toBeInTheDocument()
    expect(screen.getByText('Renamed (1)')).toBeInTheDocument()

    // File paths
    expect(screen.getByText('src/components/Button.tsx')).toBeInTheDocument()
    expect(screen.getByText('src/components/NewComponent.tsx')).toBeInTheDocument()
    expect(screen.getByText('src/components/OldComponent.tsx')).toBeInTheDocument()
    expect(screen.getByText('src/components/RenamedComponent.tsx')).toBeInTheDocument()

    // Addition/deletion counts
    expect(screen.getByText('+25')).toBeInTheDocument()
    expect(screen.getByText('-5')).toBeInTheDocument()
    expect(screen.getByText('+100')).toBeInTheDocument()
    expect(screen.getByText('-75')).toBeInTheDocument()

    // Total stats
    expect(screen.getByText('Total:')).toBeInTheDocument()
    expect(screen.getByText('+185')).toBeInTheDocument() // 25 + 100 + 50 + 10
    expect(screen.getByText('-90')).toBeInTheDocument() // 5 + 75 + 10
  })

  it('handles files with unknown status', () => {
    const mockFiles: FileChangeInfo[] = [
      {
        filePath: 'src/unknown-status.txt',
        additions: 5,
        deletions: 2,
        // No status field
      },
    ]

    vi.mocked(useChangedFiles).mockReturnValue({
      data: mockFiles,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useChangedFiles>)

    render(<SessionFilesTab session={mockSession} />)

    expect(screen.getByText('Unknown (1)')).toBeInTheDocument()
    expect(screen.getByText('src/unknown-status.txt')).toBeInTheDocument()
  })

  it('handles files without addition/deletion counts', () => {
    const mockFiles: FileChangeInfo[] = [
      {
        filePath: 'src/no-stats.txt',
        status: 1,
        // No additions/deletions
      },
    ]

    vi.mocked(useChangedFiles).mockReturnValue({
      data: mockFiles,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useChangedFiles>)

    render(<SessionFilesTab session={mockSession} />)

    expect(screen.getByText('src/no-stats.txt')).toBeInTheDocument()
    // Should not show total stats section
    expect(screen.queryByText('Total:')).not.toBeInTheDocument()
  })
})

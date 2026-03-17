import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ProjectCard } from './project-card'
import type { Project } from '@/api/generated/types.gen'

// Mock TanStack Router Link
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
    // Replace $paramName with actual values from params
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

const mockProject: Project = {
  id: 'proj-123',
  name: 'Test Project',
  localPath: '/home/user/repos/test-project',
  defaultBranch: 'main',
  gitHubOwner: 'testowner',
  gitHubRepo: 'testrepo',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-03-01T12:00:00Z',
}

const mockProjectWithoutGitHub: Project = {
  id: 'proj-456',
  name: 'Local Project',
  localPath: '/home/user/local-project',
  defaultBranch: 'develop',
  createdAt: '2024-02-01T00:00:00Z',
  updatedAt: '2024-02-15T08:30:00Z',
}

describe('ProjectCard', () => {
  const defaultProps = {
    project: mockProject,
    onDelete: vi.fn(),
  }

  it('renders project name', () => {
    render(<ProjectCard {...defaultProps} />)

    expect(screen.getByText('Test Project')).toBeInTheDocument()
  })

  it('renders project local path', () => {
    render(<ProjectCard {...defaultProps} />)

    expect(screen.getByText('/home/user/repos/test-project')).toBeInTheDocument()
  })

  it('renders GitHub owner/repo when available', () => {
    render(<ProjectCard {...defaultProps} />)

    expect(screen.getByText('testowner/testrepo')).toBeInTheDocument()
  })

  it('does not render GitHub info when not available', () => {
    render(<ProjectCard {...defaultProps} project={mockProjectWithoutGitHub} />)

    expect(screen.queryByText('testowner/testrepo')).not.toBeInTheDocument()
    expect(screen.getByText('Local Project')).toBeInTheDocument()
  })

  it('displays Local badge for projects without GitHub info', () => {
    render(<ProjectCard {...defaultProps} project={mockProjectWithoutGitHub} />)

    // Should show Local badge instead of GitHub owner/repo
    const localBadge = screen.getByText('Local')
    expect(localBadge).toBeInTheDocument()
  })

  it('renders updated timestamp', () => {
    render(<ProjectCard {...defaultProps} />)

    // The component should show relative time or formatted date
    expect(screen.getByText(/updated/i)).toBeInTheDocument()
  })

  it('links to project issues page', () => {
    render(<ProjectCard {...defaultProps} />)

    const link = screen.getByRole('link')
    expect(link).toHaveAttribute('href', '/projects/proj-123')
  })

  it('calls onDelete when delete button is clicked and confirmed', async () => {
    const user = userEvent.setup()
    const onDelete = vi.fn()

    render(<ProjectCard {...defaultProps} onDelete={onDelete} />)

    // Find and click the delete button
    const deleteButton = screen.getByRole('button', { name: /delete/i })
    await user.click(deleteButton)

    // Confirm deletion in dialog
    const confirmButton = screen.getByRole('button', { name: /continue|confirm|delete/i })
    await user.click(confirmButton)

    expect(onDelete).toHaveBeenCalledWith('proj-123')
  })

  it('does not call onDelete when deletion is cancelled', async () => {
    const user = userEvent.setup()
    const onDelete = vi.fn()

    render(<ProjectCard {...defaultProps} onDelete={onDelete} />)

    // Find and click the delete button
    const deleteButton = screen.getByRole('button', { name: /delete/i })
    await user.click(deleteButton)

    // Cancel deletion in dialog
    const cancelButton = screen.getByRole('button', { name: /cancel/i })
    await user.click(cancelButton)

    expect(onDelete).not.toHaveBeenCalled()
  })
})

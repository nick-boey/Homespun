import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ProjectCard } from './project-card'
import type { Project } from '@/api/generated/types.gen'

// Mock TanStack Router Link component
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
  }) => {
    const href = params ? to.replace('$projectId', params.projectId) : to
    return (
      <a href={href} {...props}>
        {children}
      </a>
    )
  },
}))

describe('ProjectCard', () => {
  const mockProject: Project = {
    id: 'test-project-1',
    name: 'Test Project',
    localPath: '/path/to/project',
    gitHubOwner: 'owner',
    gitHubRepo: 'repo',
    defaultBranch: 'main',
    createdAt: '2024-01-15T10:00:00Z',
    updatedAt: '2024-01-20T15:30:00Z',
  }

  it('renders project name', () => {
    render(<ProjectCard project={mockProject} onDelete={() => {}} />)

    expect(screen.getByText('Test Project')).toBeInTheDocument()
  })

  it('renders GitHub repository info when available', () => {
    render(<ProjectCard project={mockProject} onDelete={() => {}} />)

    expect(screen.getByText('owner/repo')).toBeInTheDocument()
  })

  it('renders local path when GitHub info is not available', () => {
    const localProject: Project = {
      ...mockProject,
      gitHubOwner: undefined,
      gitHubRepo: undefined,
    }

    render(<ProjectCard project={localProject} onDelete={() => {}} />)

    expect(screen.getByText('/path/to/project')).toBeInTheDocument()
  })

  it('renders default branch', () => {
    render(<ProjectCard project={mockProject} onDelete={() => {}} />)

    expect(screen.getByText('main')).toBeInTheDocument()
  })

  it('calls onDelete when delete button is clicked', async () => {
    const user = userEvent.setup()
    const handleDelete = vi.fn()

    render(<ProjectCard project={mockProject} onDelete={handleDelete} />)

    const deleteButton = screen.getByRole('button', { name: /delete/i })
    await user.click(deleteButton)

    expect(handleDelete).toHaveBeenCalledWith('test-project-1')
  })

  it('renders link to project detail page', () => {
    render(<ProjectCard project={mockProject} onDelete={() => {}} />)

    const link = screen.getByRole('link')
    expect(link).toHaveAttribute('href', '/projects/test-project-1')
  })
})

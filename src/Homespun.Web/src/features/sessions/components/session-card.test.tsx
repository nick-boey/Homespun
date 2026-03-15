import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import React from 'react'
import { SessionCard } from './session-card'
import type { SessionSummary } from '@/api/generated/types.gen'
import { SessionMode, ClaudeSessionStatus } from '@/api/generated/types.gen'

const mockSession: SessionSummary = {
  id: 'test-session-id',
  entityId: 'issue-123',
  projectId: 'project-1',
  model: 'claude-3.5-sonnet',
  mode: SessionMode.BUILD,
  status: ClaudeSessionStatus.RUNNING,
  createdAt: new Date().toISOString(),
  lastActivityAt: new Date().toISOString(),
  messageCount: 0,
  totalCostUsd: 0.05,
}

// Mock the router
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...props
  }: {
    children: React.ReactNode
    to: string
    [key: string]: unknown
  }) => {
    return React.createElement('a', { href: to, ...props }, children)
  },
}))

describe('SessionCard', () => {
  it('renders session information correctly', () => {
    render(
      <SessionCard
        session={mockSession}
        entityTitle="Fix login bug"
        entityType="issue"
        projectName="Test Project"
        onStop={vi.fn()}
      />
    )

    // Check entity badge and title
    expect(screen.getByText('Issue')).toBeInTheDocument()
    expect(screen.getByText('Fix login bug')).toBeInTheDocument()

    // Check status
    expect(screen.getByText('Running')).toBeInTheDocument()

    // Check mode
    expect(screen.getByText('Build')).toBeInTheDocument()

    // Check model (should extract sonnet from full model name)
    expect(screen.getByText('sonnet')).toBeInTheDocument()

    // Check action buttons
    expect(screen.getByRole('link', { name: /Chat/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Stop/ })).toBeInTheDocument()
  })

  it('shows PR badge for pull request entities', () => {
    render(
      <SessionCard
        session={{ ...mockSession, entityId: 'pr-456' }}
        entityTitle="Add new feature"
        entityType="pr"
        projectName="Test Project"
      />
    )

    expect(screen.getByText('PR')).toBeInTheDocument()
    expect(screen.getByText('Add new feature')).toBeInTheDocument()
  })

  it('falls back to entity ID when title is not available', () => {
    render(
      <SessionCard
        session={mockSession}
        entityTitle={undefined}
        entityType="issue"
        projectName="Test Project"
      />
    )

    // When no title is provided, entity ID appears twice: once as title fallback and once in metadata
    const entityIdElements = screen.getAllByText('issue-123')
    expect(entityIdElements).toHaveLength(2)
  })

  it('shows Plan mode correctly', () => {
    render(
      <SessionCard
        session={{ ...mockSession, mode: SessionMode.PLAN }}
        entityTitle="Test"
        entityType="issue"
        projectName="Test Project"
      />
    )

    expect(screen.getByText('Plan')).toBeInTheDocument()
  })

  it('hides Stop button for stopped sessions', () => {
    render(
      <SessionCard
        session={{ ...mockSession, status: ClaudeSessionStatus.STOPPED }}
        entityTitle="Test"
        entityType="issue"
        projectName="Test Project"
      />
    )

    expect(screen.queryByRole('button', { name: /Stop/ })).not.toBeInTheDocument()
    expect(screen.getByText('Stopped')).toBeInTheDocument()
  })

  it('shows error state correctly', () => {
    render(
      <SessionCard
        session={{ ...mockSession, status: ClaudeSessionStatus.ERROR }}
        entityTitle="Test"
        entityType="issue"
        projectName="Test Project"
      />
    )

    expect(screen.queryByRole('button', { name: /Stop/ })).not.toBeInTheDocument()
    expect(screen.getByText('Error')).toBeInTheDocument()
  })

  it('displays time information correctly', () => {
    const oneHourAgo = new Date(Date.now() - 60 * 60 * 1000).toISOString()
    const twoHoursAgo = new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString()

    render(
      <SessionCard
        session={{
          ...mockSession,
          createdAt: twoHoursAgo,
          lastActivityAt: oneHourAgo,
        }}
        entityTitle="Test"
        entityType="issue"
        projectName="Test Project"
      />
    )

    expect(screen.getByText(/Started 2 hours ago/)).toBeInTheDocument()
    expect(screen.getByText(/Active 1 hour ago/)).toBeInTheDocument()
  })

  it('calls onStop when Stop button is clicked', () => {
    const onStop = vi.fn()
    render(
      <SessionCard
        session={mockSession}
        entityTitle="Test"
        entityType="issue"
        projectName="Test Project"
        onStop={onStop}
      />
    )

    const stopButton = screen.getByRole('button', { name: /Stop/ })
    fireEvent.click(stopButton)

    expect(onStop).toHaveBeenCalledWith('test-session-id')
  })

  it('shows project name when provided', () => {
    render(
      <SessionCard
        session={mockSession}
        entityTitle="Test"
        entityType="issue"
        projectName="My Awesome Project"
      />
    )

    expect(screen.getByText('My Awesome Project')).toBeInTheDocument()
  })

  it('shows message count when available', () => {
    render(
      <SessionCard
        session={mockSession}
        entityTitle="Test"
        entityType="issue"
        projectName="Test Project"
        messageCount={42}
      />
    )

    expect(screen.getByText('42 messages')).toBeInTheDocument()
  })

  it('shows different status animations', () => {
    const statusTests = [
      { status: ClaudeSessionStatus.STARTING, label: 'Starting' },
      { status: ClaudeSessionStatus.RUNNING_HOOKS, label: 'Running Hooks' },
      { status: ClaudeSessionStatus.RUNNING, label: 'Running' },
      { status: ClaudeSessionStatus.WAITING_FOR_INPUT, label: 'Waiting' },
      { status: ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER, label: 'Question' },
      { status: ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION, label: 'Plan Ready' },
    ]

    statusTests.forEach(({ status, label }) => {
      const { rerender } = render(
        <SessionCard
          session={{ ...mockSession, status }}
          entityTitle="Test"
          entityType="issue"
          projectName="Test Project"
        />
      )

      expect(screen.getByText(label)).toBeInTheDocument()

      // Check that the status indicator has animation class
      const statusElement = screen.getByText(label).parentElement
      expect(statusElement?.querySelector('.animate-pulse')).toBeInTheDocument()

      rerender(<div />)
    })
  })

  it('displays entity ID in metadata section', () => {
    render(
      <SessionCard
        session={mockSession}
        entityTitle="Fix login bug"
        entityType="issue"
        projectName="Test Project"
      />
    )

    // Check that entity ID is shown
    expect(screen.getByText('issue-123')).toBeInTheDocument()
    // Check that title is still shown as main title
    expect(screen.getByText('Fix login bug')).toBeInTheDocument()
  })

  it('displays entity ID even when title is missing', () => {
    render(
      <SessionCard
        session={mockSession}
        entityTitle={undefined}
        entityType="issue"
        projectName="Test Project"
      />
    )

    // Check that entity ID appears in both places (title fallback and metadata)
    const entityIdElements = screen.getAllByText('issue-123')
    expect(entityIdElements).toHaveLength(2) // One in title, one in metadata
  })

  it('displays entity ID without project name', () => {
    render(
      <SessionCard
        session={mockSession}
        entityTitle="Test Issue"
        entityType="issue"
        projectName={undefined}
      />
    )

    // Check that entity ID is shown even without project
    expect(screen.getByText('issue-123')).toBeInTheDocument()
    // Check that bullet separator is not shown
    expect(screen.queryByText('•')).not.toBeInTheDocument()
  })
})

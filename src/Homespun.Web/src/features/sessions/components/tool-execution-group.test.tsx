import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ToolExecutionGroupDisplay } from './tool-execution-group'
import type { ToolExecutionGroup, ClaudeMessageContent } from '@/types/tool-execution'

describe('ToolExecutionGroupDisplay', () => {
  const mockGroup: ToolExecutionGroup = {
    id: 'group1',
    executions: [
      {
        toolUse: {
          contentType: 'tool_use',
          toolUseId: 'tool1',
          name: 'read_file',
          input: { path: 'test1.ts' },
        } as ClaudeMessageContent,
        toolResult: {
          contentType: 'tool_result',
          toolUseId: 'tool1',
          content: 'File contents 1',
        } as ClaudeMessageContent,
        isRunning: false,
      },
      {
        toolUse: {
          contentType: 'tool_use',
          toolUseId: 'tool2',
          name: 'write_file',
          input: { path: 'test2.ts' },
        } as ClaudeMessageContent,
        toolResult: {
          contentType: 'tool_result',
          toolUseId: 'tool2',
          content: 'Success',
        } as ClaudeMessageContent,
        isRunning: false,
      },
      {
        toolUse: {
          contentType: 'tool_use',
          toolUseId: 'tool3',
          name: 'bash',
          input: { command: 'ls -la' },
        } as ClaudeMessageContent,
        toolResult: {
          contentType: 'tool_result',
          toolUseId: 'tool3',
          content: 'Directory listing',
        } as ClaudeMessageContent,
        isRunning: false,
      },
    ],
    timestamp: '2024-01-01T00:00:00Z',
    originalMessageIds: ['msg1', 'msg2'],
  }

  it('renders tool count correctly', () => {
    render(<ToolExecutionGroupDisplay group={mockGroup} />)
    expect(screen.getByText('3 tool calls')).toBeInTheDocument()
  })

  it('shows last 2 executions by default', () => {
    render(<ToolExecutionGroupDisplay group={mockGroup} />)

    // Should show the last 2 tools
    expect(screen.getByText(/write_file/)).toBeInTheDocument()
    expect(screen.getByText(/bash/)).toBeInTheDocument()

    // Should not show the first tool
    expect(screen.queryByText(/read_file/)).not.toBeInTheDocument()

    // Should show hidden count
    expect(screen.getByText('1 earlier tool call hidden')).toBeInTheDocument()
  })

  it('shows all executions when expanded', async () => {
    const user = userEvent.setup()
    render(<ToolExecutionGroupDisplay group={mockGroup} />)

    // Click to expand
    await user.click(screen.getByRole('button', { name: /show all/i }))

    // All tools should be visible
    expect(screen.getByText(/read_file/)).toBeInTheDocument()
    expect(screen.getByText(/write_file/)).toBeInTheDocument()
    expect(screen.getByText(/bash/)).toBeInTheDocument()

    // Hidden message should be gone
    expect(screen.queryByText(/earlier tool call hidden/)).not.toBeInTheDocument()
  })

  it('handles single tool execution', () => {
    const singleGroup: ToolExecutionGroup = {
      ...mockGroup,
      executions: [mockGroup.executions[0]],
    }

    render(<ToolExecutionGroupDisplay group={singleGroup} />)

    expect(screen.getByText('1 tool call')).toBeInTheDocument()
    expect(screen.getByText(/read_file/)).toBeInTheDocument()

    // No hidden message or expand button for single tool
    expect(screen.queryByText(/earlier tool/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /show all/i })).not.toBeInTheDocument()
  })

  it('handles two tool executions', () => {
    const twoToolGroup: ToolExecutionGroup = {
      ...mockGroup,
      executions: mockGroup.executions.slice(0, 2),
    }

    render(<ToolExecutionGroupDisplay group={twoToolGroup} />)

    expect(screen.getByText('2 tool calls')).toBeInTheDocument()

    // Both tools should be visible
    expect(screen.getByText(/read_file/)).toBeInTheDocument()
    expect(screen.getByText(/write_file/)).toBeInTheDocument()

    // No expand button needed for exactly 2 tools
    expect(screen.queryByRole('button', { name: /show all/i })).not.toBeInTheDocument()
    expect(screen.queryByText(/earlier tool/)).not.toBeInTheDocument()
  })

  it('shows error state for failed tools', () => {
    const errorGroup: ToolExecutionGroup = {
      ...mockGroup,
      executions: [
        {
          ...mockGroup.executions[0],
          toolResult: {
            contentType: 'tool_result',
            toolUseId: 'tool1',
            content: 'Error: File not found',
            isError: true,
          } as ClaudeMessageContent,
        },
      ],
    }

    render(<ToolExecutionGroupDisplay group={errorGroup} />)

    // Should have error styling (implementation specific)
    const toolRow = screen.getByText(/read_file/).closest('[data-testid="tool-execution-row"]')
    expect(toolRow).toHaveAttribute('data-error', 'true')
  })

  it('shows running state for incomplete tools', () => {
    const runningGroup: ToolExecutionGroup = {
      ...mockGroup,
      executions: [
        {
          ...mockGroup.executions[0],
          toolResult: undefined,
          isRunning: true,
        },
      ],
    }

    render(<ToolExecutionGroupDisplay group={runningGroup} />)

    // Should show running indicator
    expect(screen.getByTestId('running-indicator')).toBeInTheDocument()
  })

  it('collapses back to showing 2 when clicking show less', async () => {
    const user = userEvent.setup()
    render(<ToolExecutionGroupDisplay group={mockGroup} />)

    // Expand first
    await user.click(screen.getByRole('button', { name: /show all/i }))
    expect(screen.getByText(/read_file/)).toBeInTheDocument()

    // Collapse back
    await user.click(screen.getByRole('button', { name: /show less/i }))

    // First tool should be hidden again
    expect(screen.queryByText(/read_file/)).not.toBeInTheDocument()
    expect(screen.getByText('1 earlier tool call hidden')).toBeInTheDocument()
  })
})
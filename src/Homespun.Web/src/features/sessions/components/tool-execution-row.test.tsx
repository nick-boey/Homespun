import { describe, it, expect } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ToolExecutionRow } from './tool-execution-row'
import type { ToolExecution } from '@/types/tool-execution'

describe('ToolExecutionRow', () => {
  const mockExecution: ToolExecution = {
    toolUse: {
      contentType: 'tool_use',
      toolUseId: 'tool1',
      name: 'read_file',
      input: { path: '/src/app.tsx' },
    },
    toolResult: {
      contentType: 'tool_result',
      toolUseId: 'tool1',
      content: 'File contents here...',
    },
    isRunning: false,
  }

  it('renders tool name and icon', () => {
    render(<ToolExecutionRow execution={mockExecution} />)

    expect(screen.getByText('read_file')).toBeInTheDocument()
    expect(screen.getByText('📄')).toBeInTheDocument()
  })

  it('shows summary from input', () => {
    render(<ToolExecutionRow execution={mockExecution} />)

    expect(screen.getByText('/src/app.tsx')).toBeInTheDocument()
  })

  it('expands to show full details when clicked', async () => {
    const user = userEvent.setup()
    render(<ToolExecutionRow execution={mockExecution} />)

    // Details should not be visible initially
    expect(screen.queryByText('File contents here...')).not.toBeInTheDocument()

    // Click on the button (not the row itself)
    const button = screen.getByRole('button')
    await user.click(button)

    // Wait for content to appear
    const content = await screen.findByText('File contents here...')
    expect(content).toBeInTheDocument()
  })

  it('shows error state with red indicator', () => {
    const errorExecution: ToolExecution = {
      ...mockExecution,
      toolResult: {
        contentType: 'tool_result',
        toolUseId: 'tool1',
        content: 'Error: File not found',
        isError: true,
      },
    }

    render(<ToolExecutionRow execution={errorExecution} />)

    const row = screen.getByTestId('tool-execution-row')
    expect(row).toHaveAttribute('data-error', 'true')
    expect(row).toHaveClass('border-l-4', 'border-destructive')
    expect(screen.getByText('Error')).toBeInTheDocument()
  })

  it('shows running state with spinner', () => {
    const runningExecution: ToolExecution = {
      ...mockExecution,
      toolResult: undefined,
      isRunning: true,
    }

    render(<ToolExecutionRow execution={runningExecution} />)

    expect(screen.getByTestId('running-indicator')).toBeInTheDocument()
    expect(screen.getByText('Running...')).toBeInTheDocument()
  })

  it('uses correct icons for different tool types', () => {
    const testCases = [
      { name: 'write_file', icon: '✏️' },
      { name: 'bash', icon: '💻' },
      { name: 'grep', icon: '🔍' },
      { name: 'unknown_tool', icon: '🔧' }, // fallback
    ]

    testCases.forEach(({ name, icon }) => {
      const { rerender } = render(
        <ToolExecutionRow
          execution={{
            ...mockExecution,
            toolUse: { ...mockExecution.toolUse, name },
          }}
        />
      )

      expect(screen.getByText(icon)).toBeInTheDocument()
      rerender(<></>)
    })
  })

  it('shows command for bash tools', () => {
    const bashExecution: ToolExecution = {
      ...mockExecution,
      toolUse: {
        ...mockExecution.toolUse,
        name: 'bash',
        input: { command: 'npm test' },
      },
    }

    render(<ToolExecutionRow execution={bashExecution} />)

    expect(screen.getByText('npm test')).toBeInTheDocument()
  })

  it('collapses details when clicked again', async () => {
    const user = userEvent.setup()
    render(<ToolExecutionRow execution={mockExecution} />)

    const button = screen.getByRole('button')

    // Expand
    await user.click(button)
    await screen.findByText('File contents here...')

    // Collapse
    await user.click(button)

    // Wait for content to disappear
    await waitFor(() => {
      expect(screen.queryByText('File contents here...')).not.toBeInTheDocument()
    })
  })

  it('handles tool use without result gracefully', () => {
    const noResultExecution: ToolExecution = {
      ...mockExecution,
      toolResult: undefined,
      isRunning: false,
    }

    render(<ToolExecutionRow execution={noResultExecution} />)

    // Should still show the path from input as summary
    expect(screen.getByText('/src/app.tsx')).toBeInTheDocument()
    expect(() => screen.getByTestId('tool-execution-row')).not.toThrow()
  })
})

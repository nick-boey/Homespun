import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ToolResultRenderer } from './tool-result-renderer'
import type { ClaudeMessageContent } from '@/types/tool-execution'

// Mock the individual tool result components
vi.mock('./read-tool-result', () => ({
  ReadToolResult: ({ content }: { content: string }) => (
    <div data-testid="read-tool-result">{content}</div>
  ),
}))

vi.mock('./write-tool-result', () => ({
  WriteToolResult: ({ content }: { content: string }) => (
    <div data-testid="write-tool-result">{content}</div>
  ),
}))

vi.mock('./bash-tool-result', () => ({
  BashToolResult: ({ content }: { content: string }) => (
    <div data-testid="bash-tool-result">{content}</div>
  ),
}))

vi.mock('./grep-tool-result', () => ({
  GrepToolResult: ({ content }: { content: string }) => (
    <div data-testid="grep-tool-result">{content}</div>
  ),
}))

vi.mock('./generic-tool-result', () => ({
  GenericToolResult: ({ content }: { content: string }) => (
    <div data-testid="generic-tool-result">{content}</div>
  ),
}))

describe('ToolResultRenderer', () => {
  const mockToolResult: ClaudeMessageContent = {
    contentType: 'tool_result',
    toolUseId: 'tool1',
    content: 'Test content',
  }

  it('renders ReadToolResult for read_file tool', () => {
    render(
      <ToolResultRenderer
        toolName="read_file"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('read-tool-result')).toBeInTheDocument()
    expect(screen.getByText('Test content')).toBeInTheDocument()
  })

  it('renders ReadToolResult for read tool', () => {
    render(
      <ToolResultRenderer
        toolName="read"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('read-tool-result')).toBeInTheDocument()
  })

  it('renders WriteToolResult for write_file tool', () => {
    render(
      <ToolResultRenderer
        toolName="write_file"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('write-tool-result')).toBeInTheDocument()
  })

  it('renders WriteToolResult for write tool', () => {
    render(
      <ToolResultRenderer
        toolName="write"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('write-tool-result')).toBeInTheDocument()
  })

  it('renders WriteToolResult for edit tool', () => {
    render(
      <ToolResultRenderer
        toolName="edit"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('write-tool-result')).toBeInTheDocument()
  })

  it('renders BashToolResult for bash tool', () => {
    render(
      <ToolResultRenderer
        toolName="bash"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('bash-tool-result')).toBeInTheDocument()
  })

  it('renders GrepToolResult for grep tool', () => {
    render(
      <ToolResultRenderer
        toolName="grep"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('grep-tool-result')).toBeInTheDocument()
  })

  it('renders GrepToolResult for search tool', () => {
    render(
      <ToolResultRenderer
        toolName="search"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('grep-tool-result')).toBeInTheDocument()
  })

  it('renders GenericToolResult for unknown tools', () => {
    render(
      <ToolResultRenderer
        toolName="unknown_tool"
        toolResult={mockToolResult}
      />
    )

    expect(screen.getByTestId('generic-tool-result')).toBeInTheDocument()
  })

  it('handles error results with error styling', () => {
    const errorResult: ClaudeMessageContent = {
      ...mockToolResult,
      content: 'Error: Something went wrong',
      isError: true,
    }

    render(
      <ToolResultRenderer
        toolName="bash"
        toolResult={errorResult}
      />
    )

    const result = screen.getByTestId('bash-tool-result')
    expect(result).toBeInTheDocument()
    expect(result).toHaveTextContent('Error: Something went wrong')
  })

  it('passes tool input to components', () => {
    const toolInput = { path: 'test.ts' }

    render(
      <ToolResultRenderer
        toolName="read"
        toolResult={mockToolResult}
        toolInput={toolInput}
      />
    )

    expect(screen.getByTestId('read-tool-result')).toBeInTheDocument()
  })
})
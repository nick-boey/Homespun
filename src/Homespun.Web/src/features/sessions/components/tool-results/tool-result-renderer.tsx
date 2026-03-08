import type { ClaudeMessageContent } from '@/types/tool-execution'
import { ReadToolResult } from './read-tool-result'
import { WriteToolResult } from './write-tool-result'
import { BashToolResult } from './bash-tool-result'
import { GrepToolResult } from './grep-tool-result'
import { GenericToolResult } from './generic-tool-result'

interface ToolResultRendererProps {
  toolName: string
  toolResult: ClaudeMessageContent
  toolInput?: unknown
}

export function ToolResultRenderer({ toolName, toolResult, toolInput }: ToolResultRendererProps) {
  const content = toolResult.content || ''
  const isError = toolResult.isError || false

  // Normalize tool name to lowercase for consistent matching
  const normalizedToolName = toolName.toLowerCase()

  // Dispatch to specific component based on tool name
  switch (normalizedToolName) {
    case 'read':
    case 'read_file':
      return <ReadToolResult content={content} isError={isError} toolInput={toolInput} />

    case 'write':
    case 'write_file':
    case 'edit':
      return <WriteToolResult content={content} isError={isError} toolInput={toolInput} />

    case 'bash':
      return <BashToolResult content={content} isError={isError} toolInput={toolInput} />

    case 'grep':
    case 'search':
      return <GrepToolResult content={content} isError={isError} toolInput={toolInput} />

    default:
      return <GenericToolResult content={content} isError={isError} toolInput={toolInput} />
  }
}

import { CheckCircle, Search } from 'lucide-react'
import type { Toolkit } from '@assistant-ui/react'

import { CodeBlock } from '@/components/tool-ui/code-block'
import { Terminal } from '@/components/tool-ui/terminal'
import { cn } from '@/lib/utils'

import {
  AskUserQuestionRenderer,
  type AskUserQuestionArgs,
  type AskUserQuestionResult,
} from './tool-renderers/ask-user-question'
import {
  ProposePlanRenderer,
  type ProposePlanArgs,
  type ProposePlanResult,
} from './tool-renderers/propose-plan'

function parseArgs(argsText: string | undefined): Record<string, unknown> {
  if (!argsText) return {}
  try {
    const parsed = JSON.parse(argsText)
    return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

function parseResult<T>(result: unknown): T | undefined {
  if (result == null) return undefined
  if (typeof result === 'object') return result as T
  if (typeof result === 'string' && result.length > 0) {
    try {
      return JSON.parse(result) as T
    } catch {
      return undefined
    }
  }
  return undefined
}

function toString(value: unknown): string {
  if (value == null) return ''
  return typeof value === 'string' ? value : JSON.stringify(value, null, 2)
}

function codeBlockId(toolCallId: string, suffix: string): string {
  return `${toolCallId}-${suffix}`
}

export const toolkit: Toolkit = {
  Bash: {
    type: 'backend',
    render: ({ toolCallId, argsText, result, isError }) => {
      const args = parseArgs(argsText)
      const command = typeof args.command === 'string' ? args.command : ''
      const output = toString(result)
      return (
        <div
          className={cn(
            'overflow-hidden rounded-lg',
            isError && 'border-destructive/50 rounded-lg border'
          )}
        >
          <Terminal
            id={codeBlockId(toolCallId, 'bash')}
            command={command}
            stdout={isError ? undefined : output}
            stderr={isError ? output : undefined}
            exitCode={isError ? 1 : 0}
          />
        </div>
      )
    },
  },
  Read: {
    type: 'backend',
    render: ({ toolCallId, argsText, result, isError }) => {
      const args = parseArgs(argsText)
      const filePath =
        typeof args.file_path === 'string'
          ? args.file_path
          : typeof args.path === 'string'
            ? args.path
            : undefined
      const content = toString(result)

      if (isError) {
        return (
          <div className="text-destructive border-destructive/50 rounded border p-2 text-sm">
            {content}
          </div>
        )
      }

      return (
        <div className="space-y-2">
          {filePath && (
            <div className="text-muted-foreground text-xs">
              File: <span className="font-mono">{filePath}</span>
            </div>
          )}
          <div className="border-border overflow-hidden rounded border">
            <CodeBlock
              id={codeBlockId(toolCallId, 'read')}
              language="text"
              code={content}
              lineNumbers="visible"
            />
          </div>
        </div>
      )
    },
  },
  Grep: {
    type: 'backend',
    render: ({ toolCallId, argsText, result, isError }) => {
      const args = parseArgs(argsText)
      const pattern = typeof args.pattern === 'string' ? args.pattern : undefined
      const path = typeof args.path === 'string' ? args.path : undefined
      const content = toString(result)

      if (isError) {
        return (
          <div className="text-destructive border-destructive/50 rounded border p-2 text-sm">
            {content}
          </div>
        )
      }

      const lines = content.split('\n').filter((line) => line.trim())
      const matchCount = lines.length

      return (
        <div className="space-y-2">
          <div className="text-muted-foreground flex items-center gap-2 text-xs">
            <Search className="size-3" />
            <span>
              {matchCount} {matchCount === 1 ? 'match' : 'matches'}
              {pattern && (
                <>
                  {' '}
                  for <span className="bg-muted rounded px-1 py-0.5 font-mono">{pattern}</span>
                </>
              )}
              {path && (
                <>
                  {' '}
                  in <span className="font-mono">{path}</span>
                </>
              )}
            </span>
          </div>
          <div className="border-border overflow-hidden rounded border">
            <CodeBlock
              id={codeBlockId(toolCallId, 'grep')}
              language="text"
              code={content}
              lineNumbers="hidden"
            />
          </div>
        </div>
      )
    },
  },
  /**
   * Interactive agent-initiated tools — see
   * `openspec/changes/questions-plans-as-tools`. `type: "human"` matches the
   * semantics: the agent requests input, the user supplies the result. The
   * server emits these as canonical `TOOL_CALL_START/ARGS/END` for A2A
   * `StatusUpdate{input-required}`; the renderer calls `addResult` and
   * dispatches to the hub so the worker is unblocked. When a
   * `TOOL_CALL_RESULT` arrives (from the hub-synthesised tool_result
   * message), the Toolkit switches the render into receipt mode.
   *
   * `parameters` is an open JSON Schema — the wire contract for args lives on
   * the server; the client treats the payload as opaque and parses into the
   * renderer's typed view.
   */
  ask_user_question: {
    type: 'human',
    parameters: { type: 'object', additionalProperties: true },
    render: ({ argsText, result, addResult }) => {
      const args = parseArgs(argsText) as unknown as AskUserQuestionArgs
      const committed = parseResult<AskUserQuestionResult>(result)
      return (
        <AskUserQuestionRenderer args={args} result={committed} addResult={(v) => addResult(v)} />
      )
    },
  },
  propose_plan: {
    type: 'human',
    parameters: { type: 'object', additionalProperties: true },
    render: ({ argsText, result, addResult }) => {
      const args = parseArgs(argsText) as unknown as ProposePlanArgs
      const committed = parseResult<ProposePlanResult>(result)
      return <ProposePlanRenderer args={args} result={committed} addResult={(v) => addResult(v)} />
    },
  },
  Write: {
    type: 'backend',
    render: ({ argsText, result, isError }) => {
      const args = parseArgs(argsText)
      const filePath =
        typeof args.file_path === 'string'
          ? args.file_path
          : typeof args.path === 'string'
            ? args.path
            : undefined
      const content = toString(result)

      if (isError) {
        return (
          <div className="text-destructive border-destructive/50 rounded border p-2 text-sm">
            <span>{content}</span>
          </div>
        )
      }

      return (
        <div className="text-muted-foreground text-sm">
          <div className="flex items-start gap-2">
            <CheckCircle className="mt-0.5 size-4 text-green-600 dark:text-green-400" />
            <div>
              <span>{content}</span>
              {filePath && (
                <div className="mt-1 text-xs">
                  File: <span className="font-mono">{filePath}</span>
                </div>
              )}
            </div>
          </div>
        </div>
      )
    },
  },
}

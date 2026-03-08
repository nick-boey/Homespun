import * as React from 'react'
import type { ToolExecution } from '@/types/tool-execution'
import { cn } from '@/lib/utils'
import { Loader2, ChevronRight, ChevronDown } from 'lucide-react'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { ToolResultRenderer } from './tool-results/tool-result-renderer'

interface ToolExecutionRowProps {
  execution: ToolExecution
  className?: string
}

export function ToolExecutionRow({ execution, className }: ToolExecutionRowProps) {
  const [isExpanded, setIsExpanded] = React.useState(false)
  const hasError = execution.toolResult?.isError

  return (
    <Collapsible open={isExpanded} onOpenChange={setIsExpanded}>
      <div
        className={cn(
          'rounded-md bg-background/50 overflow-hidden',
          hasError && 'border-l-4 border-destructive',
          className
        )}
        data-testid="tool-execution-row"
        data-error={hasError ? 'true' : 'false'}
      >
        <CollapsibleTrigger asChild>
          <button
            type="button"
            className={cn(
              'flex items-center gap-2 p-2 w-full text-left',
              'hover:bg-accent/50 transition-colors cursor-pointer'
            )}
          >
            {/* Expand/collapse icon */}
            <span className="text-muted-foreground">
              {isExpanded ? <ChevronDown className="size-3" /> : <ChevronRight className="size-3" />}
            </span>

            {/* Tool icon */}
            <span className="text-sm">{getToolIcon(execution.toolUse.name)}</span>

            {/* Tool name */}
            <span className="font-medium text-sm">{execution.toolUse.name}</span>

            {/* Status/summary */}
            <span className="text-sm text-muted-foreground flex-1">
              {getToolSummary(execution)}
            </span>

            {/* Running indicator */}
            {execution.isRunning && (
              <Loader2 className="size-4 animate-spin" data-testid="running-indicator" />
            )}
          </button>
        </CollapsibleTrigger>

        <CollapsibleContent>
          <div className="px-4 pb-2 pt-0">
            {execution.toolResult ? (
              <ToolResultRenderer
                toolName={execution.toolUse.name}
                toolResult={execution.toolResult}
                toolInput={execution.toolUse.input}
              />
            ) : execution.isRunning ? (
              <div className="text-sm text-muted-foreground">Waiting for result...</div>
            ) : (
              <div className="text-sm text-muted-foreground">No result available</div>
            )}
          </div>
        </CollapsibleContent>
      </div>
    </Collapsible>
  )
}

// Helper to get tool icon based on name
function getToolIcon(toolName: string): string {
  const icons: Record<string, string> = {
    read_file: '📄',
    read: '📄',
    write_file: '✏️',
    write: '📝',
    edit: '✏️',
    delete: '🗑️',
    bash: '💻',
    grep: '🔍',
    search: '🔍',
  }

  return icons[toolName.toLowerCase()] || '🔧'
}

// Helper to get tool summary
function getToolSummary(execution: ToolExecution): string {
  if (execution.isRunning) {
    return 'Running...'
  }

  if (execution.toolResult?.isError) {
    return 'Error'
  }

  // Try to extract a meaningful summary from input
  const input = execution.toolUse.input as Record<string, unknown>
  if (input.path) {
    return String(input.path)
  }
  if (input.command) {
    return String(input.command)
  }
  if (input.pattern) {
    return String(input.pattern)
  }

  return 'Completed'
}
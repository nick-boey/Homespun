import { memo, useCallback } from 'react'
import { GitCommitVertical, GitFork } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ExecutionMode } from '@/api'

export interface ExecutionModeToggleProps {
  executionMode: ExecutionMode
  onToggle: () => void
  disabled?: boolean
}

/**
 * Toggle button for switching between Series and Parallel execution modes.
 *
 * - Series mode (executionMode='series'): Children execute sequentially
 * - Parallel mode (executionMode='parallel'): Children execute concurrently
 */
export const ExecutionModeToggle = memo(function ExecutionModeToggle({
  executionMode,
  onToggle,
  disabled = false,
}: ExecutionModeToggleProps) {
  const isSeries = executionMode === ExecutionMode.SERIES

  const handleClick = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation()
      onToggle()
    },
    [onToggle]
  )

  return (
    <Button
      variant="ghost"
      size="sm"
      className="text-muted-foreground hover:text-foreground h-6 w-6 shrink-0 p-0"
      onClick={handleClick}
      disabled={disabled}
      title={
        isSeries
          ? 'Children execute in series (click to toggle)'
          : 'Children execute in parallel (click to toggle)'
      }
      aria-label={isSeries ? 'Series execution mode' : 'Parallel execution mode'}
    >
      {isSeries ? (
        <GitCommitVertical className="h-3.5 w-3.5" />
      ) : (
        <GitFork className="h-3.5 w-3.5" />
      )}
    </Button>
  )
})

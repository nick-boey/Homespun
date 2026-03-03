import { memo } from 'react'
import { CheckCircle, XCircle, Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'

export type MutationStatus = 'idle' | 'pending' | 'success' | 'error'

export interface MutationIndicatorProps {
  /** Current status of the mutation */
  status: MutationStatus
  /** Text to display during pending state */
  pendingText?: string
  /** Text to display on success */
  successText?: string
  /** Text to display on error */
  errorText?: string
  /** Additional className */
  className?: string
  /** Whether to show text labels */
  showText?: boolean
  /** Auto-hide success/error after this many milliseconds (0 to disable) */
  autoHideDelay?: number
  /** Callback when auto-hide completes */
  onAutoHide?: () => void
}

/**
 * Visual indicator for mutation states.
 * Shows spinner during pending, checkmark on success, X on error.
 */
export const MutationIndicator = memo(function MutationIndicator({
  status,
  pendingText = 'Saving...',
  successText = 'Saved',
  errorText = 'Failed',
  className,
  showText = true,
}: MutationIndicatorProps) {
  if (status === 'idle') {
    return null
  }

  return (
    <div
      data-testid="mutation-indicator"
      data-status={status}
      className={cn(
        'inline-flex items-center gap-1.5 text-sm',
        status === 'pending' && 'text-muted-foreground',
        status === 'success' && 'text-green-600 dark:text-green-400',
        status === 'error' && 'text-destructive',
        className
      )}
    >
      {status === 'pending' && (
        <>
          <Loader2 className="h-4 w-4 animate-spin" />
          {showText && <span>{pendingText}</span>}
        </>
      )}
      {status === 'success' && (
        <>
          <CheckCircle className="h-4 w-4" />
          {showText && <span>{successText}</span>}
        </>
      )}
      {status === 'error' && (
        <>
          <XCircle className="h-4 w-4" />
          {showText && <span>{errorText}</span>}
        </>
      )}
    </div>
  )
})

export interface UseMutationIndicatorOptions {
  /** Auto-hide success state after this many milliseconds */
  successDuration?: number
  /** Auto-hide error state after this many milliseconds */
  errorDuration?: number
}

/**
 * Hook to derive mutation status from TanStack Query mutation state.
 *
 * @example
 * ```tsx
 * const mutation = useUpdateIssue()
 * const status = getMutationStatus(mutation)
 *
 * return (
 *   <div>
 *     <Button onClick={() => mutation.mutate(data)}>Save</Button>
 *     <MutationIndicator status={status} />
 *   </div>
 * )
 * ```
 */
export function getMutationStatus(mutation: {
  isPending?: boolean
  isSuccess?: boolean
  isError?: boolean
}): MutationStatus {
  if (mutation.isPending) return 'pending'
  if (mutation.isSuccess) return 'success'
  if (mutation.isError) return 'error'
  return 'idle'
}

import { memo } from 'react'
import { cn } from '@/lib/utils'
import { CheckCircle, XCircle, Clock } from 'lucide-react'

export interface CiStatusBadgeProps {
  checksPassing?: boolean | null
  size?: 'sm' | 'md'
  className?: string
}

const SIZE_CLASSES = {
  sm: 'px-1.5 py-0.5 text-xs',
  md: 'px-2 py-1 text-sm',
}

const ICON_SIZE_CLASSES = {
  sm: 'h-3 w-3',
  md: 'h-4 w-4',
}

/**
 * Badge component for displaying CI check status.
 */
export const CiStatusBadge = memo(function CiStatusBadge({
  checksPassing,
  size = 'md',
  className,
}: CiStatusBadgeProps) {
  if (checksPassing === true) {
    return (
      <span
        className={cn(
          'inline-flex shrink-0 items-center gap-1 rounded font-medium',
          'bg-green-500/20 text-green-700 dark:text-green-400',
          SIZE_CLASSES[size],
          className
        )}
      >
        <CheckCircle className={ICON_SIZE_CLASSES[size]} />
        Checks Passing
      </span>
    )
  }

  if (checksPassing === false) {
    return (
      <span
        className={cn(
          'inline-flex shrink-0 items-center gap-1 rounded font-medium',
          'bg-red-500/20 text-red-700 dark:text-red-400',
          SIZE_CLASSES[size],
          className
        )}
      >
        <XCircle className={ICON_SIZE_CLASSES[size]} />
        Checks Failing
      </span>
    )
  }

  // null or undefined - pending
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center gap-1 rounded font-medium',
        'bg-yellow-500/20 text-yellow-700 dark:text-yellow-400',
        SIZE_CLASSES[size],
        className
      )}
    >
      <Clock className={ICON_SIZE_CLASSES[size]} />
      Checks Pending
    </span>
  )
})

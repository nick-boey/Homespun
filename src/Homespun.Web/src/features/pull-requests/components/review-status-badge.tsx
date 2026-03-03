import { memo } from 'react'
import { cn } from '@/lib/utils'
import { ThumbsUp, MessageSquare, Clock } from 'lucide-react'

export interface ReviewStatusBadgeProps {
  isApproved?: boolean | null
  approvalCount?: number
  changesRequestedCount?: number
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
 * Badge component for displaying PR review status.
 */
export const ReviewStatusBadge = memo(function ReviewStatusBadge({
  isApproved,
  approvalCount = 0,
  changesRequestedCount = 0,
  size = 'md',
  className,
}: ReviewStatusBadgeProps) {
  // Show changes requested if any
  if (changesRequestedCount > 0) {
    const label =
      changesRequestedCount === 1
        ? '1 Change Requested'
        : `${changesRequestedCount} Changes Requested`
    return (
      <span
        className={cn(
          'inline-flex shrink-0 items-center gap-1 rounded font-medium',
          'bg-orange-500/20 text-orange-700 dark:text-orange-400',
          SIZE_CLASSES[size],
          className
        )}
      >
        <MessageSquare className={ICON_SIZE_CLASSES[size]} />
        {label}
      </span>
    )
  }

  // Show approvals if approved
  if (isApproved || approvalCount > 0) {
    const label = approvalCount === 1 ? '1 Approval' : `${approvalCount} Approvals`
    return (
      <span
        className={cn(
          'inline-flex shrink-0 items-center gap-1 rounded font-medium',
          'bg-green-500/20 text-green-700 dark:text-green-400',
          SIZE_CLASSES[size],
          className
        )}
      >
        <ThumbsUp className={ICON_SIZE_CLASSES[size]} />
        {label}
      </span>
    )
  }

  // Default: awaiting review
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center gap-1 rounded font-medium',
        'bg-gray-500/20 text-gray-700 dark:text-gray-400',
        SIZE_CLASSES[size],
        className
      )}
    >
      <Clock className={ICON_SIZE_CLASSES[size]} />
      Awaiting Review
    </span>
  )
})

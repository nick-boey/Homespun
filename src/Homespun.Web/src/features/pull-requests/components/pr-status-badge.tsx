import { memo } from 'react'
import { PullRequestStatus } from '@/api'
import { cn } from '@/lib/utils'

export interface PrStatusBadgeProps {
  status: PullRequestStatus
  size?: 'sm' | 'md'
  className?: string
}

const STATUS_CONFIG: Record<number, { label: string; bgColor: string; textColor: string }> = {
  [PullRequestStatus[0]]: {
    label: 'Draft',
    bgColor: 'bg-gray-500/20',
    textColor: 'text-gray-700 dark:text-gray-400',
  },
  [PullRequestStatus[1]]: {
    label: 'Open',
    bgColor: 'bg-green-500/20',
    textColor: 'text-green-700 dark:text-green-400',
  },
  [PullRequestStatus[2]]: {
    label: 'Merged',
    bgColor: 'bg-purple-500/20',
    textColor: 'text-purple-700 dark:text-purple-400',
  },
  [PullRequestStatus[3]]: {
    label: 'Closed',
    bgColor: 'bg-red-500/20',
    textColor: 'text-red-700 dark:text-red-400',
  },
  [PullRequestStatus[4]]: {
    label: 'Changes Requested',
    bgColor: 'bg-orange-500/20',
    textColor: 'text-orange-700 dark:text-orange-400',
  },
  [PullRequestStatus[5]]: {
    label: 'Approved',
    bgColor: 'bg-blue-500/20',
    textColor: 'text-blue-700 dark:text-blue-400',
  },
  [PullRequestStatus[6]]: {
    label: 'Review Required',
    bgColor: 'bg-yellow-500/20',
    textColor: 'text-yellow-700 dark:text-yellow-400',
  },
}

const DEFAULT_STATUS_CONFIG = {
  label: 'Unknown',
  bgColor: 'bg-gray-500/20',
  textColor: 'text-gray-700 dark:text-gray-400',
}

const SIZE_CLASSES = {
  sm: 'px-1.5 py-0.5 text-xs',
  md: 'px-2 py-1 text-sm',
}

/**
 * Badge component for displaying pull request status.
 */
export const PrStatusBadge = memo(function PrStatusBadge({
  status,
  size = 'md',
  className,
}: PrStatusBadgeProps) {
  const config = STATUS_CONFIG[status] ?? DEFAULT_STATUS_CONFIG

  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center rounded font-medium',
        config.bgColor,
        config.textColor,
        SIZE_CLASSES[size],
        className
      )}
    >
      {config.label}
    </span>
  )
})

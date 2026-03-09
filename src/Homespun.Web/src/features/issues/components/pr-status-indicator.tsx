import { GitBranch, Check, X, Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'

interface PrStatusIndicatorProps {
  checksPassing: boolean | null
  hasConflicts: boolean
}

export function PrStatusIndicator({ checksPassing, hasConflicts }: PrStatusIndicatorProps) {
  return (
    <div className="flex items-center gap-1">
      {/* Merge conflict indicator */}
      <GitBranch
        className={cn('h-3 w-3', hasConflicts ? 'text-red-500' : 'text-green-500')}
        aria-label={hasConflicts ? 'Has merge conflicts' : 'No merge conflicts'}
      />

      {/* CI checks indicator */}
      {checksPassing === null ? (
        <Loader2 className="h-3 w-3 animate-spin text-yellow-500" aria-label="Tests running" />
      ) : checksPassing ? (
        <Check className="h-3 w-3 text-green-500" aria-label="Tests passing" />
      ) : (
        <X className="h-3 w-3 text-red-500" aria-label="Tests failing" />
      )}
    </div>
  )
}

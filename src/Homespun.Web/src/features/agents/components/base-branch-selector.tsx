import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useBranches } from '../hooks'
import { Loader } from '@/components/ui/loader'
import { Button } from '@/components/ui/button'
import { AlertCircle, RefreshCw } from 'lucide-react'

interface BaseBranchSelectorProps {
  /** Path to the git repository */
  repoPath: string | undefined
  /** The project's default branch */
  defaultBranch?: string | null
  /** Currently selected branch */
  value: string
  /** Called when selection changes */
  onChange: (value: string) => void
  /** Whether the selector is disabled */
  disabled?: boolean
  /** Optional aria-label for accessibility */
  'aria-label'?: string
}

/**
 * A dropdown selector for choosing a base branch.
 * Fetches available branches from the repository and prioritizes the default branch.
 */
export function BaseBranchSelector({
  repoPath,
  defaultBranch,
  value,
  onChange,
  disabled = false,
  'aria-label': ariaLabel = 'Select base branch',
}: BaseBranchSelectorProps) {
  const { branches, isLoading, isError, isFetching, refetch } = useBranches(repoPath, defaultBranch)

  // If no value is set and branches are loaded, auto-select default or first branch
  const effectiveValue = value || defaultBranch || branches[0]?.shortName || ''

  if (isLoading) {
    return (
      <div className="flex h-9 items-center gap-2 px-3">
        <Loader variant="circular" size="sm" />
        <span className="text-muted-foreground text-sm">Loading branches...</span>
      </div>
    )
  }

  if (isError) {
    return (
      <div
        role="alert"
        className="border-destructive/50 bg-destructive/5 flex items-center justify-between gap-2 rounded-md border px-3 py-2"
      >
        <div className="text-destructive flex items-center gap-2 text-sm">
          <AlertCircle className="size-4" aria-hidden="true" />
          <span>Failed to load branches</span>
        </div>
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={() => refetch()}
          disabled={isFetching}
        >
          <RefreshCw
            className={`size-3.5 ${isFetching ? 'animate-spin' : ''}`}
            aria-hidden="true"
          />
          Retry
        </Button>
      </div>
    )
  }

  return (
    <Select
      value={effectiveValue}
      onValueChange={onChange}
      disabled={disabled || branches.length === 0}
    >
      <SelectTrigger className="w-full" aria-label={ariaLabel}>
        <SelectValue placeholder="Select branch..." />
      </SelectTrigger>
      <SelectContent>
        {branches.map((branch) => (
          <SelectItem key={branch.shortName} value={branch.shortName ?? ''}>
            {branch.shortName}
            {branch.shortName === defaultBranch && (
              <span className="text-muted-foreground ml-1">(default)</span>
            )}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

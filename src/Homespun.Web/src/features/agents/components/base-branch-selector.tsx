import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useBranches } from '../hooks'
import { Loader } from '@/components/ui/loader'

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
  const { branches, isLoading, isError } = useBranches(repoPath, defaultBranch)

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
    return <div className="text-destructive text-sm">Failed to load branches</div>
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

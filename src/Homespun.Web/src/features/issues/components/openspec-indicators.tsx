/**
 * Branch and change status indicators for an issue row.
 *
 * Visual grammar (see openspec-integration design doc §D6):
 * - Branch symbol: gray (none), white (branch, no change), amber (branch with change).
 * - Change symbol: red ◐ (incomplete), amber ◐ (ready-to-apply), green ● (ready-to-archive),
 *   blue ✓ (archived). Omitted when no change.
 */

import { BranchPresence, ChangePhase } from '@/api'
import type { IssueOpenSpecState } from '@/api/generated/types.gen'
import { cn } from '@/lib/utils'

export interface OpenSpecIndicatorsProps {
  state: IssueOpenSpecState | null | undefined
}

export function OpenSpecIndicators({ state }: OpenSpecIndicatorsProps) {
  if (!state) return null
  const branchColor = getBranchColor(state.branchState)
  const change = getChangeSymbol(state.changeState)

  return (
    <span className="flex items-center gap-1" data-testid="openspec-indicators">
      <span
        aria-label={`branch ${state.branchState}`}
        data-testid="openspec-branch-symbol"
        data-branch-state={state.branchState}
        className={cn('inline-block h-2 w-2 rounded-full', branchColor)}
      />
      {change ? (
        <span
          aria-label={`change ${state.changeState}`}
          data-testid="openspec-change-symbol"
          data-change-state={state.changeState}
          className={cn('text-[12px] leading-none', change.color)}
        >
          {change.glyph}
        </span>
      ) : null}
    </span>
  )
}

function getBranchColor(branch: BranchPresence): string {
  switch (branch) {
    case BranchPresence.WITH_CHANGE:
      return 'bg-amber-500'
    case BranchPresence.EXISTS:
      return 'bg-white ring-1 ring-muted-foreground/40'
    case BranchPresence.NONE:
    default:
      return 'bg-muted-foreground/40'
  }
}

function getChangeSymbol(phase: ChangePhase): { glyph: string; color: string } | null {
  switch (phase) {
    case ChangePhase.INCOMPLETE:
      return { glyph: '◐', color: 'text-red-500' }
    case ChangePhase.READY_TO_APPLY:
      return { glyph: '◐', color: 'text-amber-500' }
    case ChangePhase.READY_TO_ARCHIVE:
      return { glyph: '●', color: 'text-green-500' }
    case ChangePhase.ARCHIVED:
      return { glyph: '✓', color: 'text-blue-500' }
    case ChangePhase.NONE:
    default:
      return null
  }
}

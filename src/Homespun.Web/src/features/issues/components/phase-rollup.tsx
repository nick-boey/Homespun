/**
 * Phase roll-up badges and modal for an issue's linked OpenSpec change.
 *
 * The change's tasks.md is parsed server-side into one PhaseSummary per
 * `## N. Phase` heading. Each badge shows `done/total`; clicking it opens a
 * modal listing the phase's leaf tasks with their checkbox state.
 */

import { useState } from 'react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { cn } from '@/lib/utils'
import type { PhaseSummary } from '@/api/generated/types.gen'

export interface PhaseRollupBadgesProps {
  changeName: string | null | undefined
  phases: PhaseSummary[] | null | undefined
}

export function PhaseRollupBadges({ changeName, phases }: PhaseRollupBadgesProps) {
  const [activePhase, setActivePhase] = useState<PhaseSummary | null>(null)

  if (!phases || phases.length === 0) return null

  return (
    <div className="flex flex-wrap gap-1" data-testid="phase-rollup-badges">
      {phases.map((phase) => {
        const done = phase.done ?? 0
        const total = phase.total ?? 0
        const complete = total > 0 && done >= total
        return (
          <button
            key={phase.name}
            type="button"
            onClick={(e) => {
              e.stopPropagation()
              setActivePhase(phase)
            }}
            className={cn(
              'shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium transition-colors',
              complete
                ? 'bg-green-500/20 text-green-700 dark:text-green-400'
                : 'bg-muted text-muted-foreground hover:bg-muted/80'
            )}
            data-testid="phase-badge"
            data-phase-name={phase.name}
            title={`${phase.name} (${done}/${total})`}
          >
            {phase.name}: {done}/{total}
          </button>
        )
      })}

      <Dialog open={!!activePhase} onOpenChange={(open) => !open && setActivePhase(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{activePhase?.name}</DialogTitle>
            <DialogDescription>
              {changeName ? (
                <>
                  <span className="font-mono">{changeName}</span>
                  {' · '}
                </>
              ) : null}
              {activePhase?.done ?? 0}/{activePhase?.total ?? 0} tasks complete
            </DialogDescription>
          </DialogHeader>
          <ul className="space-y-2" data-testid="phase-task-list">
            {(activePhase?.tasks ?? []).map((task, idx) => (
              <li
                key={idx}
                className="flex items-start gap-2 text-sm"
                data-testid="phase-task"
                data-done={task.done ? 'true' : 'false'}
              >
                <span
                  className={cn(
                    'mt-0.5 inline-block h-4 w-4 flex-shrink-0 rounded border',
                    task.done
                      ? 'border-green-500 bg-green-500 text-white'
                      : 'border-muted-foreground/40'
                  )}
                  aria-hidden="true"
                >
                  {task.done ? (
                    <svg viewBox="0 0 16 16" className="h-full w-full fill-white p-0.5">
                      <path d="M6 12l-4-4 1.4-1.4L6 9.2l6.6-6.6L14 4z" />
                    </svg>
                  ) : null}
                </span>
                <span className={cn(task.done && 'text-muted-foreground line-through')}>
                  {task.description}
                </span>
              </li>
            ))}
            {(!activePhase?.tasks || activePhase.tasks.length === 0) && (
              <li className="text-muted-foreground text-sm italic">No tasks in this phase.</li>
            )}
          </ul>
        </DialogContent>
      </Dialog>
    </div>
  )
}

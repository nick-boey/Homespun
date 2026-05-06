/**
 * Modal showing the OpenSpec change linked to an issue: phases as collapsible
 * branches, tasks as leaves with a checkbox-style indicator. Triggered from the
 * change-state glyph in `OpenSpecIndicators`.
 */

import { useState, type ReactNode } from 'react'
import { ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import type { IssueOpenSpecState, PhaseSummary } from '@/api/generated/types.gen'

export interface OpenSpecChangeDialogProps {
  state: IssueOpenSpecState
  trigger: ReactNode
}

export function OpenSpecChangeDialog({ state, trigger }: OpenSpecChangeDialogProps) {
  const phases = state.phases ?? []
  const title = state.changeName ?? 'OpenSpec change'

  return (
    <Dialog>
      <DialogTrigger asChild>{trigger}</DialogTrigger>
      <DialogContent
        data-testid="openspec-change-dialog"
        className="sm:max-w-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <DialogHeader>
          <DialogTitle className="font-mono text-base">{title}</DialogTitle>
          <DialogDescription>
            {phases.length === 0
              ? 'No phases recorded for this change.'
              : `${phases.length} phase${phases.length === 1 ? '' : 's'} · status: ${state.changeState}`}
          </DialogDescription>
        </DialogHeader>
        <ul
          className="max-h-[60vh] space-y-1 overflow-y-auto pr-1 text-sm"
          data-testid="openspec-change-dialog-tree"
        >
          {phases.map((phase, idx) => (
            <PhaseNode key={`${phase.name ?? 'phase'}-${idx}`} phase={phase} />
          ))}
        </ul>
      </DialogContent>
    </Dialog>
  )
}

function PhaseNode({ phase }: { phase: PhaseSummary }) {
  const [open, setOpen] = useState(true)
  const tasks = phase.tasks ?? []
  const done = phase.done ?? 0
  const total = phase.total ?? tasks.length
  const isComplete = total > 0 && done >= total

  return (
    <li data-testid="openspec-change-dialog-phase" data-phase-name={phase.name ?? ''}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="hover:bg-muted flex w-full items-center gap-2 rounded px-1 py-1 text-left"
        aria-expanded={open}
      >
        <ChevronRight
          className={cn('h-3.5 w-3.5 transition-transform', open && 'rotate-90')}
          aria-hidden="true"
        />
        <span className="font-medium">{phase.name ?? 'Unnamed phase'}</span>
        <span className={cn('text-xs', isComplete ? 'text-green-600' : 'text-muted-foreground')}>
          {done}/{total}
        </span>
      </button>
      {open && (
        <ul className="border-muted ml-5 space-y-1 border-l pl-3">
          {tasks.length === 0 ? (
            <li className="text-muted-foreground py-1 text-xs italic">No tasks in this phase.</li>
          ) : (
            tasks.map((task, idx) => (
              <li
                key={idx}
                data-testid="openspec-change-dialog-task"
                className="flex items-start gap-2 py-0.5"
              >
                <span
                  aria-hidden="true"
                  className={cn(
                    'mt-0.5 inline-flex h-4 w-4 shrink-0 items-center justify-center rounded border text-[10px]',
                    task.done
                      ? 'border-green-600 bg-green-600 text-white'
                      : 'border-muted-foreground/40'
                  )}
                >
                  {task.done ? '✓' : ''}
                </span>
                <span className={cn(task.done && 'text-muted-foreground line-through')}>
                  {task.description ?? '(no description)'}
                </span>
              </li>
            ))
          )}
        </ul>
      )}
    </li>
  )
}

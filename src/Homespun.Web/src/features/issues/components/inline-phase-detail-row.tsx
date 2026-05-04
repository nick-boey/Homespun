import { memo } from 'react'
import type { TaskGraphPhaseRenderLine } from '../services'
import { calculateSvgWidth, getLaneCenterX } from './task-graph-svg'

export interface InlinePhaseDetailRowProps {
  line: TaskGraphPhaseRenderLine
  maxLanes: number
}

export const InlinePhaseDetailRow = memo(function InlinePhaseDetailRow({
  line,
  maxLanes,
}: InlinePhaseDetailRowProps) {
  const svgWidth = calculateSvgWidth(maxLanes)
  const parentLaneX = getLaneCenterX(line.lane - 1 >= 0 ? line.lane - 1 : 0)

  return (
    <div
      className="bg-muted/30 border-muted animate-expand flex overflow-hidden border-t"
      role="region"
      aria-label="Phase tasks"
      data-testid="inline-phase-detail-row"
      data-phase-id={line.phaseId}
    >
      {/* SVG gutter — lane guide lines + vertical continuation in the parent issue's lane */}
      <svg
        width={svgWidth}
        height="100%"
        className="shrink-0 self-stretch"
        aria-hidden="true"
        style={{ minHeight: 40 }}
      >
        {Array.from({ length: maxLanes }, (_, i) => (
          <line
            key={`guide-${i}`}
            x1={getLaneCenterX(i)}
            y1={0}
            x2={getLaneCenterX(i)}
            y2="100%"
            stroke="#e5e7eb"
            strokeWidth={1}
            opacity={0.3}
          />
        ))}
        {/* Vertical continuation line in the parent issue's lane */}
        <line
          x1={parentLaneX}
          y1={0}
          x2={parentLaneX}
          y2="100%"
          stroke="#6b7280"
          strokeWidth={1}
          strokeDasharray="4 3"
          opacity={0.5}
        />
      </svg>
      <ul
        className="max-h-[400px] flex-1 space-y-1 overflow-y-auto px-3 py-3"
        data-testid="phase-task-list"
      >
        {line.tasks.map((task, idx) => (
          <li key={idx} className="flex items-start gap-2 text-sm">
            <svg
              width={16}
              height={16}
              viewBox="0 0 16 16"
              className="mt-0.5 shrink-0"
              aria-hidden="true"
            >
              {task.done ? (
                <>
                  <rect x={1} y={1} width={14} height={14} rx={2} fill="#22c55e" />
                  <path
                    d="M4 8 L7 11 L12 5"
                    stroke="white"
                    strokeWidth={2}
                    fill="none"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </>
              ) : (
                <rect
                  x={1}
                  y={1}
                  width={14}
                  height={14}
                  rx={2}
                  fill="none"
                  stroke="currentColor"
                  strokeWidth={1.5}
                  className="text-muted-foreground"
                />
              )}
            </svg>
            <span className={task.done ? 'text-muted-foreground line-through' : ''}>
              {task.description}
            </span>
          </li>
        ))}
        {line.tasks.length === 0 && (
          <li className="text-muted-foreground text-sm italic">No tasks in this phase.</li>
        )}
      </ul>
    </div>
  )
})

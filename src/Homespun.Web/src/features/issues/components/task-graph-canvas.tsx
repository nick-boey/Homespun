/**
 * TaskGraphCanvas - Single SVG canvas for rendering the entire task graph.
 *
 * Uses D3.js for layout calculation and edge path generation.
 * Issue descriptions and controls are rendered using foreignObject elements.
 */

import { memo, useRef, useMemo, useEffect, useCallback, useState, useLayoutEffect } from 'react'
import { cn } from '@/lib/utils'
import { IssueType, IssueStatus, ExecutionMode, ClaudeSessionStatus } from '@/api'
import { computeD3Layout, getContentX } from '../services/task-graph-d3-layout'
import {
  animateNodes,
  animateEdges,
  animateSvgHeight,
  TRANSITION_DURATION,
} from '../services/task-graph-animations'
import {
  TaskGraphIssueRowContent,
  TaskGraphIssueExpandedContent,
  TaskGraphPrRowContent,
  TaskGraphSeparatorContent,
  TaskGraphLoadMoreContent,
} from './task-graph-row-content'
import { ROW_HEIGHT, NODE_RADIUS } from './task-graph-svg'
import { HiddenParentIndicator } from './task-graph-svg-elements'
import type {
  TaskGraphRenderLine,
  TaskGraphIssueRenderLine,
  TaskGraphPrRenderLine,
} from '../services'

export interface TaskGraphCanvasProps {
  renderLines: TaskGraphRenderLine[]
  maxLanes: number
  projectId: string
  expandedIds: Set<string>
  selectedIssueId?: string | null
  onSelectIssue?: (issueId: string | null) => void
  onToggleExpand?: (issueId: string) => void
  onEditIssue?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  onOpenSession?: (sessionId: string) => void
  onTypeChange?: (issueId: string, newType: IssueType) => void
  onStatusChange?: (issueId: string, newStatus: IssueStatus) => void
  onExecutionModeChange?: (issueId: string, newMode: ExecutionMode) => void
  onLoadMorePrs?: () => void
  searchQuery?: string
  moveSourceIssueId?: string | null
  isMoveOperationActive?: boolean
  className?: string
}

/**
 * Maps agent status to ring color.
 */
function getAgentStatusColor(status: string | null): string | null {
  if (!status) return null

  switch (status) {
    case ClaudeSessionStatus.STARTING:
    case ClaudeSessionStatus.RUNNING_HOOKS:
    case ClaudeSessionStatus.RUNNING:
      return '#3b82f6'
    case ClaudeSessionStatus.WAITING_FOR_INPUT:
    case ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER:
    case ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION:
      return '#eab308'
    case ClaudeSessionStatus.ERROR:
      return '#ef4444'
    default:
      return null
  }
}

/**
 * Renders the task graph as a single large SVG with foreignObject elements
 * for issue content.
 */
export const TaskGraphCanvas = memo(function TaskGraphCanvas({
  renderLines,
  maxLanes,
  projectId,
  expandedIds,
  selectedIssueId,
  onSelectIssue,
  onToggleExpand,
  onEditIssue,
  onRunAgent,
  onOpenSession,
  onTypeChange,
  onStatusChange,
  onExecutionModeChange,
  onLoadMorePrs,
  searchQuery,
  moveSourceIssueId,
  isMoveOperationActive,
  className,
}: TaskGraphCanvasProps) {
  const svgRef = useRef<SVGSVGElement>(null)
  const edgesRef = useRef<SVGGElement>(null)
  const nodesRef = useRef<SVGGElement>(null)
  const prevLayoutRef = useRef<ReturnType<typeof computeD3Layout> | null>(null)

  // Track expanded content heights
  const [expandedHeights, setExpandedHeights] = useState<Map<string, number>>(new Map())

  // Compute layout with D3
  const layout = useMemo(() => {
    return computeD3Layout(renderLines, expandedIds, expandedHeights, maxLanes)
  }, [renderLines, expandedIds, expandedHeights, maxLanes])

  // Content X offset (after the SVG lanes)
  const contentX = useMemo(() => getContentX(maxLanes), [maxLanes])

  // Handle expanded content height measurement
  const handleExpandedHeightChange = useCallback((issueId: string, height: number) => {
    setExpandedHeights((prev) => {
      const next = new Map(prev)
      if (height === 0) {
        next.delete(issueId)
      } else {
        next.set(issueId, height)
      }
      return next
    })
  }, [])

  // Animate edges and nodes when layout changes
  useEffect(() => {
    if (!edgesRef.current || !nodesRef.current) return

    const prevLayout = prevLayoutRef.current
    const shouldAnimate = prevLayout !== null

    if (shouldAnimate) {
      // Animate to new layout
      animateEdges(edgesRef.current, layout.edges, TRANSITION_DURATION)
      animateNodes(nodesRef.current, layout.nodes, TRANSITION_DURATION)

      if (svgRef.current && prevLayout.totalHeight !== layout.totalHeight) {
        animateSvgHeight(svgRef.current, layout.totalHeight, TRANSITION_DURATION)
      }
    }

    prevLayoutRef.current = layout
  }, [layout])

  // Handle row click
  const handleRowClick = useCallback(
    (issueId: string) => {
      onSelectIssue?.(issueId)
    },
    [onSelectIssue]
  )

  return (
    <svg
      ref={svgRef}
      width="100%"
      height={layout.totalHeight}
      className={cn('task-graph-canvas', className)}
      data-testid="task-graph-canvas"
    >
      {/* Defs for reusable elements */}
      <defs>{/* Could add arrow markers, gradients, etc. here */}</defs>

      {/* Layer 1: Edges (background) */}
      <g ref={edgesRef} className="edges-layer">
        {layout.edges.map((edge) => (
          <path
            key={edge.id}
            className="edge"
            d={edge.path}
            stroke={edge.color}
            strokeWidth={2}
            fill="none"
          />
        ))}
      </g>

      {/* Layer 2: Node circles */}
      <g ref={nodesRef} className="nodes-layer">
        {layout.nodes.map((node) => {
          if (node.type !== 'issue') return null

          const line = node.line as TaskGraphIssueRenderLine
          const nodeColor = node.nodeColor ?? '#3b82f6'
          const isOutlineOnly = !line.hasDescription
          const agentStatusColor = line.agentStatus?.isActive
            ? getAgentStatusColor(line.agentStatus.status)
            : null

          return (
            <g key={`node-${line.issueId}`}>
              {/* Agent status ring */}
              {agentStatusColor && (
                <circle
                  cx={node.x}
                  cy={node.y}
                  r={NODE_RADIUS + 4}
                  fill="none"
                  stroke={agentStatusColor}
                  strokeWidth={2}
                  opacity={0.6}
                  className="animate-pulse"
                />
              )}

              {/* Node circle */}
              {isOutlineOnly ? (
                <circle
                  className="node"
                  cx={node.x}
                  cy={node.y}
                  r={NODE_RADIUS}
                  fill="none"
                  stroke={nodeColor}
                  strokeWidth={2}
                />
              ) : (
                <circle className="node" cx={node.x} cy={node.y} r={NODE_RADIUS} fill={nodeColor} />
              )}

              {/* Hidden parent indicator */}
              {line.hasHiddenParent && (
                <HiddenParentIndicator
                  cx={node.x}
                  cy={node.y}
                  nodeColor={nodeColor}
                  isSeriesMode={line.hiddenParentIsSeriesMode}
                />
              )}
            </g>
          )
        })}

        {/* PR nodes */}
        {layout.nodes.map((node) => {
          if (node.type !== 'pr') return null

          const line = node.line as TaskGraphPrRenderLine

          return (
            <circle
              key={`pr-node-${line.prNumber}`}
              cx={node.x}
              cy={node.y}
              r={NODE_RADIUS + 2}
              fill="#51A5C1"
              stroke="white"
              strokeWidth={2}
            />
          )
        })}

        {/* Load more button node */}
        {layout.nodes.map((node) => {
          if (node.type !== 'loadMore') return null

          return (
            <g key="load-more-node">
              <circle
                cx={node.x}
                cy={node.y}
                r={NODE_RADIUS + 2}
                fill="#51A5C1"
                stroke="white"
                strokeWidth={2}
              />
              <text
                x={node.x}
                y={node.y}
                textAnchor="middle"
                dominantBaseline="central"
                fill="white"
                fontSize={14}
                fontWeight="bold"
              >
                +
              </text>
            </g>
          )
        })}
      </g>

      {/* Layer 3: foreignObject content */}
      <g className="content-layer">
        {layout.nodes.map((node) => {
          if (node.type === 'issue') {
            const line = node.line as TaskGraphIssueRenderLine
            const isSelected = selectedIssueId === line.issueId
            const isExpanded = expandedIds.has(line.issueId)

            return (
              <foreignObject
                key={`content-${line.issueId}`}
                x={contentX}
                y={node.contentY}
                width={`calc(100% - ${contentX}px)`}
                height={node.rowHeight}
                overflow="visible"
              >
                <div>
                  <TaskGraphIssueRowContent
                    line={line}
                    projectId={projectId}
                    isSelected={isSelected}
                    isExpanded={isExpanded}
                    searchQuery={searchQuery}
                    onToggleExpand={() => onToggleExpand?.(line.issueId)}
                    onEdit={onEditIssue}
                    onRunAgent={onRunAgent}
                    onOpenSession={onOpenSession}
                    onTypeChange={onTypeChange}
                    onStatusChange={onStatusChange}
                    onExecutionModeChange={onExecutionModeChange}
                    isMoveSource={moveSourceIssueId === line.issueId}
                    isMoveOperationActive={isMoveOperationActive}
                    onClick={() => handleRowClick(line.issueId)}
                  />

                  {/* Expanded content */}
                  {isExpanded && (
                    <ExpandedContentMeasurer
                      line={line}
                      onHeightChange={(h) => handleExpandedHeightChange(line.issueId, h)}
                      onEdit={onEditIssue}
                      onRunAgent={onRunAgent}
                      onOpenSession={onOpenSession}
                      onClose={() => onToggleExpand?.(line.issueId)}
                    />
                  )}
                </div>
              </foreignObject>
            )
          }

          if (node.type === 'pr') {
            const line = node.line as TaskGraphPrRenderLine

            return (
              <foreignObject
                key={`content-pr-${line.prNumber}`}
                x={contentX}
                y={node.contentY}
                width={`calc(100% - ${contentX}px)`}
                height={ROW_HEIGHT}
              >
                <div>
                  <TaskGraphPrRowContent line={line} />
                </div>
              </foreignObject>
            )
          }

          if (node.type === 'separator') {
            return (
              <foreignObject
                key={`content-separator-${node.contentY}`}
                x={contentX}
                y={node.contentY}
                width={`calc(100% - ${contentX}px)`}
                height={ROW_HEIGHT / 2}
              >
                <div>
                  <TaskGraphSeparatorContent />
                </div>
              </foreignObject>
            )
          }

          if (node.type === 'loadMore') {
            return (
              <foreignObject
                key="content-load-more"
                x={contentX}
                y={node.contentY}
                width={`calc(100% - ${contentX}px)`}
                height={ROW_HEIGHT}
              >
                <div>
                  <TaskGraphLoadMoreContent onLoadMore={onLoadMorePrs} />
                </div>
              </foreignObject>
            )
          }

          return null
        })}
      </g>
    </svg>
  )
})

/**
 * Wrapper component that measures expanded content height.
 */
interface ExpandedContentMeasurerProps {
  line: TaskGraphIssueRenderLine
  onHeightChange: (height: number) => void
  onEdit?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  onOpenSession?: (sessionId: string) => void
  onClose?: () => void
}

function ExpandedContentMeasurer({
  line,
  onHeightChange,
  onEdit,
  onRunAgent,
  onOpenSession,
  onClose,
}: ExpandedContentMeasurerProps) {
  const contentRef = useRef<HTMLDivElement>(null)
  const lastHeightRef = useRef<number>(-1)

  // Measure content height
  useLayoutEffect(() => {
    if (!contentRef.current) return

    const reportHeight = (height: number) => {
      // Only report if height actually changed
      if (Math.abs(height - lastHeightRef.current) > 1) {
        lastHeightRef.current = height
        onHeightChange(height)
      }
    }

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        reportHeight(entry.contentRect.height)
      }
    })

    // Initial measurement
    reportHeight(contentRef.current.getBoundingClientRect().height)

    observer.observe(contentRef.current)
    return () => observer.disconnect()
  }, [onHeightChange])

  // Report zero height when unmounting
  useEffect(() => {
    return () => {
      lastHeightRef.current = 0
      onHeightChange(0)
    }
  }, [onHeightChange])

  return (
    <div ref={contentRef}>
      <TaskGraphIssueExpandedContent
        line={line}
        onEdit={onEdit}
        onRunAgent={onRunAgent}
        onOpenSession={onOpenSession}
        onClose={onClose}
      />
    </div>
  )
}

export default TaskGraphCanvas

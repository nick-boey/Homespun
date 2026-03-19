/**
 * D3 animation helpers for task graph transitions.
 *
 * Provides smooth animations for node positions, edge paths, and expansion/collapse.
 */

import { select, type Selection, type BaseType } from 'd3-selection'
import 'd3-transition'
import { interpolatePath } from 'd3-interpolate-path'
import { easeCubicInOut } from 'd3-ease'
import type { D3TaskGraphNode, D3TaskGraphEdge } from './task-graph-d3-layout'

/** Animation duration in milliseconds */
export const TRANSITION_DURATION = 300

/** Easing function for animations */
export const TRANSITION_EASE = easeCubicInOut

/**
 * Animates nodes to their new positions.
 */
export function animateNodes(
  container: SVGGElement,
  nodes: D3TaskGraphNode[],
  duration: number = TRANSITION_DURATION
): void {
  const issueNodes = nodes.filter((n) => n.type === 'issue' && n.issueId)

  const nodeSelection = select(container)
    .selectAll<SVGCircleElement, D3TaskGraphNode>('circle.node')
    .data(issueNodes, (d) => d?.issueId ?? '')

  // Exit: fade out
  nodeSelection.exit().transition().duration(duration).attr('opacity', 0).remove()

  // Enter: fade in at position
  const enter = nodeSelection
    .enter()
    .append('circle')
    .attr('class', 'node')
    .attr('cx', (d) => d.x)
    .attr('cy', (d) => d.y)
    .attr('r', 6)
    .attr('fill', (d) => d.nodeColor ?? '#3b82f6')
    .attr('opacity', 0)

  enter.transition().duration(duration).attr('opacity', 1)

  // Update: animate to new position
  nodeSelection
    .merge(enter)
    .transition()
    .duration(duration)
    .attr('cx', (d) => d.x)
    .attr('cy', (d) => d.y)
    .attr('fill', (d) => d.nodeColor ?? '#3b82f6')
}

/**
 * Animates edges with path morphing.
 */
export function animateEdges(
  container: SVGGElement,
  edges: D3TaskGraphEdge[],
  duration: number = TRANSITION_DURATION
): void {
  const edgeSelection = select(container)
    .selectAll<SVGPathElement, D3TaskGraphEdge>('path.edge')
    .data(edges, (d) => d?.id ?? '')

  // Exit: fade out
  edgeSelection.exit().transition().duration(duration).attr('opacity', 0).remove()

  // Enter: draw in (animate stroke-dashoffset)
  const enter = edgeSelection
    .enter()
    .append('path')
    .attr('class', 'edge')
    .attr('d', (d) => d.path)
    .attr('stroke', (d) => d.color)
    .attr('stroke-width', 2)
    .attr('fill', 'none')
    .attr('opacity', 0)

  enter.transition().duration(duration).attr('opacity', 1)

  // Update: morph path
  edgeSelection
    .merge(enter)
    .transition()
    .duration(duration)
    .attrTween('d', function (d) {
      const previous = this.getAttribute('d') ?? d.path
      return interpolatePath(previous, d.path)
    })
    .attr('stroke', (d) => d.color)
}

/**
 * Animates the SVG container height for expansion/collapse.
 */
export function animateSvgHeight(
  svg: SVGSVGElement,
  newHeight: number,
  duration: number = TRANSITION_DURATION
): void {
  select(svg).transition().duration(duration).attr('height', newHeight)
}

/**
 * Animates foreignObject positions for expansion/collapse.
 */
export function animateForeignObjects(
  container: SVGGElement,
  nodes: D3TaskGraphNode[],
  duration: number = TRANSITION_DURATION
): void {
  const selection = select(container)
    .selectAll<SVGForeignObjectElement, D3TaskGraphNode>('foreignObject')
    .data(nodes, (d) => d?.issueId ?? `${d?.type ?? 'unknown'}-${d?.contentY ?? 0}`)

  selection
    .transition()
    .duration(duration)
    .attr('y', (d) => d.contentY)
    .attr('height', (d) => d.rowHeight)
}

/**
 * Creates a staggered animation for multiple elements.
 * Each element starts slightly after the previous one.
 */
export function staggeredTransition<E extends BaseType, T>(
  selection: Selection<E, T, BaseType, unknown>,
  duration: number = TRANSITION_DURATION,
  staggerDelay: number = 30
) {
  return selection
    .transition()
    .duration(duration)
    .delay((_, i) => i * staggerDelay)
}

/**
 * Cancels all active transitions on an element.
 */
export function cancelTransitions(element: SVGElement): void {
  select(element).interrupt()
}

/**
 * Checks if an element has active transitions.
 */
export function hasActiveTransition(element: SVGElement): boolean {
  const selection = select(element)
  // Check if there's a transition by looking for the __transition__ property
  return !!(selection.node() as unknown as { __transition__?: unknown })?.__transition__
}

/**
 * Applies enter animation to new elements with a slide-in effect.
 */
export function applyEnterAnimation<E extends BaseType, T>(
  selection: Selection<E, T, BaseType, unknown>,
  direction: 'left' | 'right' | 'top' | 'bottom' = 'top',
  duration: number = TRANSITION_DURATION
): void {
  const offset = 20

  let initialTransform: string
  switch (direction) {
    case 'left':
      initialTransform = `translate(-${offset}, 0)`
      break
    case 'right':
      initialTransform = `translate(${offset}, 0)`
      break
    case 'top':
      initialTransform = `translate(0, -${offset})`
      break
    case 'bottom':
      initialTransform = `translate(0, ${offset})`
      break
  }

  selection
    .attr('transform', initialTransform)
    .attr('opacity', 0)
    .transition()
    .duration(duration)
    .attr('transform', 'translate(0, 0)')
    .attr('opacity', 1)
}

/**
 * Applies exit animation to elements being removed.
 */
export function applyExitAnimation<E extends BaseType, T>(
  selection: Selection<E, T, BaseType, unknown>,
  direction: 'left' | 'right' | 'top' | 'bottom' = 'top',
  duration: number = TRANSITION_DURATION
) {
  const offset = 20

  let finalTransform: string
  switch (direction) {
    case 'left':
      finalTransform = `translate(-${offset}, 0)`
      break
    case 'right':
      finalTransform = `translate(${offset}, 0)`
      break
    case 'top':
      finalTransform = `translate(0, -${offset})`
      break
    case 'bottom':
      finalTransform = `translate(0, ${offset})`
      break
  }

  return selection
    .transition()
    .duration(duration)
    .attr('transform', finalTransform)
    .attr('opacity', 0)
}

/**
 * Schedules a callback to run after all transitions complete.
 */
export function onTransitionsComplete(
  _container: SVGElement,
  callback: () => void,
  timeout: number = TRANSITION_DURATION + 50
): void {
  // Use a timeout as a simple way to wait for transitions
  // A more robust approach would track transition promises
  setTimeout(callback, timeout)
}

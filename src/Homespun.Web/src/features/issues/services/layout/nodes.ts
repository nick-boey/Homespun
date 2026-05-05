/**
 * Discriminated union of node kinds that flow through the layout engine.
 *
 * The engine itself only knows about `IGraphNode` (id + childSequencing). This
 * module layers a `kind`-tagged union on top so consumers can switch on the
 * node type after layout — issues vs phases vs (future) anything else — without
 * string-id parsing.
 *
 * Adding a new node kind: declare a new `…LayoutNode` interface, add it to the
 * `LayoutNode` union, and add a guard. The engine and `IssueLayoutService`'s
 * generic plumbing don't care what the kind is; only the `task-graph-layout.ts`
 * consumer needs to grow a new arm.
 */

import type { IGraphNode } from './types'
import type { LayoutIssue } from './issue-layout-service'

export interface IssueLayoutNode extends IGraphNode {
  readonly kind: 'issue'
  readonly issue: LayoutIssue
}

export interface LayoutPhaseTask {
  readonly description: string | null
  readonly done: boolean
}

export interface LayoutPhase {
  readonly name: string
  readonly done: number
  readonly total: number
  readonly tasks: readonly LayoutPhaseTask[]
}

export interface PhaseLayoutNode extends IGraphNode {
  readonly kind: 'phase'
  readonly parentIssueId: string
  readonly phase: LayoutPhase
}

export type LayoutNode = IssueLayoutNode | PhaseLayoutNode

export function isIssueNode(node: LayoutNode): node is IssueLayoutNode {
  return node.kind === 'issue'
}

export function isPhaseNode(node: LayoutNode): node is PhaseLayoutNode {
  return node.kind === 'phase'
}

/** Stable id for a phase node — `${issueId}::phase::${phaseName}`. */
export function phaseNodeId(parentIssueId: string, phaseName: string): string {
  return `${parentIssueId}::phase::${phaseName}`
}

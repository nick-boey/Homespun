/**
 * Node kinds that flow through the layout engine.
 *
 * `issue` is the normal case. `pending-issue` is a synthetic node injected
 * into the layout engine when the user is creating a new issue inline, so the
 * engine assigns it a real row/lane/edge position rather than overlaying it at
 * a fixed DOM position.
 */

import type { IGraphNode } from './types'
import type { LayoutIssue, ParentIssueRef } from './issue-layout-service'

export const PENDING_ISSUE_ID = '__pending-issue__'

export interface IssueLayoutNode extends IGraphNode {
  readonly kind: 'issue'
  readonly issue: LayoutIssue
}

export interface PendingIssueLayoutNode extends IGraphNode {
  readonly kind: 'pending-issue'
  readonly pendingTitle: string
  readonly parentIssues?: readonly ParentIssueRef[]
}

export type LayoutNode = IssueLayoutNode | PendingIssueLayoutNode

export function isIssueNode(node: LayoutNode): node is IssueLayoutNode {
  return node.kind === 'issue'
}

export function isPendingIssueNode(node: LayoutNode): node is PendingIssueLayoutNode {
  return node.kind === 'pending-issue'
}

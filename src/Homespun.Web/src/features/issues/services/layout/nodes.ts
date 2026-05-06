/**
 * Node kinds that flow through the layout engine.
 *
 * Currently only `issue` exists. The discriminated-union shape is kept so a
 * future kind can be layered on without rewriting consumers.
 */

import type { IGraphNode } from './types'
import type { LayoutIssue } from './issue-layout-service'

export interface IssueLayoutNode extends IGraphNode {
  readonly kind: 'issue'
  readonly issue: LayoutIssue
}

export type LayoutNode = IssueLayoutNode

export function isIssueNode(node: LayoutNode): node is IssueLayoutNode {
  return node.kind === 'issue'
}

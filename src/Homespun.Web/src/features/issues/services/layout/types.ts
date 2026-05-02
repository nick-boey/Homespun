/**
 * Type-level mirror of Fleece.Core 3.0.0's GraphLayout API surface
 * (`Fleece.Core.Models.Graph.*`). Keeping the names and shapes identical to the
 * C# reference makes the golden-fixture diff trivial.
 *
 * Drift detection: `tests/Homespun.Web.LayoutFixtures` emits
 * `*.expected.json` from the live C# engine; `golden-fixtures.test.ts`
 * structurally compares the TS port output. A change in the upstream
 * algorithm or in this file's shapes will surface as a fixture diff.
 */

export type LayoutMode = 'issueGraph' | 'normalTree'

export type ChildSequencing = 'series' | 'parallel'

export type EdgeKind = 'seriesSibling' | 'seriesCornerToParent' | 'parallelChildToSpine'

export type EdgeAttachSide = 'top' | 'bottom' | 'left' | 'right'

export type InactiveVisibility = 'hide' | 'ifHasActiveDescendants' | 'always'

export interface IGraphNode {
  readonly id: string
  readonly childSequencing: ChildSequencing
}

export interface PositionedNode<T> {
  readonly node: T
  readonly row: number
  readonly lane: number
  readonly appearanceIndex: number
  readonly totalAppearances: number
}

export interface Edge<T> {
  readonly id: string
  readonly from: T
  readonly to: T
  readonly kind: EdgeKind
  readonly startRow: number
  readonly startLane: number
  readonly endRow: number
  readonly endLane: number
  readonly pivotLane: number | null
  readonly sourceAttach: EdgeAttachSide
  readonly targetAttach: EdgeAttachSide
}

export interface GraphLayout<T> {
  readonly nodes: readonly PositionedNode<T>[]
  readonly edges: readonly Edge<T>[]
  readonly totalRows: number
  readonly totalLanes: number
}

export type GraphLayoutResult<T> =
  | { readonly ok: true; readonly layout: GraphLayout<T> }
  | { readonly ok: false; readonly cycle: readonly string[] }

export interface GraphLayoutRequest<T extends IGraphNode> {
  readonly allNodes: readonly T[]
  readonly rootFinder: (allNodes: readonly T[]) => Iterable<T>
  readonly childIterator: (parent: T) => Iterable<T>
  readonly mode: LayoutMode
}

export type GraphSortDirection = 'ascending' | 'descending'

export type GraphSortCriteria = 'createdAt' | 'priority' | 'hasDescription' | 'title'

export interface GraphSortRule {
  readonly criteria: GraphSortCriteria
  readonly direction?: GraphSortDirection
}

export interface GraphSortConfig {
  readonly rules: readonly GraphSortRule[]
}

export const DefaultGraphSortConfig: GraphSortConfig = {
  rules: [{ criteria: 'createdAt', direction: 'ascending' }],
}

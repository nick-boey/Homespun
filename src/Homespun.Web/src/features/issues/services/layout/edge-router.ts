/**
 * Edge occupancy walk — port of Fleece.Core 3.0.0's
 * `GraphLayoutService.WalkEdge` and `BuildOccupancy`. Given the laid-out
 * nodes and the directed edges between them, this fills a (rows × lanes)
 * grid with `OccupancyCell` entries describing which edges traverse which
 * cells. The renderer can use this to nudge edges that overlap, but the
 * occupancy data isn't required for the basic edge geometry — it's a
 * pure derived view on the layout result.
 */

import type { Edge, IGraphNode, PositionedNode } from './types'

export type EdgeSegmentKind =
  | 'vertical'
  | 'horizontal'
  | 'cornerNE'
  | 'cornerNW'
  | 'cornerSE'
  | 'cornerSW'
  | 'junctionTEast'
  | 'junctionTWest'
  | 'junctionTNorth'
  | 'junctionTSouth'

export interface EdgeOccupancy {
  readonly edgeId: string
  readonly segment: EdgeSegmentKind
}

export interface OccupancyCell<T> {
  readonly node: PositionedNode<T> | null
  readonly edges: readonly EdgeOccupancy[]
}

type EdgeWalkVisitor = (row: number, lane: number, edgeId: string, kind: EdgeSegmentKind) => void

/**
 * Replays an edge across the (row, lane) grid in the same order as
 * Fleece.Core's `WalkEdge`. The shape of the walk depends on the source
 * attach side: top/bottom-attached edges run vertically first then
 * horizontally; left/right-attached edges run horizontally first then
 * vertically.
 */
export function walkEdge<T extends IGraphNode>(edge: Edge<T>, visit: EdgeWalkVisitor): void {
  const { startRow, startLane, endRow, endLane, sourceAttach, id } = edge

  if (sourceAttach === 'top' || sourceAttach === 'bottom') {
    visit(startRow, startLane, id, 'vertical')
    for (let r = startRow + 1; r < endRow; r++) {
      visit(r, startLane, id, 'vertical')
    }

    if (endLane === startLane) {
      if (endRow !== startRow) {
        visit(endRow, endLane, id, 'vertical')
      }
      return
    }

    // Bend at endRow, then horizontal run to endLane.
    visit(endRow, startLane, id, 'horizontal')
    const step = endLane > startLane ? 1 : -1
    for (let l = startLane + step; l !== endLane; l += step) {
      visit(endRow, l, id, 'horizontal')
    }
    visit(endRow, endLane, id, 'horizontal')
    return
  }

  // sourceAttach is 'left' or 'right' — horizontal-then-vertical.
  if (startLane === endLane) {
    visit(startRow, startLane, id, 'vertical')
  } else {
    visit(startRow, startLane, id, 'horizontal')
    const step = endLane > startLane ? 1 : -1
    for (let l = startLane + step; l !== endLane; l += step) {
      visit(startRow, l, id, 'horizontal')
    }
    visit(startRow, endLane, id, 'vertical')
  }
  for (let r = startRow + 1; r < endRow; r++) {
    visit(r, endLane, id, 'vertical')
  }
  if (endRow !== startRow) {
    visit(endRow, endLane, id, 'vertical')
  }
}

/**
 * Builds the (rows × lanes) occupancy grid from the laid-out nodes and
 * edges. Each cell is `{ node, edges[] }` — `node` is non-null for cells
 * containing a positioned node; `edges[]` is the (possibly empty) list of
 * edge segments crossing that cell.
 */
export function buildOccupancy<T extends IGraphNode>(
  nodes: readonly PositionedNode<T>[],
  edges: readonly Edge<T>[],
  totalRows: number,
  totalLanes: number
): OccupancyCell<T>[][] {
  const grid: (PositionedNode<T> | null)[][] = []
  const edgesAt: (EdgeOccupancy[] | null)[][] = []
  for (let r = 0; r < totalRows; r++) {
    const nodeRow = new Array<PositionedNode<T> | null>(totalLanes).fill(null)
    const edgeRow = new Array<EdgeOccupancy[] | null>(totalLanes).fill(null)
    grid.push(nodeRow)
    edgesAt.push(edgeRow)
  }

  for (const node of nodes) {
    grid[node.row][node.lane] = node
  }

  for (const edge of edges) {
    walkEdge(edge, (r, l, edgeId, segment) => {
      if (r < 0 || r >= totalRows || l < 0 || l >= totalLanes) return
      let bucket = edgesAt[r][l]
      if (bucket === null) {
        bucket = []
        edgesAt[r][l] = bucket
      }
      bucket.push({ edgeId, segment })
    })
  }

  const out: OccupancyCell<T>[][] = []
  for (let r = 0; r < totalRows; r++) {
    const row: OccupancyCell<T>[] = []
    for (let l = 0; l < totalLanes; l++) {
      row.push({ node: grid[r][l], edges: edgesAt[r][l] ?? [] })
    }
    out.push(row)
  }
  return out
}

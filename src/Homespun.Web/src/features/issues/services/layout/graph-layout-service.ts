/**
 * TS port of `Fleece.Core.Services.GraphLayout.GraphLayoutService` (v3.0.0).
 *
 * The algorithm walks the graph in DFS order, emitting one row per node.
 * In `issueGraph` mode, the parent is emitted *after* its children so the
 * tree reads bottom-up (leaves at row 0, root at the bottom). Lane
 * assignment depends on the parent's child-sequencing:
 *
 * - `series` parents stack siblings in increasing lane order, with the
 *   parent landing at `maxChildLane + 1`.
 * - `parallel` parents share a single lane with each child, with the parent
 *   landing one lane higher than the deepest child.
 *
 * In `normalTree` mode, the parent is emitted *before* its children
 * (top-down). Each child's lane is `parentLane + 1`.
 *
 * Cycles in the parent chain produce a `{ ok: false, cycle }` result; the
 * cycle list is the offending node IDs from cycle root to the repeat node.
 *
 * Re-emission semantics: when the same node is reached via a second
 * ancestor chain (multi-parent diamond), the second visit emits a fresh
 * row at the requested start lane but does NOT recurse — the children are
 * already in place from the first emission.
 */

import type {
  Edge,
  GraphLayout,
  GraphLayoutRequest,
  GraphLayoutResult,
  IGraphNode,
  PositionedNode,
} from './types'

interface PositionedEntry<T extends IGraphNode> {
  node: T
  row: number
  lane: number
}

interface LayoutContext<T extends IGraphNode> {
  request: GraphLayoutRequest<T>
  emitted: PositionedEntry<T>[]
  edges: Edge<T>[]
  pathStack: string[]
  appearanceCounts: Map<string, number>
  detectedCycle: string[] | null
}

const idEq = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()

function pathContains(stack: readonly string[], id: string): boolean {
  for (const item of stack) {
    if (idEq(item, id)) return true
  }
  return false
}

function extractCycle(stack: readonly string[], id: string): string[] {
  // The C# Stack<string> iterates top-down (most recent first). Reversing
  // gives root-first order. The cycle is from the first repeated id to the
  // current id, with the current id appended.
  const rootFirst = [...stack]
  const ix = rootFirst.findIndex((s) => idEq(s, id))
  if (ix < 0) return [id, id]
  return [...rootFirst.slice(ix), id]
}

function emit<T extends IGraphNode>(
  node: T,
  lane: number,
  ctx: LayoutContext<T>
): PositionedEntry<T> {
  const entry: PositionedEntry<T> = {
    node,
    row: ctx.emitted.length,
    lane,
  }
  ctx.emitted.push(entry)
  const lc = node.id.toLowerCase()
  ctx.appearanceCounts.set(lc, (ctx.appearanceCounts.get(lc) ?? 0) + 1)
  return entry
}

function isLeafEmission<T extends IGraphNode>(child: T, ctx: LayoutContext<T>): boolean {
  if (ctx.appearanceCounts.has(child.id.toLowerCase())) return true
  if (pathContains(ctx.pathStack, child.id)) return false
  for (const _ of ctx.request.childIterator(child)) {
    return false
  }
  return true
}

function emitIssueGraphEdges<T extends IGraphNode>(
  parent: T,
  sequencing: 'series' | 'parallel',
  subtreeRoots: readonly PositionedEntry<T>[],
  parentEntry: PositionedEntry<T>,
  ctx: LayoutContext<T>
): void {
  if (subtreeRoots.length === 0) return

  if (sequencing === 'series') {
    for (let i = 1; i < subtreeRoots.length; i++) {
      const a = subtreeRoots[i - 1]
      const b = subtreeRoots[i]
      ctx.edges.push({
        id: `${a.node.id}->${b.node.id}:seriesSibling#${ctx.edges.length}`,
        from: a.node,
        to: b.node,
        kind: 'seriesSibling',
        startRow: a.row,
        startLane: a.lane,
        endRow: b.row,
        endLane: b.lane,
        pivotLane: null,
        sourceAttach: 'bottom',
        targetAttach: 'top',
      })
    }
    const last = subtreeRoots[subtreeRoots.length - 1]
    ctx.edges.push({
      id: `${last.node.id}->${parent.id}:seriesCornerToParent#${ctx.edges.length}`,
      from: last.node,
      to: parent,
      kind: 'seriesCornerToParent',
      startRow: last.row,
      startLane: last.lane,
      endRow: parentEntry.row,
      endLane: parentEntry.lane,
      pivotLane: last.lane,
      sourceAttach: 'bottom',
      targetAttach: 'left',
    })
    return
  }

  for (const child of subtreeRoots) {
    ctx.edges.push({
      id: `${child.node.id}->${parent.id}:parallelChildToSpine#${ctx.edges.length}`,
      from: child.node,
      to: parent,
      kind: 'parallelChildToSpine',
      startRow: child.row,
      startLane: child.lane,
      endRow: parentEntry.row,
      endLane: parentEntry.lane,
      pivotLane: parentEntry.lane,
      sourceAttach: 'right',
      targetAttach: 'top',
    })
  }
}

function emitNormalTreeEdges<T extends IGraphNode>(
  parent: T,
  parentEntry: PositionedEntry<T>,
  childEntries: readonly PositionedEntry<T>[],
  sequencing: 'series' | 'parallel',
  ctx: LayoutContext<T>
): void {
  if (childEntries.length === 0) return

  if (sequencing === 'series') {
    const first = childEntries[0]
    ctx.edges.push({
      id: `${parent.id}->${first.node.id}:seriesCornerToParent#${ctx.edges.length}`,
      from: parent,
      to: first.node,
      kind: 'seriesCornerToParent',
      startRow: parentEntry.row,
      startLane: parentEntry.lane,
      endRow: first.row,
      endLane: first.lane,
      pivotLane: parentEntry.lane,
      sourceAttach: 'bottom',
      targetAttach: 'left',
    })
    for (let i = 1; i < childEntries.length; i++) {
      const a = childEntries[i - 1]
      const b = childEntries[i]
      ctx.edges.push({
        id: `${a.node.id}->${b.node.id}:seriesSibling#${ctx.edges.length}`,
        from: a.node,
        to: b.node,
        kind: 'seriesSibling',
        startRow: a.row,
        startLane: a.lane,
        endRow: b.row,
        endLane: b.lane,
        pivotLane: null,
        sourceAttach: 'bottom',
        targetAttach: 'top',
      })
    }
    return
  }

  for (const child of childEntries) {
    ctx.edges.push({
      id: `${parent.id}->${child.node.id}:parallelChildToSpine#${ctx.edges.length}`,
      from: parent,
      to: child.node,
      kind: 'parallelChildToSpine',
      startRow: parentEntry.row,
      startLane: parentEntry.lane,
      endRow: child.row,
      endLane: child.lane,
      pivotLane: parentEntry.lane,
      sourceAttach: 'bottom',
      targetAttach: 'left',
    })
  }
}

function layoutSubtreeIssueGraph<T extends IGraphNode>(
  node: T,
  startLane: number,
  ctx: LayoutContext<T>
): number {
  if (pathContains(ctx.pathStack, node.id)) {
    ctx.detectedCycle = extractCycle(ctx.pathStack, node.id)
    return -1
  }
  if (ctx.appearanceCounts.has(node.id.toLowerCase())) {
    emit(node, startLane, ctx)
    return startLane
  }

  ctx.pathStack.push(node.id)
  const children: T[] = []
  for (const c of ctx.request.childIterator(node)) children.push(c)

  if (children.length === 0) {
    emit(node, startLane, ctx)
    ctx.pathStack.pop()
    return startLane
  }

  const subtreeRoots: PositionedEntry<T>[] = []
  let resultLane: number

  if (node.childSequencing === 'series') {
    let lane = startLane
    let firstChild = true
    for (const child of children) {
      const leaf = isLeafEmission(child, ctx)
      const childStartLane = leaf || firstChild ? lane : lane + 1
      const childMaxLane = layoutSubtreeIssueGraph(child, childStartLane, ctx)
      if (ctx.detectedCycle !== null) {
        ctx.pathStack.pop()
        return -1
      }
      if (!leaf) lane = childMaxLane
      subtreeRoots.push(ctx.emitted[ctx.emitted.length - 1])
      firstChild = false
    }
    resultLane = lane
  } else {
    let maxChildLane = startLane
    for (const child of children) {
      const childMaxLane = layoutSubtreeIssueGraph(child, startLane, ctx)
      if (ctx.detectedCycle !== null) {
        ctx.pathStack.pop()
        return -1
      }
      if (childMaxLane > maxChildLane) maxChildLane = childMaxLane
      subtreeRoots.push(ctx.emitted[ctx.emitted.length - 1])
    }
    resultLane = maxChildLane
  }

  const parentLane = resultLane + 1
  const parentEntry = emit(node, parentLane, ctx)
  emitIssueGraphEdges(node, node.childSequencing, subtreeRoots, parentEntry, ctx)
  ctx.pathStack.pop()
  return parentLane
}

function layoutSubtreeNormalTree<T extends IGraphNode>(
  node: T,
  lane: number,
  ctx: LayoutContext<T>,
  siblingEntries: PositionedEntry<T>[] | null
): void {
  if (pathContains(ctx.pathStack, node.id)) {
    ctx.detectedCycle = extractCycle(ctx.pathStack, node.id)
    return
  }
  const alreadySeen = ctx.appearanceCounts.has(node.id.toLowerCase())
  const entry = emit(node, lane, ctx)
  siblingEntries?.push(entry)
  if (alreadySeen) return

  ctx.pathStack.push(node.id)
  const children: T[] = []
  for (const c of ctx.request.childIterator(node)) children.push(c)
  if (children.length === 0) {
    ctx.pathStack.pop()
    return
  }
  const childEntries: PositionedEntry<T>[] = []
  for (const child of children) {
    layoutSubtreeNormalTree(child, lane + 1, ctx, childEntries)
    if (ctx.detectedCycle !== null) {
      ctx.pathStack.pop()
      return
    }
  }
  emitNormalTreeEdges(node, entry, childEntries, node.childSequencing, ctx)
  ctx.pathStack.pop()
}

function assignAppearanceCounts<T extends IGraphNode>(
  emitted: readonly PositionedEntry<T>[]
): PositionedNode<T>[] {
  const totals = new Map<string, number>()
  for (const e of emitted) {
    const k = e.node.id.toLowerCase()
    totals.set(k, (totals.get(k) ?? 0) + 1)
  }
  const seen = new Map<string, number>()
  const out: PositionedNode<T>[] = []
  for (const e of emitted) {
    const k = e.node.id.toLowerCase()
    const next = (seen.get(k) ?? 0) + 1
    seen.set(k, next)
    out.push({
      node: e.node,
      row: e.row,
      lane: e.lane,
      appearanceIndex: next,
      totalAppearances: totals.get(k) ?? 1,
    })
  }
  return out
}

function emptyLayout<T>(): GraphLayout<T> {
  return { nodes: [], edges: [], totalRows: 0, totalLanes: 0 }
}

export class GraphLayoutService {
  layout<T extends IGraphNode>(request: GraphLayoutRequest<T>): GraphLayoutResult<T> {
    if (request.mode !== 'issueGraph' && request.mode !== 'normalTree') {
      throw new Error(`Unsupported LayoutMode: ${String(request.mode)}`)
    }

    const ctx: LayoutContext<T> = {
      request,
      emitted: [],
      edges: [],
      pathStack: [],
      appearanceCounts: new Map(),
      detectedCycle: null,
    }

    const roots: T[] = []
    for (const r of request.rootFinder(request.allNodes)) roots.push(r)
    if (request.allNodes.length === 0 || roots.length === 0) {
      return { ok: true, layout: emptyLayout<T>() }
    }

    let maxLane = 0
    if (request.mode === 'issueGraph') {
      for (const root of roots) {
        const lane = layoutSubtreeIssueGraph(root, 0, ctx)
        if (ctx.detectedCycle !== null) {
          return { ok: false, cycle: ctx.detectedCycle }
        }
        if (lane > maxLane) maxLane = lane
      }
    } else {
      for (const root of roots) {
        layoutSubtreeNormalTree(root, 0, ctx, null)
        if (ctx.detectedCycle !== null) {
          return { ok: false, cycle: ctx.detectedCycle }
        }
      }
      for (const e of ctx.emitted) {
        if (e.lane > maxLane) maxLane = e.lane
      }
    }

    if (ctx.emitted.length === 0) {
      return { ok: true, layout: emptyLayout<T>() }
    }

    const nodes = assignAppearanceCounts(ctx.emitted)
    return {
      ok: true,
      layout: {
        nodes,
        edges: ctx.edges,
        totalRows: ctx.emitted.length,
        totalLanes: maxLane + 1,
      },
    }
  }
}

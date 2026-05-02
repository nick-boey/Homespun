import { describe, it, expect } from 'vitest'
import { GraphLayoutService } from './graph-layout-service'
import type { ChildSequencing, GraphLayoutRequest, IGraphNode, LayoutMode } from './types'

interface TestNode extends IGraphNode {
  readonly id: string
  readonly childSequencing: ChildSequencing
  readonly children: readonly string[]
}

const node = (id: string, childSequencing: ChildSequencing, ...children: string[]): TestNode => ({
  id,
  childSequencing,
  children,
})

const makeRequest = (
  nodes: readonly TestNode[],
  rootIds: readonly string[],
  mode: LayoutMode = 'issueGraph'
): GraphLayoutRequest<TestNode> => {
  const lookup = new Map<string, TestNode>()
  for (const n of nodes) lookup.set(n.id, n)
  return {
    allNodes: nodes,
    rootFinder: () => rootIds.map((id) => lookup.get(id)!),
    childIterator: (n) => n.children.map((id) => lookup.get(id)!),
    mode,
  }
}

const service = new GraphLayoutService()

describe('GraphLayoutService.layout (issueGraph)', () => {
  it('returns empty layout for empty input', () => {
    const result = service.layout(makeRequest([], []))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.layout.nodes).toEqual([])
    expect(result.layout.edges).toEqual([])
    expect(result.layout.totalRows).toBe(0)
    expect(result.layout.totalLanes).toBe(0)
  })

  it('emits a single-node graph at row=0 lane=0', () => {
    const a = node('A', 'series')
    const result = service.layout(makeRequest([a], ['A']))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.layout.nodes).toHaveLength(1)
    expect(result.layout.nodes[0]).toMatchObject({ row: 0, lane: 0 })
    expect(result.layout.edges).toEqual([])
    expect(result.layout.totalRows).toBe(1)
    expect(result.layout.totalLanes).toBe(1)
  })

  it('series chain: leaves at low lanes, root at highest lane', () => {
    // R-(series)->A-(series)->B-(leaf)
    const r = node('R', 'series', 'A')
    const a = node('A', 'series', 'B')
    const b = node('B', 'series')
    const result = service.layout(makeRequest([r, a, b], ['R']))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const byId = new Map(result.layout.nodes.map((n) => [n.node.id, n] as const))
    expect(byId.get('B')!.lane).toBe(0)
    expect(byId.get('A')!.lane).toBe(1)
    expect(byId.get('R')!.lane).toBe(2)
    // bottom-up emission order
    expect(result.layout.nodes.map((n) => n.node.id)).toEqual(['B', 'A', 'R'])
  })

  it('parallel children share a lane with their parent', () => {
    // P-(parallel)->C1, C2, C3
    const p = node('P', 'parallel', 'C1', 'C2', 'C3')
    const c1 = node('C1', 'series')
    const c2 = node('C2', 'series')
    const c3 = node('C3', 'series')
    const result = service.layout(makeRequest([p, c1, c2, c3], ['P']))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const byId = new Map(result.layout.nodes.map((n) => [n.node.id, n] as const))
    // All children at lane 0, parent at lane 1.
    expect(byId.get('C1')!.lane).toBe(0)
    expect(byId.get('C2')!.lane).toBe(0)
    expect(byId.get('C3')!.lane).toBe(0)
    expect(byId.get('P')!.lane).toBe(1)
    // Edges: 3 parallelChildToSpine.
    expect(result.layout.edges).toHaveLength(3)
    for (const e of result.layout.edges) {
      expect(e.kind).toBe('parallelChildToSpine')
      expect(e.sourceAttach).toBe('right')
      expect(e.targetAttach).toBe('top')
    }
  })

  it('series children stack into increasing lanes; emits sibling + corner edges', () => {
    // P-(series)->A, B, C  where A, B, C are leaves.
    const p = node('P', 'series', 'A', 'B', 'C')
    const result = service.layout(
      makeRequest([p, node('A', 'series'), node('B', 'series'), node('C', 'series')], ['P'])
    )
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const byId = new Map(result.layout.nodes.map((n) => [n.node.id, n] as const))
    // Leaf siblings under a series parent stay at the same lane (leaf optimization),
    // and the parent lands one lane above.
    expect(byId.get('A')!.lane).toBe(0)
    expect(byId.get('B')!.lane).toBe(0)
    expect(byId.get('C')!.lane).toBe(0)
    expect(byId.get('P')!.lane).toBe(1)
    // Edges: A→B, B→C (sibling), C→P (corner).
    expect(result.layout.edges).toHaveLength(3)
    expect(result.layout.edges[0].kind).toBe('seriesSibling')
    expect(result.layout.edges[1].kind).toBe('seriesSibling')
    expect(result.layout.edges[2].kind).toBe('seriesCornerToParent')
    expect(result.layout.edges[2].pivotLane).toBe(0)
  })

  it('detects cycle in parent chain and returns ok=false', () => {
    // A → B → A
    const a = node('A', 'series', 'B')
    const b = node('B', 'series', 'A')
    const result = service.layout(makeRequest([a, b], ['A']))
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.cycle).toContain('A')
  })

  it('multi-parent (diamond): node is emitted twice, totalAppearances=2', () => {
    // R(parallel) → L, M;  L → X;  M → X (diamond)
    const r = node('R', 'parallel', 'L', 'M')
    const l = node('L', 'series', 'X')
    const m = node('M', 'series', 'X')
    const x = node('X', 'series')
    const result = service.layout(makeRequest([r, l, m, x], ['R']))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const xs = result.layout.nodes.filter((n) => n.node.id === 'X')
    expect(xs).toHaveLength(2)
    for (const x of xs) expect(x.totalAppearances).toBe(2)
    expect(xs.map((x) => x.appearanceIndex).sort()).toEqual([1, 2])
  })

  it('respects normalTree mode: parent emitted before children, children at lane+1', () => {
    const r = node('R', 'parallel', 'A', 'B')
    const a = node('A', 'series')
    const b = node('B', 'series')
    const result = service.layout(makeRequest([r, a, b], ['R'], 'normalTree'))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.layout.nodes.map((n) => n.node.id)).toEqual(['R', 'A', 'B'])
    const byId = new Map(result.layout.nodes.map((n) => [n.node.id, n] as const))
    expect(byId.get('R')!.lane).toBe(0)
    expect(byId.get('A')!.lane).toBe(1)
    expect(byId.get('B')!.lane).toBe(1)
  })

  it('numbers each node row by emission order', () => {
    const r = node('R', 'parallel', 'A', 'B')
    const a = node('A', 'series')
    const b = node('B', 'series')
    const result = service.layout(makeRequest([r, a, b], ['R']))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.layout.nodes.map((n) => n.row)).toEqual([0, 1, 2])
    expect(result.layout.totalRows).toBe(3)
  })

  it('totalLanes is max lane + 1', () => {
    // R-(series)->A-(series)->B (3 nodes deep series → root at lane 2)
    const r = node('R', 'series', 'A')
    const a = node('A', 'series', 'B')
    const b = node('B', 'series')
    const result = service.layout(makeRequest([r, a, b], ['R']))
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.layout.totalLanes).toBe(3)
  })

  it('rejects unsupported layout modes', () => {
    expect(() =>
      service.layout({
        ...makeRequest([], []),
        mode: 'bogus' as never,
      })
    ).toThrow(/Unsupported LayoutMode/)
  })
})

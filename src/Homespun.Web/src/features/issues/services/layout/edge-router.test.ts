import { describe, it, expect } from 'vitest'
import { buildOccupancy, walkEdge } from './edge-router'
import type { Edge, IGraphNode, PositionedNode } from './types'

interface N extends IGraphNode {
  id: string
  childSequencing: 'series' | 'parallel'
}

const mk = (id: string): N => ({ id, childSequencing: 'series' })

describe('walkEdge', () => {
  it('emits a single vertical cell when source/end share a row & lane', () => {
    const cells: { r: number; l: number; kind: string }[] = []
    const edge: Edge<N> = {
      id: 'e',
      from: mk('A'),
      to: mk('B'),
      kind: 'seriesSibling',
      startRow: 2,
      startLane: 1,
      endRow: 2,
      endLane: 1,
      pivotLane: null,
      sourceAttach: 'bottom',
      targetAttach: 'top',
    }
    walkEdge(edge, (r, l, _id, kind) => cells.push({ r, l, kind }))
    expect(cells).toEqual([{ r: 2, l: 1, kind: 'vertical' }])
  })

  it('top/bottom-attached: vertical run then horizontal turn at endRow', () => {
    const cells: { r: number; l: number; kind: string }[] = []
    const edge: Edge<N> = {
      id: 'e',
      from: mk('A'),
      to: mk('B'),
      kind: 'seriesCornerToParent',
      startRow: 0,
      startLane: 0,
      endRow: 2,
      endLane: 3,
      pivotLane: 0,
      sourceAttach: 'bottom',
      targetAttach: 'left',
    }
    walkEdge(edge, (r, l, _id, kind) => cells.push({ r, l, kind }))
    // Vertical: (0,0), (1,0). Then horizontal at row 2: (2,0)H, (2,1)H, (2,2)H, (2,3)H.
    expect(cells).toEqual([
      { r: 0, l: 0, kind: 'vertical' },
      { r: 1, l: 0, kind: 'vertical' },
      { r: 2, l: 0, kind: 'horizontal' },
      { r: 2, l: 1, kind: 'horizontal' },
      { r: 2, l: 2, kind: 'horizontal' },
      { r: 2, l: 3, kind: 'horizontal' },
    ])
  })

  it('left/right-attached: horizontal run then vertical descent', () => {
    const cells: { r: number; l: number; kind: string }[] = []
    const edge: Edge<N> = {
      id: 'e',
      from: mk('A'),
      to: mk('B'),
      kind: 'parallelChildToSpine',
      startRow: 0,
      startLane: 0,
      endRow: 3,
      endLane: 2,
      pivotLane: 2,
      sourceAttach: 'right',
      targetAttach: 'top',
    }
    walkEdge(edge, (r, l, _id, kind) => cells.push({ r, l, kind }))
    // Horizontal: (0,0)H, (0,1)H, then vertical at lane 2: (0,2)V, (1,2)V, (2,2)V, (3,2)V.
    expect(cells).toEqual([
      { r: 0, l: 0, kind: 'horizontal' },
      { r: 0, l: 1, kind: 'horizontal' },
      { r: 0, l: 2, kind: 'vertical' },
      { r: 1, l: 2, kind: 'vertical' },
      { r: 2, l: 2, kind: 'vertical' },
      { r: 3, l: 2, kind: 'vertical' },
    ])
  })
})

describe('buildOccupancy', () => {
  it('places nodes in their (row, lane) cells with edge segments alongside', () => {
    const a: PositionedNode<N> = {
      node: mk('A'),
      row: 0,
      lane: 0,
      appearanceIndex: 1,
      totalAppearances: 1,
    }
    const b: PositionedNode<N> = {
      node: mk('B'),
      row: 1,
      lane: 0,
      appearanceIndex: 1,
      totalAppearances: 1,
    }
    const grid = buildOccupancy(
      [a, b],
      [
        {
          id: 'e',
          from: mk('A'),
          to: mk('B'),
          kind: 'seriesSibling',
          startRow: 0,
          startLane: 0,
          endRow: 1,
          endLane: 0,
          pivotLane: null,
          sourceAttach: 'bottom',
          targetAttach: 'top',
        },
      ],
      2,
      1
    )
    expect(grid).toHaveLength(2)
    expect(grid[0][0].node?.node.id).toBe('A')
    expect(grid[1][0].node?.node.id).toBe('B')
    // Edge cells: (0,0) vertical and (1,0) vertical.
    expect(grid[0][0].edges.map((e) => e.segment)).toEqual(['vertical'])
    expect(grid[1][0].edges.map((e) => e.segment)).toEqual(['vertical'])
  })
})

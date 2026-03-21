import { describe, it, expect } from 'vitest'
import { computeD3Layout, recalculateLayoutForExpansion, getContentX } from './task-graph-d3-layout'
import type { TaskGraphIssueRenderLine, TaskGraphPrRenderLine } from './task-graph-layout'
import { TaskGraphMarkerType } from './task-graph-layout'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import { ROW_HEIGHT, LANE_WIDTH } from '../components/task-graph-svg'

// Type used is D3LayoutResult - it's just the return type of computeD3Layout

// Helper to create a mock issue render line
function createIssueRenderLine(
  overrides: Partial<TaskGraphIssueRenderLine> = {}
): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    issueId: 'test-123',
    title: 'Test Issue',
    description: null,
    branchName: null,
    lane: 0,
    marker: TaskGraphMarkerType.Open,
    parentLane: null,
    isFirstChild: false,
    isSeriesChild: false,
    drawTopLine: false,
    drawBottomLine: false,
    seriesConnectorFromLane: null,
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
    hasDescription: false,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    drawLane0Connector: false,
    isLastLane0Connector: false,
    drawLane0PassThrough: false,
    lane0Color: null,
    hasHiddenParent: false,
    hiddenParentIsSeriesMode: false,
    executionMode: ExecutionMode.SERIES,
    ...overrides,
  }
}

// Helper to create a PR render line
function createPrRenderLine(overrides: Partial<TaskGraphPrRenderLine> = {}): TaskGraphPrRenderLine {
  return {
    type: 'pr',
    prNumber: 123,
    title: 'Test PR',
    url: null,
    isMerged: true,
    hasDescription: false,
    agentStatus: null,
    drawTopLine: false,
    drawBottomLine: false,
    ...overrides,
  }
}

describe('computeD3Layout', () => {
  describe('basic positioning', () => {
    it('computes positions for a single issue node', () => {
      const renderLines = [createIssueRenderLine({ issueId: 'issue-1', lane: 0 })]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 1)

      expect(result.nodes).toHaveLength(1)
      expect(result.nodes[0].type).toBe('issue')
      expect(result.nodes[0].x).toBe(LANE_WIDTH / 2) // Lane 0 center
      expect(result.nodes[0].y).toBe(ROW_HEIGHT / 2) // First row center
      expect(result.nodes[0].contentY).toBe(0)
      expect(result.nodes[0].rowHeight).toBe(ROW_HEIGHT)
    })

    it('computes positions for multiple issue nodes', () => {
      const renderLines = [
        createIssueRenderLine({ issueId: 'issue-1', lane: 0 }),
        createIssueRenderLine({ issueId: 'issue-2', lane: 1 }),
        createIssueRenderLine({ issueId: 'issue-3', lane: 0 }),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 2)

      expect(result.nodes).toHaveLength(3)

      // First node
      expect(result.nodes[0].y).toBe(ROW_HEIGHT / 2)
      expect(result.nodes[0].contentY).toBe(0)

      // Second node
      expect(result.nodes[1].y).toBe(ROW_HEIGHT + ROW_HEIGHT / 2)
      expect(result.nodes[1].contentY).toBe(ROW_HEIGHT)
      expect(result.nodes[1].x).toBe(LANE_WIDTH / 2 + LANE_WIDTH) // Lane 1

      // Third node
      expect(result.nodes[2].y).toBe(ROW_HEIGHT * 2 + ROW_HEIGHT / 2)
      expect(result.nodes[2].contentY).toBe(ROW_HEIGHT * 2)
    })

    it('computes total height correctly', () => {
      const renderLines = [
        createIssueRenderLine({ issueId: 'issue-1' }),
        createIssueRenderLine({ issueId: 'issue-2' }),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 1)

      expect(result.totalHeight).toBe(ROW_HEIGHT * 2)
    })

    it('computes total width based on max lanes', () => {
      const renderLines = [createIssueRenderLine()]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 3)

      expect(result.totalWidth).toBe(LANE_WIDTH * 3 + LANE_WIDTH / 2)
    })
  })

  describe('expansion handling', () => {
    it('increases row height for expanded nodes', () => {
      const renderLines = [
        createIssueRenderLine({ issueId: 'issue-1' }),
        createIssueRenderLine({ issueId: 'issue-2' }),
      ]

      const expandedIds = new Set(['issue-1'])
      const expandedHeights = new Map([['issue-1', 200]])

      const result = computeD3Layout(renderLines, expandedIds, expandedHeights, 1)

      expect(result.nodes[0].rowHeight).toBe(ROW_HEIGHT + 200)
      expect(result.nodes[1].rowHeight).toBe(ROW_HEIGHT) // Second node not expanded

      // Second node should be pushed down by the expanded height
      expect(result.nodes[1].contentY).toBe(ROW_HEIGHT + 200)
      expect(result.totalHeight).toBe(ROW_HEIGHT * 2 + 200)
    })

    it('keeps node center Y in collapsed portion', () => {
      const renderLines = [createIssueRenderLine({ issueId: 'issue-1' })]

      const expandedIds = new Set(['issue-1'])
      const expandedHeights = new Map([['issue-1', 300]])

      const result = computeD3Layout(renderLines, expandedIds, expandedHeights, 1)

      // Node center should still be in the first ROW_HEIGHT portion
      expect(result.nodes[0].y).toBe(ROW_HEIGHT / 2)
    })
  })

  describe('edge generation', () => {
    it('generates top line edge when drawTopLine is true', () => {
      const renderLines = [createIssueRenderLine({ issueId: 'issue-1', drawTopLine: true })]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 1)

      const topLineEdge = result.edges.find((e) => e.id.includes('top-line'))
      expect(topLineEdge).toBeDefined()
      expect(topLineEdge!.type).toBe('topLine')
    })

    it('generates bottom line edge when drawBottomLine is true', () => {
      const renderLines = [createIssueRenderLine({ issueId: 'issue-1', drawBottomLine: true })]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 1)

      const bottomLineEdge = result.edges.find((e) => e.id.includes('bottom-line'))
      expect(bottomLineEdge).toBeDefined()
      expect(bottomLineEdge!.type).toBe('bottomLine')
    })

    it('generates lane 0 passthrough when drawLane0PassThrough is true', () => {
      const renderLines = [
        createIssueRenderLine({ issueId: 'issue-1', lane: 1, drawLane0PassThrough: true }),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 2)

      const lane0Edge = result.edges.find((e) => e.type === 'lane0Passthrough')
      expect(lane0Edge).toBeDefined()
    })

    it('generates lane 0 connector when drawLane0Connector is true', () => {
      const renderLines = [
        createIssueRenderLine({
          issueId: 'issue-1',
          lane: 1,
          drawLane0Connector: true,
          isLastLane0Connector: false,
        }),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 2)

      const lane0Edge = result.edges.find((e) => e.type === 'lane0Connector')
      expect(lane0Edge).toBeDefined()
    })

    it('generates parallel parent connector for non-series children', () => {
      const renderLines = [
        createIssueRenderLine({
          issueId: 'issue-1',
          lane: 0,
          parentLane: 1,
          isSeriesChild: false,
          isFirstChild: true,
        }),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 2)

      const parentEdge = result.edges.find((e) => e.type === 'parentConnector')
      expect(parentEdge).toBeDefined()
    })

    it('generates series connector when seriesConnectorFromLane is set', () => {
      const renderLines = [
        createIssueRenderLine({
          issueId: 'issue-1',
          lane: 0,
          seriesConnectorFromLane: 1,
        }),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 2)

      const seriesEdge = result.edges.find((e) => e.type === 'seriesConnector')
      expect(seriesEdge).toBeDefined()
    })
  })

  describe('PR handling', () => {
    it('computes positions for PR nodes', () => {
      const renderLines = [
        createPrRenderLine({ prNumber: 100 }),
        createIssueRenderLine({ issueId: 'issue-1' }),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 1)

      expect(result.nodes[0].type).toBe('pr')
      expect(result.nodes[0].x).toBe(LANE_WIDTH / 2) // Lane 0
      expect(result.nodes[1].contentY).toBe(ROW_HEIGHT)
    })

    it('generates PR vertical lines when needed', () => {
      const renderLines = [
        createPrRenderLine({ prNumber: 100, drawTopLine: true, drawBottomLine: true }),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 1)

      const prTopEdge = result.edges.find((e) => e.id.includes('pr-top'))
      const prBottomEdge = result.edges.find((e) => e.id.includes('pr-bottom'))

      expect(prTopEdge).toBeDefined()
      expect(prBottomEdge).toBeDefined()
    })
  })

  describe('separator handling', () => {
    it('computes positions for separator rows', () => {
      const renderLines = [
        createPrRenderLine(),
        { type: 'separator' as const },
        createIssueRenderLine(),
      ]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 1)

      expect(result.nodes[1].type).toBe('separator')
    })
  })

  describe('load more handling', () => {
    it('computes positions for load more rows', () => {
      const renderLines = [{ type: 'loadMore' as const }, createIssueRenderLine()]
      const result = computeD3Layout(renderLines, new Set(), new Map(), 1)

      expect(result.nodes[0].type).toBe('loadMore')
    })
  })
})

describe('recalculateLayoutForExpansion', () => {
  it('recalculates layout with new expansion state', () => {
    const initialLines = [
      createIssueRenderLine({ issueId: 'issue-1' }),
      createIssueRenderLine({ issueId: 'issue-2' }),
    ]
    const initialLayout = computeD3Layout(initialLines, new Set(), new Map(), 1)

    const newExpandedIds = new Set(['issue-1'])
    const newExpandedHeights = new Map([['issue-1', 150]])

    const newLayout = recalculateLayoutForExpansion(
      initialLayout,
      newExpandedIds,
      newExpandedHeights
    )

    expect(newLayout.nodes[0].rowHeight).toBe(ROW_HEIGHT + 150)
    expect(newLayout.totalHeight).toBe(ROW_HEIGHT * 2 + 150)
  })
})

describe('parallel parent vertical span post-processing', () => {
  it('emits a continuous vertical span between first and last child rows', () => {
    // Parent at lane 2, three children at lane 0 across three rows
    const renderLines = [
      createIssueRenderLine({
        issueId: 'child-1',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: true,
      }),
      createIssueRenderLine({
        issueId: 'child-2',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: false,
      }),
      createIssueRenderLine({
        issueId: 'child-3',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: false,
      }),
    ]
    const result = computeD3Layout(renderLines, new Set(), new Map(), 3)

    const spanEdge = result.edges.find((e) => e.id === 'parallel-vertical-span-lane2')
    expect(spanEdge).toBeDefined()
    expect(spanEdge!.type).toBe('parallelVertical')

    // Span should go from bottom of first child row to top of last child row
    const expectedStartY = ROW_HEIGHT // bottom of row 0
    const expectedEndY = ROW_HEIGHT * 2 // top of row 2
    const parentLaneX = LANE_WIDTH / 2 + 2 * LANE_WIDTH
    expect(spanEdge!.path).toBe(
      `M ${parentLaneX} ${expectedStartY} L ${parentLaneX} ${expectedEndY}`
    )
  })

  it('does not emit a span for a single child', () => {
    const renderLines = [
      createIssueRenderLine({
        issueId: 'child-1',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: true,
      }),
    ]
    const result = computeD3Layout(renderLines, new Set(), new Map(), 3)

    const spanEdge = result.edges.find((e) => e.id.startsWith('parallel-vertical-span'))
    expect(spanEdge).toBeUndefined()
  })

  it('does not emit a span for two adjacent children (no gap)', () => {
    const renderLines = [
      createIssueRenderLine({
        issueId: 'child-1',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: true,
      }),
      createIssueRenderLine({
        issueId: 'child-2',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: false,
      }),
    ]
    const result = computeD3Layout(renderLines, new Set(), new Map(), 3)

    // startY = ROW_HEIGHT (bottom of row 0), endY = ROW_HEIGHT (top of row 1) — equal, no span
    const spanEdge = result.edges.find((e) => e.id.startsWith('parallel-vertical-span'))
    expect(spanEdge).toBeUndefined()
  })

  it('emits a span that covers an inline editor gap between children', () => {
    const renderLines = [
      createIssueRenderLine({
        issueId: 'child-1',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: true,
      }),
      createIssueRenderLine({
        issueId: 'child-2',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: false,
      }),
    ]
    // Insert editor below child-1, adding an extra ROW_HEIGHT between children
    const result = computeD3Layout(renderLines, new Set(), new Map(), 3, {
      referenceIssueId: 'child-1',
      position: 'below',
    })

    const spanEdge = result.edges.find((e) => e.id.startsWith('parallel-vertical-span'))
    expect(spanEdge).toBeDefined()

    // child-1 row bottom = ROW_HEIGHT, editor adds ROW_HEIGHT, child-2 top = ROW_HEIGHT * 2
    const expectedStartY = ROW_HEIGHT
    const expectedEndY = ROW_HEIGHT * 2
    const parentLaneX = LANE_WIDTH / 2 + 2 * LANE_WIDTH
    expect(spanEdge!.path).toBe(
      `M ${parentLaneX} ${expectedStartY} L ${parentLaneX} ${expectedEndY}`
    )
  })

  it('tracks multiple parent lanes independently', () => {
    const renderLines = [
      createIssueRenderLine({
        issueId: 'child-a1',
        lane: 0,
        parentLane: 1,
        isSeriesChild: false,
        isFirstChild: true,
      }),
      createIssueRenderLine({
        issueId: 'child-b1',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: true,
      }),
      createIssueRenderLine({
        issueId: 'unrelated',
        lane: 0,
      }),
      createIssueRenderLine({
        issueId: 'child-a2',
        lane: 0,
        parentLane: 1,
        isSeriesChild: false,
        isFirstChild: false,
      }),
      createIssueRenderLine({
        issueId: 'child-b2',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: false,
      }),
    ]
    const result = computeD3Layout(renderLines, new Set(), new Map(), 3)

    const spanLane1 = result.edges.find((e) => e.id === 'parallel-vertical-span-lane1')
    const spanLane2 = result.edges.find((e) => e.id === 'parallel-vertical-span-lane2')

    expect(spanLane1).toBeDefined()
    expect(spanLane2).toBeDefined()

    // Lane 1: first child row 0 bottom (ROW_HEIGHT) to last child row 3 top (ROW_HEIGHT * 3)
    const lane1X = LANE_WIDTH / 2 + 1 * LANE_WIDTH
    expect(spanLane1!.path).toBe(`M ${lane1X} ${ROW_HEIGHT} L ${lane1X} ${ROW_HEIGHT * 3}`)

    // Lane 2: first child row 1 bottom (ROW_HEIGHT * 2) to last child row 4 top (ROW_HEIGHT * 4)
    const lane2X = LANE_WIDTH / 2 + 2 * LANE_WIDTH
    expect(spanLane2!.path).toBe(`M ${lane2X} ${ROW_HEIGHT * 2} L ${lane2X} ${ROW_HEIGHT * 4}`)
  })

  it('does not emit a span for series children', () => {
    const renderLines = [
      createIssueRenderLine({
        issueId: 'child-1',
        lane: 0,
        parentLane: 2,
        isSeriesChild: true,
        isFirstChild: true,
      }),
      createIssueRenderLine({
        issueId: 'unrelated',
        lane: 0,
      }),
      createIssueRenderLine({
        issueId: 'child-2',
        lane: 0,
        parentLane: 2,
        isSeriesChild: true,
        isFirstChild: false,
      }),
    ]
    const result = computeD3Layout(renderLines, new Set(), new Map(), 3)

    const spanEdge = result.edges.find((e) => e.id.startsWith('parallel-vertical-span'))
    expect(spanEdge).toBeUndefined()
  })

  it('accounts for expanded rows when computing span start', () => {
    const renderLines = [
      createIssueRenderLine({
        issueId: 'child-1',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: true,
      }),
      createIssueRenderLine({
        issueId: 'unrelated',
        lane: 0,
      }),
      createIssueRenderLine({
        issueId: 'child-2',
        lane: 0,
        parentLane: 2,
        isSeriesChild: false,
        isFirstChild: false,
      }),
    ]

    // Expand child-1 by 100px
    const expandedIds = new Set(['child-1'])
    const expandedHeights = new Map([['child-1', 100]])
    const result = computeD3Layout(renderLines, expandedIds, expandedHeights, 3)

    const spanEdge = result.edges.find((e) => e.id === 'parallel-vertical-span-lane2')
    expect(spanEdge).toBeDefined()

    // First child row: cumulativeY=0, rowHeight=ROW_HEIGHT+100, so rowBottom = ROW_HEIGHT + 100
    // But span startY should be cumulativeY + ROW_HEIGHT (the collapsed portion bottom)
    // Unrelated row: cumulativeY = ROW_HEIGHT + 100
    // child-2 top: cumulativeY = ROW_HEIGHT + 100 + ROW_HEIGHT = ROW_HEIGHT * 2 + 100
    const expectedStartY = ROW_HEIGHT
    const expectedEndY = ROW_HEIGHT * 2 + 100
    const parentLaneX = LANE_WIDTH / 2 + 2 * LANE_WIDTH
    expect(spanEdge!.path).toBe(
      `M ${parentLaneX} ${expectedStartY} L ${parentLaneX} ${expectedEndY}`
    )
  })
})

describe('getContentX', () => {
  it('computes content X offset based on max lanes', () => {
    expect(getContentX(1)).toBe(LANE_WIDTH + LANE_WIDTH / 2)
    expect(getContentX(3)).toBe(LANE_WIDTH * 3 + LANE_WIDTH / 2)
  })
})

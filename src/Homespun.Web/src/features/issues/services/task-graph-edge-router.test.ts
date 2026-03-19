import { describe, it, expect } from 'vitest'
import {
  generateOrthogonalPath,
  generateVerticalLine,
  generateHorizontalLine,
  generateSeriesConnectorPath,
  generateParallelConnectorPath,
  generateParallelVerticalLine,
  generateLane0ConnectorPath,
  generateSBendPath,
  getLaneCenterX,
  getRowCenterY,
  findSafeVerticalChannel,
  routeBypassEdge,
  type RoutingNode,
} from './task-graph-edge-router'
import { LANE_WIDTH, ROW_HEIGHT, NODE_RADIUS } from '../components/task-graph-svg'

describe('task-graph-edge-router', () => {
  describe('generateOrthogonalPath', () => {
    it('generates a straight horizontal line when y values are equal', () => {
      const path = generateOrthogonalPath({
        startX: 0,
        startY: 20,
        endX: 100,
        endY: 20,
      })
      expect(path).toBe('M 0 20 L 100 20')
    })

    it('generates a straight vertical line when x values are equal', () => {
      const path = generateOrthogonalPath({
        startX: 50,
        startY: 0,
        endX: 50,
        endY: 100,
      })
      expect(path).toBe('M 50 0 L 50 100')
    })

    it('generates horizontal-first L-bend going right and down', () => {
      const path = generateOrthogonalPath({
        startX: 0,
        startY: 0,
        endX: 100,
        endY: 100,
        cornerRadius: 10,
        direction: 'horizontal-first',
      })
      // Should contain an arc command
      expect(path).toContain('A')
      expect(path).toMatch(/^M 0 0/)
      expect(path).toMatch(/L 100 100$/)
    })

    it('generates horizontal-first L-bend going left and down', () => {
      const path = generateOrthogonalPath({
        startX: 100,
        startY: 0,
        endX: 0,
        endY: 100,
        cornerRadius: 10,
        direction: 'horizontal-first',
      })
      expect(path).toContain('A')
      expect(path).toMatch(/^M 100 0/)
      expect(path).toMatch(/L 0 100$/)
    })

    it('generates vertical-first L-bend', () => {
      const path = generateOrthogonalPath({
        startX: 0,
        startY: 0,
        endX: 100,
        endY: 100,
        cornerRadius: 10,
        direction: 'vertical-first',
      })
      expect(path).toContain('A')
      // First segment should be vertical
      expect(path).toMatch(/^M 0 0 L 0/)
    })

    it('limits corner radius to half the shorter dimension', () => {
      const path = generateOrthogonalPath({
        startX: 0,
        startY: 0,
        endX: 10, // Small horizontal distance
        endY: 100,
        cornerRadius: 20, // Larger than half of 10
        direction: 'horizontal-first',
      })
      // Should still generate a valid path
      expect(path).toContain('A')
    })
  })

  describe('generateVerticalLine', () => {
    it('generates a simple vertical line path', () => {
      const path = generateVerticalLine(50, 0, 100)
      expect(path).toBe('M 50 0 L 50 100')
    })

    it('works with negative coordinates', () => {
      const path = generateVerticalLine(-20, -50, 50)
      expect(path).toBe('M -20 -50 L -20 50')
    })
  })

  describe('generateHorizontalLine', () => {
    it('generates a simple horizontal line path', () => {
      const path = generateHorizontalLine(30, 0, 100)
      expect(path).toBe('M 0 30 L 100 30')
    })
  })

  describe('generateSeriesConnectorPath', () => {
    it('generates L-shaped path from child lane up to parent node', () => {
      const childLaneX = 72 // Lane 3
      const rowTopY = 0
      const parentX = 36 // Lane 1
      const parentY = 20

      const path = generateSeriesConnectorPath(childLaneX, rowTopY, parentX, parentY)

      expect(path).toContain('M 72 0') // Starts at child lane, top
      expect(path).toContain('A') // Has arc for corner
      // Should end near parent node
      expect(path).toContain(String(parentX - NODE_RADIUS - 2))
    })
  })

  describe('generateParallelConnectorPath', () => {
    it('generates first child connector with arc going down', () => {
      const childX = 12
      const childY = 20
      const parentLaneX = 36
      const rowBottomY = 40

      const path = generateParallelConnectorPath(
        childX,
        childY,
        parentLaneX,
        rowBottomY,
        true // isFirstChild
      )

      expect(path).toContain('A') // Has arc
      expect(path).toContain(String(rowBottomY)) // Goes to bottom
    })

    it('generates non-first child connector as horizontal line', () => {
      const childX = 12
      const childY = 20
      const parentLaneX = 36
      const rowBottomY = 40

      const path = generateParallelConnectorPath(
        childX,
        childY,
        parentLaneX,
        rowBottomY,
        false // not first child
      )

      // Should be a simple horizontal line
      expect(path).not.toContain('A')
      expect(path).toContain(`L ${parentLaneX} ${childY}`)
    })
  })

  describe('generateParallelVerticalLine', () => {
    it('generates full vertical line at parent lane', () => {
      const path = generateParallelVerticalLine(36, 0, 40)
      expect(path).toBe('M 36 0 L 36 40')
    })
  })

  describe('generateLane0ConnectorPath', () => {
    it('generates passthrough line when isPassthrough is true', () => {
      const path = generateLane0ConnectorPath(12, 36, 20, 0, 40, false, true)
      expect(path).toBe('M 12 0 L 12 40')
    })

    it('generates last connector with arc', () => {
      const path = generateLane0ConnectorPath(12, 36, 20, 0, 40, true, false)

      expect(path).toContain('A') // Has arc
      expect(path).toContain('M 12 0') // Starts at lane 0, top
    })

    it('generates non-last connector with vertical line and horizontal branch', () => {
      const path = generateLane0ConnectorPath(12, 36, 20, 0, 40, false, false)

      // Should have two path segments (M...L for vertical, M...L for horizontal)
      const moveCommands = path.match(/M/g)
      expect(moveCommands).toHaveLength(2)
    })
  })

  describe('generateSBendPath', () => {
    it('generates path with two bends through midpoint', () => {
      const path = generateSBendPath(0, 0, 100, 100, 50)

      // Should have multiple arc commands for the bends
      const arcCommands = path.match(/A/g)
      expect(arcCommands!.length).toBeGreaterThanOrEqual(2)
    })
  })

  describe('getLaneCenterX', () => {
    it('calculates center X for lane 0', () => {
      expect(getLaneCenterX(0)).toBe(LANE_WIDTH / 2)
    })

    it('calculates center X for higher lanes', () => {
      expect(getLaneCenterX(1)).toBe(LANE_WIDTH / 2 + LANE_WIDTH)
      expect(getLaneCenterX(2)).toBe(LANE_WIDTH / 2 + LANE_WIDTH * 2)
    })
  })

  describe('getRowCenterY', () => {
    it('calculates center Y from row top', () => {
      expect(getRowCenterY(0)).toBe(ROW_HEIGHT / 2)
      expect(getRowCenterY(40)).toBe(40 + ROW_HEIGHT / 2)
    })
  })

  describe('findSafeVerticalChannel', () => {
    it('returns lane boundary when no intermediate nodes', () => {
      const channel = findSafeVerticalChannel(12, 60, [])
      // Should be a lane boundary
      expect(channel % LANE_WIDTH).toBe(0)
    })

    it('finds safe channel avoiding intermediate nodes', () => {
      const intermediateNodes: RoutingNode[] = [
        {
          issueId: 'node1',
          x: 36, // Lane 1 center
          y: 60,
          lane: 1,
          parentLane: null,
          isSeriesChild: false,
          isFirstChild: false,
          rowHeight: ROW_HEIGHT,
        },
      ]

      const channel = findSafeVerticalChannel(12, 60, intermediateNodes)

      // Should not collide with node at x=36
      expect(Math.abs(channel - 36)).toBeGreaterThan(NODE_RADIUS)
    })

    it('returns midpoint as fallback when no safe lane boundary', () => {
      // Create nodes at all lane boundaries
      const intermediateNodes: RoutingNode[] = [0, 1, 2].map((lane) => ({
        issueId: `node${lane}`,
        x: LANE_WIDTH / 2 + lane * LANE_WIDTH,
        y: 60,
        lane,
        parentLane: null,
        isSeriesChild: false,
        isFirstChild: false,
        rowHeight: ROW_HEIGHT,
      }))

      const channel = findSafeVerticalChannel(12, 36, intermediateNodes)

      // Should return a valid number (might be midpoint)
      expect(typeof channel).toBe('number')
    })
  })

  describe('routeBypassEdge', () => {
    it('routes around intermediate nodes using S-bend', () => {
      const intermediateNodes: RoutingNode[] = [
        {
          issueId: 'node1',
          x: 36,
          y: 60,
          lane: 1,
          parentLane: null,
          isSeriesChild: false,
          isFirstChild: false,
          rowHeight: ROW_HEIGHT,
        },
      ]

      const path = routeBypassEdge(12, 0, 60, 120, intermediateNodes)

      expect(path).toContain('M')
      expect(path).toContain('L')
    })

    it('uses L-bend when source or target aligns with safe channel', () => {
      // No intermediate nodes means first lane boundary is safe
      const path = routeBypassEdge(0, 0, 60, 100, [])

      expect(path).toContain('A') // Has arc for the bend
    })
  })
})

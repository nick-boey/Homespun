/**
 * Tests for use-camera hook.
 *
 * Tests camera/viewport state management for Konva canvas panning.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useCamera, clampPosition } from './use-camera'

/** Create a mock TouchEvent-like object */
function createMockTouchEvent(touches: Array<{ clientX: number; clientY: number }>): {
  touches: Array<{ clientX: number; clientY: number }>
  preventDefault: ReturnType<typeof vi.fn>
} {
  return {
    touches: touches.map((t) => ({ clientX: t.clientX, clientY: t.clientY })),
    preventDefault: vi.fn(),
  }
}

describe('clampPosition', () => {
  it('returns zero for content smaller than viewport', () => {
    const result = clampPosition(100, 200, 150)
    expect(result).toBe(0)
  })

  it('clamps to minimum (left/top edge)', () => {
    const result = clampPosition(50, 200, 400)
    // Maximum scroll = content (400) - viewport (200) = 200
    // Since position 50 is valid (between 0 and 200), it should be unchanged
    expect(result).toBe(50)
  })

  it('clamps to maximum (right/bottom edge)', () => {
    const result = clampPosition(300, 200, 400)
    // Maximum scroll = content (400) - viewport (200) = 200
    // Position 300 exceeds max, so it should be clamped to 200
    expect(result).toBe(200)
  })

  it('prevents negative position', () => {
    const result = clampPosition(-50, 200, 400)
    expect(result).toBe(0)
  })
})

describe('useCamera', () => {
  const defaultContentSize = { width: 800, height: 1200 }
  const defaultViewportSize = { width: 400, height: 600 }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('initial state', () => {
    it('starts at position (0, 0)', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      expect(result.current.camera).toEqual({ x: 0, y: 0 })
    })
  })

  describe('panning', () => {
    it('updates position on pan', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.panTo(100, 200)
      })

      expect(result.current.camera).toEqual({ x: 100, y: 200 })
    })

    it('clamps position to content bounds', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      // Try to pan beyond content bounds
      act(() => {
        result.current.panTo(1000, 2000)
      })

      // Should be clamped to max scroll (content - viewport)
      expect(result.current.camera.x).toBeLessThanOrEqual(
        defaultContentSize.width - defaultViewportSize.width
      )
      expect(result.current.camera.y).toBeLessThanOrEqual(
        defaultContentSize.height - defaultViewportSize.height
      )
    })

    it('prevents negative positions', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.panTo(-100, -200)
      })

      expect(result.current.camera).toEqual({ x: 0, y: 0 })
    })
  })

  describe('panBy', () => {
    it('adds delta to current position', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.panTo(100, 100)
      })

      act(() => {
        result.current.panBy(50, 25)
      })

      expect(result.current.camera).toEqual({ x: 150, y: 125 })
    })

    it('clamps after pan delta', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.panBy(-1000, -1000)
      })

      expect(result.current.camera).toEqual({ x: 0, y: 0 })
    })
  })

  describe('scrollToRow', () => {
    it('scrolls to show row in view', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.scrollToRow(10, 40)
      })

      // Row 10 at height 40 = y position 400
      // This should adjust camera to show row
      expect(result.current.camera.y).toBeGreaterThanOrEqual(0)
    })

    it('does not scroll if row is already visible', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.scrollToRow(5, 40)
      })

      // Row 5 at height 40 = y position 200
      // Should be visible within 600px viewport
      expect(result.current.camera.y).toBeLessThanOrEqual(200)
    })
  })

  describe('reset', () => {
    it('resets camera to origin', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.panTo(200, 300)
      })

      act(() => {
        result.current.reset()
      })

      expect(result.current.camera).toEqual({ x: 0, y: 0 })
    })
  })

  describe('handleDragMove', () => {
    it('returns handler for Konva Stage drag', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      expect(typeof result.current.handleDragEnd).toBe('function')
    })
  })

  describe('viewport changes', () => {
    it('re-clamps on viewport size change', () => {
      const { result, rerender } = renderHook(
        ({ contentSize, viewportSize }) => useCamera(contentSize, viewportSize),
        {
          initialProps: {
            contentSize: defaultContentSize,
            viewportSize: defaultViewportSize,
          },
        }
      )

      // Pan to an edge position
      act(() => {
        result.current.panTo(400, 600)
      })

      // Now shrink the viewport so position is valid
      rerender({
        contentSize: defaultContentSize,
        viewportSize: { width: 200, height: 300 },
      })

      // Position should remain valid
      expect(result.current.camera.x).toBeLessThanOrEqual(600) // 800 - 200
      expect(result.current.camera.y).toBeLessThanOrEqual(900) // 1200 - 300
    })
  })

  describe('touch panning', () => {
    it('pans on single-finger touch move', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 200 }]) as unknown as TouchEvent
        )
      })

      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 50, clientY: 150 }]) as unknown as TouchEvent
        )
      })

      // Dragged left 50px and up 50px => camera pans right/down by (50, 50)
      expect(result.current.camera).toEqual({ x: 50, y: 50 })
    })

    it('tracks cumulative touch moves', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 200, clientY: 200 }]) as unknown as TouchEvent
        )
      })

      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 180, clientY: 180 }]) as unknown as TouchEvent
        )
      })

      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 160, clientY: 160 }]) as unknown as TouchEvent
        )
      })

      // Total delta: 40px in each direction
      expect(result.current.camera).toEqual({ x: 40, y: 40 })
    })

    it('calls preventDefault on touchmove to prevent page scrolling', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 200 }]) as unknown as TouchEvent
        )
      })

      const moveEvent = createMockTouchEvent([{ clientX: 90, clientY: 190 }])
      act(() => {
        result.current.touchHandlers.handleTouchMove(moveEvent as unknown as TouchEvent)
      })

      expect(moveEvent.preventDefault).toHaveBeenCalled()
    })

    it('resets touch tracking on touchend', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 200 }]) as unknown as TouchEvent
        )
      })

      act(() => {
        result.current.touchHandlers.handleTouchEnd()
      })

      // Move after touchend should not pan
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 50, clientY: 150 }]) as unknown as TouchEvent
        )
      })

      expect(result.current.camera).toEqual({ x: 0, y: 0 })
    })

    it('ignores multi-touch for single-finger panning', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([
            { clientX: 100, clientY: 200 },
            { clientX: 200, clientY: 300 },
          ]) as unknown as TouchEvent
        )
      })

      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([
            { clientX: 50, clientY: 150 },
            { clientX: 150, clientY: 250 },
          ]) as unknown as TouchEvent
        )
      })

      // Multi-touch should not trigger single-finger panning
      expect(result.current.camera).toEqual({ x: 0, y: 0 })
    })

    it('clamps touch panning to content bounds', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 500, clientY: 500 }]) as unknown as TouchEvent
        )
      })

      // Try to pan far beyond content
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 0, clientY: 0 }]) as unknown as TouchEvent
        )
      })

      // Should be clamped to max scroll
      expect(result.current.camera.x).toBeLessThanOrEqual(
        defaultContentSize.width - defaultViewportSize.width
      )
      expect(result.current.camera.y).toBeLessThanOrEqual(
        defaultContentSize.height - defaultViewportSize.height
      )
    })
  })
})

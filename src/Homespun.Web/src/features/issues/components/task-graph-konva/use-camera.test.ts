/**
 * Tests for use-camera hook.
 *
 * Tests camera/viewport state management for Konva canvas panning.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import {
  useCamera,
  clampPosition,
  AXIS_LOCK_THRESHOLD,
  MOMENTUM_FRICTION,
  MOMENTUM_MIN_VELOCITY,
} from './use-camera'

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

    it('returns handleDragMove handler', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      expect(typeof result.current.handleDragMove).toBe('function')
    })
  })

  describe('handleWheel axis locking', () => {
    it('locks to vertical when deltaY dominates', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.handleWheel({
          deltaX: 2,
          deltaY: 10,
          preventDefault: vi.fn(),
        } as unknown as React.WheelEvent)
      })

      expect(result.current.camera).toEqual({ x: 0, y: 10 })
    })

    it('locks to horizontal when deltaX dominates', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.handleWheel({
          deltaX: 10,
          deltaY: 2,
          preventDefault: vi.fn(),
        } as unknown as React.WheelEvent)
      })

      expect(result.current.camera).toEqual({ x: 10, y: 0 })
    })

    it('locks to horizontal when deltas are equal', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.handleWheel({
          deltaX: 5,
          deltaY: 5,
          preventDefault: vi.fn(),
        } as unknown as React.WheelEvent)
      })

      // >= means horizontal wins on tie
      expect(result.current.camera).toEqual({ x: 5, y: 0 })
    })
  })

  describe('handleDragMove axis locking', () => {
    const makeMockStage = (x: number, y: number) => ({
      x: vi.fn().mockReturnValue(x),
      y: vi.fn().mockReturnValue(y),
    })

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const fireEvent = (handler: (e: any) => void, stage: ReturnType<typeof makeMockStage>) => {
      handler({ target: stage })
    }

    it('locks to vertical axis when vertical movement exceeds threshold first', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      // First call: initialize drag start position
      act(() => {
        fireEvent(result.current.handleDragMove, makeMockStage(0, 0))
      })

      // Second call: move vertically past threshold
      const movedStage = makeMockStage(0, -20)
      act(() => {
        fireEvent(result.current.handleDragMove, movedStage)
      })

      // Should constrain horizontal: stage x reset to starting position (0)
      expect(movedStage.x).toHaveBeenCalledWith(0)
    })

    it('locks to horizontal axis when horizontal movement exceeds threshold first', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      // First call: initialize
      act(() => {
        fireEvent(result.current.handleDragMove, makeMockStage(0, 0))
      })

      // Second call: move horizontally past threshold
      const movedStage = makeMockStage(-20, 0)
      act(() => {
        fireEvent(result.current.handleDragMove, movedStage)
      })

      // Should constrain vertical: stage y reset to starting position (0)
      expect(movedStage.y).toHaveBeenCalledWith(0)
    })

    it('resets axis lock after drag end', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      // First drag: initialize and lock to vertical
      act(() => {
        fireEvent(result.current.handleDragMove, makeMockStage(0, 0))
      })
      const stage1 = makeMockStage(0, -20)
      act(() => {
        fireEvent(result.current.handleDragMove, stage1)
      })

      // End the first drag
      act(() => {
        fireEvent(result.current.handleDragEnd, stage1)
      })

      // Second drag: initialize
      act(() => {
        fireEvent(result.current.handleDragMove, makeMockStage(0, 0))
      })

      // Second drag: move horizontally — should lock to horizontal now
      const stage2 = makeMockStage(-20, 0)
      act(() => {
        fireEvent(result.current.handleDragMove, stage2)
      })

      // Should constrain vertical (locked to horizontal)
      expect(stage2.y).toHaveBeenCalledWith(0)
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

      // Move only vertically to avoid axis lock ambiguity
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 100, clientY: 150 }]) as unknown as TouchEvent
        )
      })

      // Dragged up 50px => camera pans down by 50, x unchanged
      expect(result.current.camera).toEqual({ x: 0, y: 50 })
    })

    it('tracks cumulative touch moves', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 200, clientY: 200 }]) as unknown as TouchEvent
        )
      })

      // Move only horizontally to test cumulative panning with axis lock
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 180, clientY: 200 }]) as unknown as TouchEvent
        )
      })

      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 160, clientY: 200 }]) as unknown as TouchEvent
        )
      })

      // Total delta: 40px horizontally
      expect(result.current.camera).toEqual({ x: 40, y: 0 })
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

  describe('touch axis locking', () => {
    it('locks to vertical when vertical movement exceeds threshold first', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 100 }]) as unknown as TouchEvent
        )
      })

      // Move vertically past threshold, with small horizontal component
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([
            { clientX: 98, clientY: 100 - AXIS_LOCK_THRESHOLD - 10 },
          ]) as unknown as TouchEvent
        )
      })

      // Should only pan vertically (y changes, x stays 0)
      expect(result.current.camera.x).toBe(0)
      expect(result.current.camera.y).toBeGreaterThan(0)
    })

    it('locks to horizontal when horizontal movement exceeds threshold first', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 100 }]) as unknown as TouchEvent
        )
      })

      // Move horizontally past threshold, with small vertical component
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([
            { clientX: 100 - AXIS_LOCK_THRESHOLD - 10, clientY: 98 },
          ]) as unknown as TouchEvent
        )
      })

      // Should only pan horizontally (x changes, y stays 0)
      expect(result.current.camera.x).toBeGreaterThan(0)
      expect(result.current.camera.y).toBe(0)
    })

    it('moves freely in both axes below threshold', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 100 }]) as unknown as TouchEvent
        )
      })

      // Move less than threshold in both axes
      const smallDelta = Math.floor(AXIS_LOCK_THRESHOLD / 2)
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([
            { clientX: 100 - smallDelta, clientY: 100 - smallDelta },
          ]) as unknown as TouchEvent
        )
      })

      // Both axes should move since we're below the lock threshold
      expect(result.current.camera.x).toBe(smallDelta)
      expect(result.current.camera.y).toBe(smallDelta)
    })

    it('resets axis lock on new touch gesture', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      // First gesture: lock to vertical
      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 100 }]) as unknown as TouchEvent
        )
      })
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 100, clientY: 80 }]) as unknown as TouchEvent
        )
      })
      act(() => {
        result.current.touchHandlers.handleTouchEnd()
      })

      const afterFirst = { ...result.current.camera }

      // Second gesture: lock to horizontal
      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 200, clientY: 200 }]) as unknown as TouchEvent
        )
      })
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([
            { clientX: 200 - AXIS_LOCK_THRESHOLD - 10, clientY: 198 },
          ]) as unknown as TouchEvent
        )
      })

      // X should have changed from horizontal swipe, Y should be same as after first gesture
      expect(result.current.camera.x).toBeGreaterThan(afterFirst.x)
      expect(result.current.camera.y).toBe(afterFirst.y)
    })
  })

  describe('touch momentum', () => {
    let rafCallbacks: Map<number, FrameRequestCallback>
    let rafId: number

    beforeEach(() => {
      rafCallbacks = new Map()
      rafId = 0
      vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
        const id = ++rafId
        rafCallbacks.set(id, cb)
        return id
      })
      vi.stubGlobal('cancelAnimationFrame', (id: number) => {
        rafCallbacks.delete(id)
      })
    })

    afterEach(() => {
      vi.unstubAllGlobals()
    })

    function flushRaf(frames: number = 1) {
      for (let i = 0; i < frames; i++) {
        const pending = new Map(rafCallbacks)
        rafCallbacks.clear()
        for (const cb of pending.values()) {
          cb(performance.now())
        }
      }
    }

    function simulateFastSwipe(
      handlers: ReturnType<typeof useCamera>['touchHandlers'],
      direction: 'up' | 'down' | 'left' | 'right',
      speed: number = 20
    ) {
      const startX = 200
      const startY = 200

      act(() => {
        handlers.handleTouchStart(
          createMockTouchEvent([{ clientX: startX, clientY: startY }]) as unknown as TouchEvent
        )
      })

      // Generate several fast move events to build velocity
      for (let i = 1; i <= 5; i++) {
        const dx = direction === 'left' ? -speed * i : direction === 'right' ? speed * i : 0
        const dy = direction === 'up' ? -speed * i : direction === 'down' ? speed * i : 0
        act(() => {
          handlers.handleTouchMove(
            createMockTouchEvent([
              { clientX: startX + dx, clientY: startY + dy },
            ]) as unknown as TouchEvent
          )
        })
      }

      act(() => {
        handlers.handleTouchEnd()
      })
    }

    it('continues scrolling with momentum after a fast swipe', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      simulateFastSwipe(result.current.touchHandlers, 'up')
      const posAfterSwipe = result.current.camera.y

      // Flush several rAF frames — camera should continue moving
      act(() => {
        flushRaf(5)
      })

      expect(result.current.camera.y).toBeGreaterThan(posAfterSwipe)
    })

    it('decelerates and eventually stops', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      simulateFastSwipe(result.current.touchHandlers, 'up')

      // Flush enough frames for momentum to stop
      // With friction 0.95 and min velocity 0.5, even a velocity of 100 stops within ~200 frames
      let prevY = result.current.camera.y
      let stopped = false
      for (let frame = 0; frame < 300; frame++) {
        act(() => {
          flushRaf(1)
        })
        const currentY = result.current.camera.y
        if (currentY === prevY) {
          stopped = true
          break
        }
        prevY = currentY
      }

      expect(stopped).toBe(true)
    })

    it('respects axis lock during momentum', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      // Swipe mostly vertically to trigger vertical axis lock
      simulateFastSwipe(result.current.touchHandlers, 'up')

      act(() => {
        flushRaf(5)
      })

      // X should not have changed (axis locked to vertical)
      expect(result.current.camera.x).toBe(0)
      expect(result.current.camera.y).toBeGreaterThan(0)
    })

    it('cancels momentum when new touch begins', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      simulateFastSwipe(result.current.touchHandlers, 'up')

      // Let momentum run a few frames
      act(() => {
        flushRaf(3)
      })
      const posBeforeNewTouch = result.current.camera.y

      // Start new touch — should cancel momentum
      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 100 }]) as unknown as TouchEvent
        )
      })

      // Flush more frames — position should not change (momentum cancelled)
      act(() => {
        flushRaf(5)
      })

      expect(result.current.camera.y).toBe(posBeforeNewTouch)
    })

    it('does not start momentum for slow/stationary touchend', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      act(() => {
        result.current.touchHandlers.handleTouchStart(
          createMockTouchEvent([{ clientX: 100, clientY: 100 }]) as unknown as TouchEvent
        )
      })

      // Single slow move (below minimum velocity)
      act(() => {
        result.current.touchHandlers.handleTouchMove(
          createMockTouchEvent([{ clientX: 100, clientY: 100 }]) as unknown as TouchEvent
        )
      })

      act(() => {
        result.current.touchHandlers.handleTouchEnd()
      })

      const posAfterEnd = { ...result.current.camera }

      act(() => {
        flushRaf(5)
      })

      // Position should not change — no momentum started
      expect(result.current.camera).toEqual(posAfterEnd)
    })

    it('cancels momentum on reset()', () => {
      const { result } = renderHook(() => useCamera(defaultContentSize, defaultViewportSize))

      simulateFastSwipe(result.current.touchHandlers, 'up')

      act(() => {
        flushRaf(2)
      })

      act(() => {
        result.current.reset()
      })

      act(() => {
        flushRaf(5)
      })

      // Should be at origin after reset, momentum should not have moved it
      expect(result.current.camera).toEqual({ x: 0, y: 0 })
    })

    it('cancels momentum on unmount', () => {
      const { result, unmount } = renderHook(() =>
        useCamera(defaultContentSize, defaultViewportSize)
      )

      simulateFastSwipe(result.current.touchHandlers, 'up')

      // Unmount should cancel the animation frame
      unmount()

      // Flushing should not throw (callbacks were cancelled)
      expect(() => {
        flushRaf(5)
      }).not.toThrow()
    })

    it('exports momentum constants', () => {
      expect(MOMENTUM_FRICTION).toBe(0.95)
      expect(MOMENTUM_MIN_VELOCITY).toBe(0.5)
      expect(AXIS_LOCK_THRESHOLD).toBe(5)
    })
  })
})

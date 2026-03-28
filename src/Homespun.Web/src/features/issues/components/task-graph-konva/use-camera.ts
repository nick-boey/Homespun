/**
 * Camera/viewport state management hook for Konva canvas.
 *
 * Handles panning, scrolling, and viewport bounds for the task graph canvas.
 */

import { useCallback, useState, useMemo, useRef } from 'react'
import type Konva from 'konva'

/**
 * Camera state representing the viewport position.
 * Values are positive offsets from the top-left corner.
 */
export interface CameraState {
  x: number
  y: number
}

/**
 * Size dimensions for viewport or content.
 */
export interface Size {
  width: number
  height: number
}

/**
 * Clamps a position value to valid scroll bounds.
 *
 * @param position - Current position value
 * @param viewportSize - Viewport dimension (width or height)
 * @param contentSize - Content dimension (width or height)
 * @returns Clamped position value
 */
export function clampPosition(position: number, viewportSize: number, contentSize: number): number {
  // If content fits in viewport, no scrolling needed
  if (contentSize <= viewportSize) {
    return 0
  }

  const maxScroll = contentSize - viewportSize
  return Math.max(0, Math.min(position, maxScroll))
}

/**
 * Hook for managing camera/viewport state for the Konva canvas.
 *
 * @param contentSize - Total size of the content being rendered
 * @param viewportSize - Size of the visible viewport
 * @returns Camera state and control functions
 */
export function useCamera(contentSize: Size, viewportSize: Size) {
  const [rawCamera, setCamera] = useState<CameraState>({ x: 0, y: 0 })
  const dragRef = useRef<{
    startX: number
    startY: number
    lockedAxis: 'x' | 'y' | null
    initialized: boolean
  }>({
    startX: 0,
    startY: 0,
    lockedAxis: null,
    initialized: false,
  })

  // Compute clamped camera based on current viewport/content sizes
  const camera = useMemo(
    () => ({
      x: clampPosition(rawCamera.x, viewportSize.width, contentSize.width),
      y: clampPosition(rawCamera.y, viewportSize.height, contentSize.height),
    }),
    [
      rawCamera.x,
      rawCamera.y,
      viewportSize.width,
      viewportSize.height,
      contentSize.width,
      contentSize.height,
    ]
  )

  /**
   * Pan to an absolute position.
   */
  const panTo = useCallback(
    (x: number, y: number) => {
      setCamera({
        x: clampPosition(x, viewportSize.width, contentSize.width),
        y: clampPosition(y, viewportSize.height, contentSize.height),
      })
    },
    [contentSize.width, contentSize.height, viewportSize.width, viewportSize.height]
  )

  /**
   * Pan by a relative delta.
   */
  const panBy = useCallback(
    (deltaX: number, deltaY: number) => {
      setCamera((prev) => ({
        x: clampPosition(prev.x + deltaX, viewportSize.width, contentSize.width),
        y: clampPosition(prev.y + deltaY, viewportSize.height, contentSize.height),
      }))
    },
    [contentSize.width, contentSize.height, viewportSize.width, viewportSize.height]
  )

  /**
   * Scroll to ensure a specific row is visible.
   *
   * @param rowIndex - Index of the row to scroll to
   * @param rowHeight - Height of each row
   */
  const scrollToRow = useCallback(
    (rowIndex: number, rowHeight: number) => {
      const rowTop = rowIndex * rowHeight
      const rowBottom = rowTop + rowHeight

      setCamera((prev) => {
        const visibleTop = prev.y
        const visibleBottom = prev.y + viewportSize.height

        // If row is above visible area, scroll up
        if (rowTop < visibleTop) {
          return {
            ...prev,
            y: clampPosition(rowTop, viewportSize.height, contentSize.height),
          }
        }

        // If row is below visible area, scroll down
        if (rowBottom > visibleBottom) {
          const newY = rowBottom - viewportSize.height
          return {
            ...prev,
            y: clampPosition(newY, viewportSize.height, contentSize.height),
          }
        }

        // Row is already visible
        return prev
      })
    },
    [contentSize.height, viewportSize.height]
  )

  /**
   * Reset camera to origin.
   */
  const reset = useCallback(() => {
    setCamera({ x: 0, y: 0 })
  }, [])

  /**
   * Handler for Konva Stage drag move event.
   * Constrains drag to a single axis once direction is determined.
   */
  const handleDragMove = useCallback((e: Konva.KonvaEventObject<DragEvent>) => {
    const stage = e.target
    if (!stage || !('x' in stage) || !('y' in stage)) return

    const stageX = typeof stage.x === 'function' ? stage.x() : 0
    const stageY = typeof stage.y === 'function' ? stage.y() : 0
    const drag = dragRef.current

    // Capture stage position at start of drag gesture
    if (!drag.initialized) {
      drag.startX = stageX
      drag.startY = stageY
      drag.initialized = true
      return
    }

    if (drag.lockedAxis === null) {
      const dx = Math.abs(stageX - drag.startX)
      const dy = Math.abs(stageY - drag.startY)
      const threshold = 5

      if (dx >= threshold || dy >= threshold) {
        drag.lockedAxis = dx >= dy ? 'x' : 'y'
      }
    }

    if (drag.lockedAxis === 'x' && typeof stage.y === 'function') {
      stage.y(drag.startY)
    } else if (drag.lockedAxis === 'y' && typeof stage.x === 'function') {
      stage.x(drag.startX)
    }
  }, [])

  /**
   * Handler for Konva Stage drag end event.
   * Updates camera position based on stage position after drag.
   */
  const handleDragEnd = useCallback(
    (e: Konva.KonvaEventObject<DragEvent>) => {
      const stage = e.target
      if (stage && 'x' in stage && 'y' in stage) {
        // Konva stage position is negated (stage moves opposite to view)
        const stageX = typeof stage.x === 'function' ? stage.x() : 0
        const stageY = typeof stage.y === 'function' ? stage.y() : 0

        setCamera({
          x: clampPosition(-stageX, viewportSize.width, contentSize.width),
          y: clampPosition(-stageY, viewportSize.height, contentSize.height),
        })
      }

      // Reset drag axis lock for next gesture
      dragRef.current = { startX: 0, startY: 0, lockedAxis: null, initialized: false }
    },
    [contentSize.width, contentSize.height, viewportSize.width, viewportSize.height]
  )

  /**
   * Handler for wheel events to pan the canvas.
   */
  const handleWheel = useCallback(
    (e: React.WheelEvent) => {
      e.preventDefault()
      if (Math.abs(e.deltaX) >= Math.abs(e.deltaY)) {
        panBy(e.deltaX, 0)
      } else {
        panBy(0, e.deltaY)
      }
    },
    [panBy]
  )

  // Touch panning state
  const touchRef = useRef<{ lastX: number; lastY: number } | null>(null)

  /**
   * Handler for touchstart — records initial touch position for panning.
   * Only tracks single-finger touches (multi-touch is ignored for panning).
   */
  const handleTouchStart = useCallback((e: TouchEvent) => {
    if (e.touches.length === 1) {
      touchRef.current = { lastX: e.touches[0].clientX, lastY: e.touches[0].clientY }
    }
  }, [])

  /**
   * Handler for touchmove — pans the camera by the touch delta.
   * Calls preventDefault to stop the page from scrolling.
   */
  const handleTouchMove = useCallback(
    (e: TouchEvent) => {
      if (e.touches.length === 1 && touchRef.current) {
        e.preventDefault()
        const dx = touchRef.current.lastX - e.touches[0].clientX
        const dy = touchRef.current.lastY - e.touches[0].clientY
        touchRef.current = { lastX: e.touches[0].clientX, lastY: e.touches[0].clientY }
        panBy(dx, dy)
      }
    },
    [panBy]
  )

  /**
   * Handler for touchend — clears touch tracking state.
   */
  const handleTouchEnd = useCallback(() => {
    touchRef.current = null
  }, [])

  const touchHandlers: TouchHandlers = useMemo(
    () => ({ handleTouchStart, handleTouchMove, handleTouchEnd }),
    [handleTouchStart, handleTouchMove, handleTouchEnd]
  )

  return {
    camera,
    panTo,
    panBy,
    scrollToRow,
    reset,
    handleDragMove,
    handleDragEnd,
    handleWheel,
    touchHandlers,
  }
}

/**
 * Touch event handlers returned by useCamera for attaching to the container element.
 */
export interface TouchHandlers {
  handleTouchStart: (e: TouchEvent) => void
  handleTouchMove: (e: TouchEvent) => void
  handleTouchEnd: () => void
}

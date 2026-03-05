import * as React from 'react'
import { cn } from '@/lib/utils'
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'

interface BottomSheetProps {
  children: React.ReactNode
  open: boolean
  onOpenChange: (open: boolean) => void
  title?: string
  className?: string
  heightMode?: 'peek' | 'full'
}

export function BottomSheet({
  children,
  open,
  onOpenChange,
  title,
  className,
  heightMode = 'full',
}: BottomSheetProps) {
  const [translateY, setTranslateY] = React.useState(0)
  const [startY, setStartY] = React.useState(0)
  const [isDragging, setIsDragging] = React.useState(false)
  const contentRef = React.useRef<HTMLDivElement>(null)

  const handleTouchStart = React.useCallback((e: React.TouchEvent) => {
    const touch = e.touches[0]
    setStartY(touch.clientY)
    setIsDragging(true)
  }, [])

  const handleTouchMove = React.useCallback(
    (e: React.TouchEvent) => {
      if (!isDragging) return

      const touch = e.touches[0]
      const deltaY = touch.clientY - startY

      // Only allow dragging down
      if (deltaY > 0) {
        setTranslateY(deltaY)
      }
    },
    [isDragging, startY]
  )

  const handleTouchEnd = React.useCallback(() => {
    setIsDragging(false)

    // If dragged more than 100px down, close the sheet
    if (translateY > 100) {
      onOpenChange(false)
    }

    // Reset transform
    setTranslateY(0)
  }, [translateY, onOpenChange])

  // Reset state when sheet closes
  React.useEffect(() => {
    if (!open) {
      setTranslateY(0)
      setIsDragging(false)
    }
  }, [open])

  const heightClass = heightMode === 'peek' ? 'h-[100px]' : 'h-[80vh]'

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        ref={contentRef}
        side="bottom"
        className={cn(
          'touch-none',
          heightClass,
          className
        )}
        style={{
          transform: `translateY(${translateY}px)`,
          transition: isDragging ? 'none' : 'transform 0.2s ease-out',
        }}
        onTouchStart={handleTouchStart}
        onTouchMove={handleTouchMove}
        onTouchEnd={handleTouchEnd}
      >
        {title && (
          <SheetHeader>
            <SheetTitle>{title}</SheetTitle>
          </SheetHeader>
        )}
        {children}
      </SheetContent>
    </Sheet>
  )
}
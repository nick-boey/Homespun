import { useCallback, useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { ChevronDown } from 'lucide-react'
import { cn } from '@/lib/utils'

export interface ScrollToBottomProps {
  /** Reference to the scrollable container element */
  scrollRef: React.RefObject<HTMLElement | null>
  /** Distance from bottom (in px) before button appears */
  threshold?: number
  /** Additional className for the button */
  className?: string
}

/**
 * A floating button that appears when the user scrolls away from the bottom
 * of a container. Clicking it scrolls the container to the bottom.
 */
export function ScrollToBottom({ scrollRef, threshold = 100, className }: ScrollToBottomProps) {
  const [isVisible, setIsVisible] = useState(false)

  const handleScroll = useCallback(() => {
    const container = scrollRef.current
    if (!container) return

    const { scrollTop, scrollHeight, clientHeight } = container
    const distanceFromBottom = scrollHeight - scrollTop - clientHeight
    setIsVisible(distanceFromBottom > threshold)
  }, [scrollRef, threshold])

  useEffect(() => {
    const container = scrollRef.current
    if (!container) return

    // Check initial scroll position
    handleScroll()

    container.addEventListener('scroll', handleScroll, { passive: true })
    return () => container.removeEventListener('scroll', handleScroll)
  }, [scrollRef, handleScroll])

  const scrollToBottom = useCallback(() => {
    const container = scrollRef.current
    if (!container) return

    container.scrollTo({
      top: container.scrollHeight,
      behavior: 'smooth',
    })
  }, [scrollRef])

  if (!isVisible) return null

  return (
    <Button
      variant="outline"
      size="icon"
      onClick={scrollToBottom}
      className={cn(
        'absolute bottom-4 left-1/2 z-10 h-8 w-8 -translate-x-1/2 rounded-full shadow-md',
        'bg-background/80 backdrop-blur-sm transition-opacity',
        className
      )}
      aria-label="Scroll to bottom"
    >
      <ChevronDown className="h-4 w-4" />
    </Button>
  )
}

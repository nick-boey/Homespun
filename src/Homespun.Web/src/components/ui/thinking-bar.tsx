'use client'

import { cn } from '@/lib/utils'
import { ChevronRight } from 'lucide-react'

type ThinkingBarProps = {
  className?: string
  text?: string
  onStop?: () => void
  stopLabel?: string
  onClick?: () => void
}

function Shimmer({ children, className }: { children: React.ReactNode; className?: string }) {
  return <span className={cn('text-foreground/70 animate-pulse', className)}>{children}</span>
}

export function ThinkingBar({
  className,
  text = 'Thinking',
  onStop,
  stopLabel = 'Answer now',
  onClick,
}: ThinkingBarProps) {
  return (
    <div className={cn('flex w-full items-center justify-between', className)}>
      {onClick ? (
        <button
          type="button"
          onClick={onClick}
          className="flex items-center gap-1 text-sm transition-opacity hover:opacity-80"
        >
          <Shimmer className="font-medium">{text}</Shimmer>
          <ChevronRight className="text-muted-foreground size-4" />
        </button>
      ) : (
        <Shimmer className="cursor-default font-medium">{text}</Shimmer>
      )}
      {onStop ? (
        <button
          onClick={onStop}
          type="button"
          className="text-muted-foreground hover:text-foreground border-muted-foreground/50 hover:border-foreground border-b border-dotted text-sm transition-colors"
        >
          {stopLabel}
        </button>
      ) : null}
    </div>
  )
}

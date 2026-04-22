// Custom: zero-gap pill grouping for adjacent <Button>s — see components/ui/INVENTORY.md.
import * as React from 'react'
import { cn } from '@/lib/utils'

export interface ButtonGroupProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode
}

const ButtonGroup = React.forwardRef<HTMLDivElement, ButtonGroupProps>(
  ({ className, children, ...props }, ref) => {
    return (
      <div
        ref={ref}
        role="group"
        className={cn(
          'flex items-center',
          '[&>*]:rounded-none',
          '[&>*:first-child]:rounded-l-md',
          '[&>*:last-child]:rounded-r-md',
          '[&>*:not(:last-child)]:border-r-0',
          className
        )}
        {...props}
      >
        {children}
      </div>
    )
  }
)
ButtonGroup.displayName = 'ButtonGroup'

export { ButtonGroup }

import { HelpCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
  PopoverHeader,
  PopoverTitle,
} from '@/components/ui/popover'
import { useMobile } from '@/hooks'

export interface FilterHelpPopoverProps {
  /** Custom trigger element (optional, defaults to help icon button) */
  trigger?: React.ReactNode
}

export function FilterHelpPopover({ trigger }: FilterHelpPopoverProps) {
  const isMobile = useMobile()
  const buttonSize = isMobile ? 'icon-touch' : 'icon-sm'

  const defaultTrigger = (
    <Button
      variant="ghost"
      size={buttonSize}
      aria-label="Filter help"
      title="Filter help"
      data-testid="filter-help-button"
    >
      <HelpCircle className="h-4 w-4" />
    </Button>
  )

  return (
    <Popover>
      <PopoverTrigger asChild>{trigger ?? defaultTrigger}</PopoverTrigger>
      <PopoverContent className="w-80" side="bottom" align="end" data-testid="filter-help-content">
        <PopoverHeader>
          <PopoverTitle>Filter Syntax</PopoverTitle>
        </PopoverHeader>
        <div className="mt-3 space-y-3 text-sm">
          <section>
            <h4 className="mb-1 font-medium">Field Filters</h4>
            <ul className="text-muted-foreground space-y-0.5 text-xs">
              <li>
                <code className="bg-muted rounded px-1">status:open</code> - Filter by status
              </li>
              <li>
                <code className="bg-muted rounded px-1">type:bug</code> - Filter by type
              </li>
              <li>
                <code className="bg-muted rounded px-1">priority:1</code> - Filter by priority
              </li>
              <li>
                <code className="bg-muted rounded px-1">assigned:john</code> - Filter by assignee
              </li>
              <li>
                <code className="bg-muted rounded px-1">tag:frontend</code> - Filter by tag
              </li>
              <li>
                <code className="bg-muted rounded px-1">pr:123</code> - Filter by linked PR
              </li>
              <li>
                <code className="bg-muted rounded px-1">id:abc</code> - Filter by issue ID
              </li>
            </ul>
          </section>

          <section>
            <h4 className="mb-1 font-medium">Status Values</h4>
            <p className="text-muted-foreground text-xs">
              draft, open, progress (or inprogress), review, complete (or done), archived, closed
            </p>
          </section>

          <section>
            <h4 className="mb-1 font-medium">Type Values</h4>
            <p className="text-muted-foreground text-xs">
              task, bug (or fix), chore, feature (or feat), idea, verify
            </p>
          </section>

          <section>
            <h4 className="mb-1 font-medium">Negation</h4>
            <p className="text-muted-foreground text-xs">
              Prefix with <code className="bg-muted rounded px-1">-</code> to exclude:{' '}
              <code className="bg-muted rounded px-1">-status:complete</code>
            </p>
          </section>

          <section>
            <h4 className="mb-1 font-medium">Multiple Values</h4>
            <p className="text-muted-foreground text-xs">
              Use comma to match any:{' '}
              <code className="bg-muted rounded px-1">status:open,progress</code>
            </p>
          </section>

          <section>
            <h4 className="mb-1 font-medium">Free Text</h4>
            <p className="text-muted-foreground text-xs">
              Unrecognized words search title, description, tags, and ID
            </p>
          </section>

          <section>
            <h4 className="mb-1 font-medium">Examples</h4>
            <ul className="text-muted-foreground space-y-0.5 text-xs">
              <li>
                <code className="bg-muted rounded px-1">status:open type:bug</code>
              </li>
              <li>
                <code className="bg-muted rounded px-1">-status:complete login</code>
              </li>
              <li>
                <code className="bg-muted rounded px-1">type:bug,feature assigned:alice</code>
              </li>
            </ul>
          </section>
        </div>
      </PopoverContent>
    </Popover>
  )
}

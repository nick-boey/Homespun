import { Link } from '@tanstack/react-router'
import { Menu, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useBreadcrumbs } from '@/hooks/use-breadcrumbs'
import { cn } from '@/lib/utils'
import { ActiveAgentsIndicator } from '@/features/agents'

interface HeaderProps {
  projectId?: string
  onMenuClick?: () => void
  className?: string
}

export function Header({ projectId, onMenuClick, className }: HeaderProps) {
  const { breadcrumbs } = useBreadcrumbs()

  return (
    <header
      className={cn(
        'border-border bg-background flex h-14 items-center gap-4 border-b px-4',
        className
      )}
    >
      {onMenuClick && (
        <Button variant="ghost" size="icon" className="md:hidden" onClick={onMenuClick}>
          <Menu className="h-5 w-5" />
          <span className="sr-only">Toggle menu</span>
        </Button>
      )}

      <nav className="flex flex-1 items-center gap-1 overflow-x-auto">
        {breadcrumbs.map((crumb, index) => (
          <div key={index} className="flex items-center gap-1">
            {index > 0 && <ChevronRight className="text-muted-foreground h-4 w-4 flex-shrink-0" />}
            {crumb.url ? (
              <Link
                to={crumb.url}
                className="text-muted-foreground hover:text-foreground text-sm whitespace-nowrap"
              >
                {crumb.title}
              </Link>
            ) : (
              <span className="text-foreground text-sm font-medium whitespace-nowrap">
                {crumb.title}
              </span>
            )}
          </div>
        ))}
      </nav>

      <div className="flex items-center gap-2">
        {projectId ? (
          <ActiveAgentsIndicator projectId={projectId} />
        ) : (
          <span className="text-muted-foreground text-sm">Select a project</span>
        )}
      </div>
    </header>
  )
}

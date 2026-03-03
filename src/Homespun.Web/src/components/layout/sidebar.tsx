import { Link, useRouterState } from '@tanstack/react-router'
import { FolderKanban, List, Settings, Bot } from 'lucide-react'
import { cn } from '@/lib/utils'

interface NavItemProps {
  to: string
  icon: React.ReactNode
  label: string
  isActive?: boolean
  indent?: boolean
}

function NavItem({ to, icon, label, isActive, indent }: NavItemProps) {
  return (
    <Link
      to={to}
      className={cn(
        'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors',
        'hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
        isActive && 'bg-sidebar-accent text-sidebar-accent-foreground',
        !isActive && 'text-sidebar-foreground',
        indent && 'pl-8'
      )}
    >
      {icon}
      {label}
    </Link>
  )
}

interface SidebarProps {
  className?: string
}

export function Sidebar({ className }: SidebarProps) {
  const routerState = useRouterState()
  const pathname = routerState.location.pathname

  const isActive = (path: string) => {
    if (path === '/') {
      return pathname === '/'
    }
    return pathname.startsWith(path)
  }

  return (
    <aside
      className={cn(
        'border-sidebar-border bg-sidebar flex h-full w-64 flex-col border-r',
        className
      )}
    >
      <div className="border-sidebar-border flex h-14 items-center border-b px-4">
        <Link to="/" className="text-sidebar-foreground flex items-center gap-2 font-semibold">
          <FolderKanban className="h-5 w-5" />
          <span>Homespun</span>
        </Link>
      </div>

      <nav className="flex-1 space-y-1 p-3">
        <div className="mb-2">
          <h3 className="text-sidebar-foreground/60 mb-1 px-3 text-xs font-medium tracking-wider uppercase">
            Projects
          </h3>
          <NavItem
            to="/"
            icon={<List className="h-4 w-4" />}
            label="All Projects"
            isActive={isActive('/') && !pathname.startsWith('/projects')}
          />
        </div>

        <div className="mb-2">
          <div className="border-sidebar-border my-3 border-t" />
          <h3 className="text-sidebar-foreground/60 mb-1 px-3 text-xs font-medium tracking-wider uppercase">
            Global
          </h3>
          <NavItem
            to="/sessions"
            icon={<Bot className="h-4 w-4" />}
            label="Sessions"
            isActive={isActive('/sessions')}
          />
          <NavItem
            to="/settings"
            icon={<Settings className="h-4 w-4" />}
            label="Settings"
            isActive={isActive('/settings')}
          />
        </div>
      </nav>

      <div className="border-sidebar-border border-t p-3">
        <div className="text-sidebar-foreground/60 px-3 text-xs">Homespun v0.1.0</div>
      </div>
    </aside>
  )
}

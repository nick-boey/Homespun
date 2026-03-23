import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { type NavSection } from '@/lib/docs'
import { cn } from '@/lib/utils'

interface SidebarProps {
  sections: NavSection[]
  onNavigate?: () => void
}

export function Sidebar({ sections, onNavigate }: SidebarProps) {
  const location = useLocation()
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})

  const toggleSection = (title: string) => {
    setCollapsed((prev) => ({ ...prev, [title]: !prev[title] }))
  }

  return (
    <nav className="space-y-4" data-testid="sidebar">
      <Link
        to="/"
        onClick={onNavigate}
        className={cn(
          'block rounded-md px-3 py-2 text-sm font-medium',
          location.pathname === '/' || location.pathname === ''
            ? 'bg-accent text-accent-foreground'
            : 'text-muted-foreground hover:bg-accent/50 hover:text-accent-foreground'
        )}
      >
        Home
      </Link>
      {sections.map((section) => {
        const isCollapsed = collapsed[section.title] ?? false
        return (
          <div key={section.title}>
            <button
              onClick={() => toggleSection(section.title)}
              className="text-foreground flex w-full items-center justify-between px-3 py-1.5 text-sm font-semibold"
              aria-expanded={!isCollapsed}
              data-testid={`section-toggle-${section.title}`}
            >
              {section.title}
              {isCollapsed ? (
                <ChevronRight className="h-4 w-4" />
              ) : (
                <ChevronDown className="h-4 w-4" />
              )}
            </button>
            {!isCollapsed && (
              <ul className="mt-1 space-y-1">
                {section.items.map((item) => {
                  const isActive = location.pathname === `/docs/${item.slug}`
                  return (
                    <li key={item.slug}>
                      <Link
                        to={`/docs/${item.slug}`}
                        onClick={onNavigate}
                        className={cn(
                          'block rounded-md px-3 py-1.5 text-sm',
                          isActive
                            ? 'bg-accent text-accent-foreground font-medium'
                            : 'text-muted-foreground hover:bg-accent/50 hover:text-accent-foreground'
                        )}
                      >
                        {item.title}
                      </Link>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        )
      })}
    </nav>
  )
}

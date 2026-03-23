import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import { Menu, X } from 'lucide-react'
import { Sidebar } from './Sidebar'
import { SearchDialog } from './SearchDialog'
import { ThemeToggle } from './ThemeToggle'
import { getNavSections } from '@/lib/docs'
import { cn } from '@/lib/utils'

const sections = getNavSections()

export function Layout() {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)

  return (
    <div className="bg-background min-h-screen">
      <header className="border-border bg-background/95 sticky top-0 z-40 border-b backdrop-blur">
        <div className="mx-auto flex h-14 max-w-7xl items-center gap-4 px-4">
          <button
            className="text-muted-foreground hover:text-foreground lg:hidden"
            onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
            aria-label="Toggle menu"
          >
            {mobileMenuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
          </button>
          <a href="/" className="text-foreground text-lg font-semibold">
            Homespun
          </a>
          <div className="flex-1" />
          <SearchDialog />
          <ThemeToggle />
          <a
            href="https://github.com/Homespun-Software/homespun"
            target="_blank"
            rel="noopener noreferrer"
            className="text-muted-foreground hover:text-foreground"
            aria-label="GitHub"
          >
            <svg className="h-5 w-5" fill="currentColor" viewBox="0 0 24 24">
              <path d="M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.868-.013-1.703-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.07 1.531 1.032 1.531 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.22-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.988 1.029-2.688-.103-.253-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0112 6.844c.85.004 1.705.115 2.504.337 1.909-1.296 2.747-1.027 2.747-1.027.546 1.379.202 2.398.1 2.651.64.7 1.028 1.595 1.028 2.688 0 3.848-2.339 4.695-4.566 4.943.359.309.678.92.678 1.855 0 1.338-.012 2.419-.012 2.747 0 .268.18.58.688.482A10.019 10.019 0 0022 12.017C22 6.484 17.522 2 12 2z" />
            </svg>
          </a>
        </div>
      </header>

      {mobileMenuOpen && (
        <div className="fixed inset-0 top-14 z-30 lg:hidden">
          <div className="fixed inset-0 bg-black/50" onClick={() => setMobileMenuOpen(false)} />
          <div className="border-border bg-background fixed top-14 bottom-0 left-0 w-72 overflow-y-auto border-r p-4">
            <Sidebar sections={sections} onNavigate={() => setMobileMenuOpen(false)} />
          </div>
        </div>
      )}

      <div className="mx-auto max-w-7xl px-4">
        <div className="flex gap-8">
          <aside
            className={cn(
              'hidden lg:block',
              'sticky top-14 h-[calc(100vh-3.5rem)] w-56 shrink-0',
              'overflow-y-auto py-6'
            )}
          >
            <Sidebar sections={sections} />
          </aside>
          <main className="min-w-0 flex-1 py-6">
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  )
}

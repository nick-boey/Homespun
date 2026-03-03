import * as React from 'react'
import { Outlet, useRouterState } from '@tanstack/react-router'
import { Sidebar } from './sidebar'
import { Header } from './header'
import { ErrorBoundary } from '@/components/error-boundary'
import { RouteLoadingFallback } from '@/components/route-loading-fallback'
import { BreadcrumbProvider } from '@/hooks/use-breadcrumbs'
import { useAppStore } from '@/stores/app-store'
import { cn } from '@/lib/utils'
import { NotificationProvider } from '@/features/notifications'

export function RootLayout() {
  const sidebarOpen = useAppStore((state) => state.sidebarOpen)
  const [mobileMenuOpen, setMobileMenuOpen] = React.useState(false)

  // Extract projectId from current route params
  const routerState = useRouterState()
  const pathname = routerState.location.pathname
  const projectId = (pathname.match(/\/projects\/([^/]+)/) ?? [])[1]

  // Close mobile menu when route changes
  React.useEffect(() => {
    setMobileMenuOpen(false)
  }, [pathname])

  const closeMobileMenu = React.useCallback(() => {
    setMobileMenuOpen(false)
  }, [])

  const toggleMobileMenu = React.useCallback(() => {
    setMobileMenuOpen((prev) => !prev)
  }, [])

  return (
    <BreadcrumbProvider>
      <NotificationProvider projectId={projectId}>
        <div className="bg-background flex h-screen overflow-hidden">
          {/* Desktop sidebar */}
          <div
            className={cn(
              'hidden md:flex',
              sidebarOpen ? 'md:w-64' : 'md:w-0',
              'transition-all duration-300'
            )}
          >
            {sidebarOpen && <Sidebar />}
          </div>

          {/* Mobile sidebar overlay */}
          <div
            className={cn(
              'fixed inset-0 z-40 bg-black/50 transition-opacity duration-300 md:hidden',
              mobileMenuOpen ? 'opacity-100' : 'pointer-events-none opacity-0'
            )}
            onClick={closeMobileMenu}
            aria-hidden="true"
          />

          {/* Mobile sidebar drawer */}
          <div
            className={cn(
              'fixed inset-y-0 left-0 z-50 w-64 transform transition-transform duration-300 ease-in-out md:hidden',
              mobileMenuOpen ? 'translate-x-0' : '-translate-x-full'
            )}
          >
            <Sidebar onNavigate={closeMobileMenu} />
          </div>

          {/* Main content */}
          <div className="flex flex-1 flex-col overflow-hidden">
            <Header projectId={projectId} onMenuClick={toggleMobileMenu} />
            <main className="flex-1 overflow-auto p-3 md:p-6">
              <ErrorBoundary>
                <React.Suspense fallback={<RouteLoadingFallback />}>
                  <Outlet />
                </React.Suspense>
              </ErrorBoundary>
            </main>
          </div>
        </div>
      </NotificationProvider>
    </BreadcrumbProvider>
  )
}

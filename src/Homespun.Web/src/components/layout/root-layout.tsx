import * as React from 'react'
import { Outlet, useRouterState } from '@tanstack/react-router'
import { Sidebar } from './sidebar'
import { Header } from './header'
import { ErrorBoundary } from '@/components/error-boundary'
import { RouteLoadingFallback } from '@/components/route-loading-fallback'
import { BreadcrumbProvider } from '@/hooks/use-breadcrumbs'
import { useAppStore } from '@/stores/app-store'
import { cn } from '@/lib/utils'

export function RootLayout() {
  const sidebarOpen = useAppStore((state) => state.sidebarOpen)
  const [mobileMenuOpen, setMobileMenuOpen] = React.useState(false)

  // Extract projectId from current route params
  const routerState = useRouterState()
  const projectId = (routerState.location.pathname.match(/\/projects\/([^/]+)/) ?? [])[1]

  return (
    <BreadcrumbProvider>
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
        {mobileMenuOpen && (
          <>
            <div
              className="fixed inset-0 z-40 bg-black/50 md:hidden"
              onClick={() => setMobileMenuOpen(false)}
            />
            <div className="fixed inset-y-0 left-0 z-50 w-64 md:hidden">
              <Sidebar />
            </div>
          </>
        )}

        {/* Main content */}
        <div className="flex flex-1 flex-col overflow-hidden">
          <Header projectId={projectId} onMenuClick={() => setMobileMenuOpen(!mobileMenuOpen)} />
          <main className="flex-1 overflow-auto p-6">
            <ErrorBoundary>
              <React.Suspense fallback={<RouteLoadingFallback />}>
                <Outlet />
              </React.Suspense>
            </ErrorBoundary>
          </main>
        </div>
      </div>
    </BreadcrumbProvider>
  )
}

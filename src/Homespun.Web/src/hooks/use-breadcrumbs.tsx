import * as React from 'react'

export interface BreadcrumbItem {
  title: string
  url?: string
}

interface BreadcrumbContextValue {
  breadcrumbs: BreadcrumbItem[]
  setBreadcrumbs: (breadcrumbs: BreadcrumbItem[]) => void
}

const BreadcrumbContext = React.createContext<BreadcrumbContextValue | null>(null)

export function BreadcrumbProvider({ children }: { children: React.ReactNode }) {
  const [breadcrumbs, setBreadcrumbs] = React.useState<BreadcrumbItem[]>([])

  const value = React.useMemo(() => ({ breadcrumbs, setBreadcrumbs }), [breadcrumbs])

  return <BreadcrumbContext.Provider value={value}>{children}</BreadcrumbContext.Provider>
}

export function useBreadcrumbs() {
  const context = React.useContext(BreadcrumbContext)
  if (!context) {
    throw new Error('useBreadcrumbs must be used within a BreadcrumbProvider')
  }
  return context
}

export function useBreadcrumbSetter(
  breadcrumbs: BreadcrumbItem[],
  deps: React.DependencyList = []
) {
  const { setBreadcrumbs } = useBreadcrumbs()

  React.useEffect(() => {
    setBreadcrumbs(breadcrumbs)
    return () => setBreadcrumbs([])
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
}

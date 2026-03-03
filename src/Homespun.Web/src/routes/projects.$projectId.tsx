import { createFileRoute, Link, Outlet, useParams } from '@tanstack/react-router'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { cn } from '@/lib/utils'

export const Route = createFileRoute('/projects/$projectId')({
  component: ProjectLayout,
})

const tabs = [
  { label: 'Issues', path: '' },
  { label: 'Branches', path: '/branches' },
  { label: 'Prompts', path: '/prompts' },
  { label: 'Secrets', path: '/secrets' },
  { label: 'Settings', path: '/settings' },
]

function ProjectLayout() {
  const { projectId } = useParams({ from: '/projects/$projectId' })
  const currentPath = Route.useMatch().pathname

  useBreadcrumbSetter(
    [{ title: 'Projects', url: '/' }, { title: `Project ${projectId}` }],
    [projectId]
  )

  const getTabPath = (tabPath: string) => `/projects/${projectId}${tabPath}`
  const isTabActive = (tabPath: string) => {
    const fullPath = getTabPath(tabPath)
    if (tabPath === '') {
      return (
        currentPath === `/projects/${projectId}` || currentPath === `/projects/${projectId}/issues`
      )
    }
    return currentPath.startsWith(fullPath)
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Project {projectId}</h1>
        <p className="text-muted-foreground text-sm">Project details and management</p>
      </div>

      <nav className="border-border flex gap-1 border-b">
        {tabs.map((tab) => (
          <Link
            key={tab.path}
            to={getTabPath(tab.path)}
            className={cn(
              'px-4 py-2 text-sm font-medium transition-colors',
              '-mb-px border-b-2',
              isTabActive(tab.path)
                ? 'border-primary text-foreground'
                : 'text-muted-foreground hover:text-foreground border-transparent'
            )}
          >
            {tab.label}
          </Link>
        ))}
      </nav>

      <Outlet />
    </div>
  )
}

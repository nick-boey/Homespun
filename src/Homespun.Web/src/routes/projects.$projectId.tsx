import { createFileRoute, Link, Outlet, useParams, useNavigate } from '@tanstack/react-router'
import { MoreHorizontal, Pencil, Trash2, Settings, RefreshCw } from 'lucide-react'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { useProject } from '@/features/projects'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'

export const Route = createFileRoute('/projects/$projectId')({
  component: ProjectLayout,
})

const tabs = [
  { label: 'Issues', path: '' },
  { label: 'Pull Requests', path: '/pull-requests' },
  { label: 'Branches', path: '/branches' },
  { label: 'Prompts', path: '/prompts' },
  { label: 'Secrets', path: '/secrets' },
  { label: 'Settings', path: '/settings' },
]

function ProjectLayout() {
  const { projectId } = useParams({ from: '/projects/$projectId' })
  const currentPath = Route.useMatch().pathname
  const navigate = useNavigate()
  const { project, isLoading, isError, refetch } = useProject(projectId)

  const projectName = project?.name ?? `Project ${projectId}`

  useBreadcrumbSetter([{ title: 'Projects', url: '/' }, { title: projectName }], [projectName])

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

  if (isLoading) {
    return (
      <div className="space-y-6" data-testid="project-loading">
        <div className="flex items-start justify-between">
          <div className="space-y-2">
            <Skeleton className="h-8 w-48" />
            <Skeleton className="h-4 w-64" />
          </div>
          <Skeleton className="h-9 w-9" />
        </div>
        <div className="border-border flex gap-1 border-b">
          {tabs.map((tab) => (
            <Skeleton key={tab.path} className="h-10 w-20" />
          ))}
        </div>
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }

  if (isError) {
    return (
      <div className="space-y-6" data-testid="project-not-found">
        <div className="border-border rounded-lg border p-8 text-center">
          <h2 className="text-xl font-semibold">Project Not Found</h2>
          <p className="text-muted-foreground mt-2">
            The project you're looking for doesn't exist or you don't have access to it.
          </p>
          <div className="mt-4 flex justify-center gap-2">
            <Button variant="outline" onClick={() => refetch()}>
              <RefreshCw className="mr-2 h-4 w-4" />
              Try Again
            </Button>
            <Button onClick={() => navigate({ to: '/' })}>Go Home</Button>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold">{projectName}</h1>
          <p className="text-muted-foreground text-sm">Project details and management</p>
        </div>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" aria-label="Project actions">
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem
              onClick={() =>
                navigate({ to: '/projects/$projectId/settings', params: { projectId } })
              }
            >
              <Pencil className="mr-2 h-4 w-4" />
              Edit Project
            </DropdownMenuItem>
            <DropdownMenuItem
              onClick={() =>
                navigate({ to: '/projects/$projectId/settings', params: { projectId } })
              }
            >
              <Settings className="mr-2 h-4 w-4" />
              Settings
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem className="text-destructive focus:text-destructive">
              <Trash2 className="mr-2 h-4 w-4" />
              Delete Project
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <nav className="border-border flex gap-1 border-b" aria-label="Project tabs">
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

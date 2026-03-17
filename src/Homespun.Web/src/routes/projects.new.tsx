import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { useCreateProject } from '@/features/projects'
import { ArrowLeft, Github, FolderGit2 } from 'lucide-react'
import { useState } from 'react'

export const Route = createFileRoute('/projects/new')({
  component: NewProject,
})

const projectSchema = z.object({
  name: z.string().min(1, 'Project name is required'),
  ownerRepo: z.string().optional(),
  defaultBranch: z.string(),
})

type ProjectFormData = z.infer<typeof projectSchema>

export default function NewProject() {
  useBreadcrumbSetter([{ title: 'Projects', url: '/' }, { title: 'New Project' }], [])
  const navigate = useNavigate()
  const [apiError, setApiError] = useState<string | null>(null)
  const [projectType, setProjectType] = useState<'github' | 'local'>('github')

  const form = useForm<ProjectFormData>({
    resolver: zodResolver(projectSchema),
    defaultValues: {
      name: '',
      ownerRepo: '',
      defaultBranch: 'main',
    },
  })

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = form

  const createProject = useCreateProject({
    onSuccess: (project) => {
      navigate({ to: '/projects/$projectId', params: { projectId: project.id! } })
    },
    onError: (error) => {
      setApiError(error.message || 'Failed to create project')
    },
  })

  const onSubmit = (data: ProjectFormData) => {
    setApiError(null)

    // Validate based on project type
    if (projectType === 'github' && !data.ownerRepo) {
      setApiError('Repository is required for GitHub projects')
      return
    }

    if (projectType === 'local' && !/^[a-zA-Z0-9_-]+$/.test(data.name)) {
      setApiError('Project name must use only letters, numbers, hyphens, and underscores')
      return
    }

    createProject.mutate({
      name: data.name,
      ownerRepo: projectType === 'github' ? data.ownerRepo : undefined,
      defaultBranch: data.defaultBranch || 'main',
    })
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link to="/">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h1 className="text-2xl font-semibold">New Project</h1>
      </div>

      <form onSubmit={handleSubmit(onSubmit)} className="max-w-md space-y-6">
        {apiError && (
          <div
            role="alert"
            className="bg-destructive/10 text-destructive border-destructive/20 rounded-md border p-3 text-sm"
          >
            {apiError}
          </div>
        )}

        <Tabs value={projectType} onValueChange={(v) => setProjectType(v as 'github' | 'local')}>
          <TabsList>
            <TabsTrigger value="github">
              <Github className="mr-2 h-4 w-4" />
              GitHub Repository
            </TabsTrigger>
            <TabsTrigger value="local">
              <FolderGit2 className="mr-2 h-4 w-4" />
              Local Project
            </TabsTrigger>
          </TabsList>
        </Tabs>

        <div className="space-y-2">
          <Label htmlFor="name">Project Name</Label>
          <Input
            id="name"
            placeholder="My Project"
            aria-invalid={!!errors.name}
            {...register('name')}
          />
          {errors.name && <p className="text-destructive text-sm">{errors.name.message}</p>}
        </div>

        {projectType === 'github' && (
          <div className="space-y-2">
            <Label htmlFor="ownerRepo">Repository</Label>
            <Input
              id="ownerRepo"
              placeholder="owner/repo or https://github.com/owner/repo"
              aria-invalid={!!errors.ownerRepo}
              {...register('ownerRepo')}
            />
            {errors.ownerRepo && (
              <p className="text-destructive text-sm">{errors.ownerRepo.message}</p>
            )}
            <p className="text-muted-foreground text-sm">
              GitHub repository in owner/repo format or full URL
            </p>
          </div>
        )}

        {projectType === 'local' && (
          <p className="text-muted-foreground text-sm">
            Creates a new local git repository. Project name must use only letters, numbers,
            hyphens, and underscores.
          </p>
        )}

        <div className="space-y-2">
          <Label htmlFor="defaultBranch">Default Branch</Label>
          <Input id="defaultBranch" placeholder="main" {...register('defaultBranch')} />
          <p className="text-muted-foreground text-sm">
            The default branch for new features (optional, defaults to main)
          </p>
        </div>

        <div className="flex gap-3">
          <Button type="submit" disabled={createProject.isPending}>
            {createProject.isPending ? 'Creating...' : 'Create Project'}
          </Button>
          <Button type="button" variant="outline" asChild>
            <Link to="/">Cancel</Link>
          </Button>
        </div>
      </form>
    </div>
  )
}

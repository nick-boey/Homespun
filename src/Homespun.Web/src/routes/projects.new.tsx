import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { useCreateProject } from '@/features/projects'
import { ArrowLeft } from 'lucide-react'
import { useState } from 'react'

export const Route = createFileRoute('/projects/new')({
  component: NewProject,
})

const projectSchema = z.object({
  name: z.string().min(1, 'Project name is required'),
  ownerRepo: z.string().min(1, 'Repository URL or owner/repo is required'),
  defaultBranch: z.string(),
})

type ProjectFormData = z.infer<typeof projectSchema>

export default function NewProject() {
  useBreadcrumbSetter([{ title: 'Projects', url: '/' }, { title: 'New Project' }], [])
  const navigate = useNavigate()
  const [apiError, setApiError] = useState<string | null>(null)

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
    createProject.mutate({
      name: data.name,
      ownerRepo: data.ownerRepo,
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

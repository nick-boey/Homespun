import { useState } from 'react'
import { useNavigate, Link } from '@tanstack/react-router'
import { useCreateProject } from '../hooks/use-projects'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { AlertCircle } from 'lucide-react'

export function ProjectCreationForm() {
  const navigate = useNavigate()
  const createProject = useCreateProject()

  const [ownerRepo, setOwnerRepo] = useState('')
  const [defaultBranch, setDefaultBranch] = useState('main')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [submitError, setSubmitError] = useState<string | null>(null)

  const validate = () => {
    const newErrors: Record<string, string> = {}

    if (!ownerRepo.trim()) {
      newErrors.ownerRepo = 'GitHub repository is required'
    } else if (!ownerRepo.includes('/')) {
      newErrors.ownerRepo = 'Please enter in format: owner/repo'
    }

    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setSubmitError(null)

    if (!validate()) {
      return
    }

    try {
      const project = await createProject.mutateAsync({
        ownerRepo: ownerRepo.trim(),
        defaultBranch: defaultBranch.trim() || 'main',
      })

      navigate({
        to: '/projects/$projectId',
        params: { projectId: project.id! },
      })
    } catch {
      setSubmitError('Failed to create project. Please try again.')
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      {submitError && (
        <div className="bg-destructive/10 text-destructive flex items-center gap-2 rounded-md p-3 text-sm">
          <AlertCircle className="h-4 w-4" />
          <span>{submitError}</span>
        </div>
      )}

      <div className="space-y-2">
        <Label htmlFor="ownerRepo">GitHub Repository</Label>
        <Input
          id="ownerRepo"
          type="text"
          placeholder="owner/repository"
          value={ownerRepo}
          onChange={(e) => {
            setOwnerRepo(e.target.value)
            if (errors.ownerRepo) {
              setErrors((prev) => ({ ...prev, ownerRepo: '' }))
            }
          }}
          aria-invalid={!!errors.ownerRepo}
          aria-describedby={errors.ownerRepo ? 'ownerRepo-error' : undefined}
        />
        {errors.ownerRepo && (
          <p id="ownerRepo-error" className="text-destructive text-sm">
            {errors.ownerRepo}
          </p>
        )}
        <p className="text-muted-foreground text-sm">
          Enter the GitHub repository in the format: owner/repository
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="defaultBranch">Default Branch</Label>
        <Input
          id="defaultBranch"
          type="text"
          placeholder="main"
          value={defaultBranch}
          onChange={(e) => setDefaultBranch(e.target.value)}
        />
        <p className="text-muted-foreground text-sm">
          The default branch to use for new features (defaults to main)
        </p>
      </div>

      <div className="flex gap-4">
        <Button type="submit" disabled={createProject.isPending}>
          {createProject.isPending ? 'Creating...' : 'Create Project'}
        </Button>
        <Button type="button" variant="outline" asChild>
          <Link to="/">Cancel</Link>
        </Button>
      </div>
    </form>
  )
}

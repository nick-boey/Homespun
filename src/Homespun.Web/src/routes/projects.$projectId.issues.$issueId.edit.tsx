import { createFileRoute, useParams, useNavigate, useBlocker, Link } from '@tanstack/react-router'
import { useForm, Controller } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useCallback, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Markdown } from '@/components/ui/markdown'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { useIssue, useUpdateIssue } from '@/features/issues'
import { ArrowLeft } from 'lucide-react'
import type { IssueStatus, IssueType } from '@/api'

export const Route = createFileRoute('/projects/$projectId/issues/$issueId/edit')({
  component: EditIssue,
})

const STATUS_OPTIONS = [
  { value: '0', label: 'Open' },
  { value: '4', label: 'In Progress' },
  { value: '5', label: 'Review' },
  { value: '1', label: 'Complete' },
  { value: '2', label: 'Closed' },
  { value: '3', label: 'Archived' },
]

const TYPE_OPTIONS = [
  { value: '0', label: 'Task' },
  { value: '1', label: 'Feature' },
  { value: '2', label: 'Bug' },
  { value: '3', label: 'Chore' },
]

const PRIORITY_OPTIONS = [
  { value: '1', label: '1 - Highest' },
  { value: '2', label: '2 - High' },
  { value: '3', label: '3 - Medium' },
  { value: '4', label: '4 - Low' },
  { value: '5', label: '5 - Lowest' },
]

const issueSchema = z.object({
  title: z.string().min(1, 'Title is required'),
  description: z.string().optional(),
  status: z.string(),
  type: z.string(),
  priority: z.string().optional(),
  executionMode: z.string(),
  workingBranchId: z.string().optional(),
  tags: z.string().optional(),
})

type IssueFormData = z.infer<typeof issueSchema>

export default function EditIssue() {
  const { projectId, issueId } = useParams({
    from: '/projects/$projectId/issues/$issueId/edit',
  })
  const navigate = useNavigate()
  const [apiError, setApiError] = useState<string | null>(null)
  const [showUnsavedDialog, setShowUnsavedDialog] = useState(false)

  useBreadcrumbSetter(
    [
      { title: 'Projects', url: '/' },
      { title: 'Issues', url: `/projects/${projectId}/issues` },
      { title: 'Edit Issue' },
    ],
    [projectId]
  )

  const { issue, isLoading, isError, error } = useIssue(issueId, projectId)

  const form = useForm<IssueFormData>({
    resolver: zodResolver(issueSchema),
    defaultValues: {
      title: '',
      description: '',
      status: '0',
      type: '0',
      priority: '3',
      executionMode: '0',
      workingBranchId: '',
      tags: '',
    },
  })

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isDirty },
    watch,
  } = form

  // Watch description for preview
  const description = watch('description')

  // Reset form when issue data loads
  useEffect(() => {
    if (issue) {
      reset({
        title: issue.title ?? '',
        description: issue.description ?? '',
        status: String(issue.status ?? 0),
        type: String(issue.type ?? 0),
        priority: issue.priority != null ? String(issue.priority) : '3',
        executionMode: String(issue.executionMode ?? 0),
        workingBranchId: issue.workingBranchId ?? '',
        tags: issue.tags?.join(', ') ?? '',
      })
    }
  }, [issue, reset])

  const updateIssue = useUpdateIssue({
    onSuccess: () => {
      navigate({
        to: '/projects/$projectId/issues',
        params: { projectId },
      })
    },
    onError: (err) => {
      setApiError(err.message || 'Failed to update issue')
    },
  })

  // Navigation blocker for unsaved changes
  const blocker = useBlocker({ condition: isDirty && !updateIssue.isPending })

  // Handle blocked navigation
  useEffect(() => {
    if (blocker.status === 'blocked') {
      setShowUnsavedDialog(true)
    }
  }, [blocker.status])

  const onSubmit = useCallback(
    (data: IssueFormData) => {
      setApiError(null)
      updateIssue.mutate({
        issueId,
        data: {
          projectId,
          title: data.title,
          description: data.description || undefined,
          status: parseInt(data.status) as IssueStatus,
          type: parseInt(data.type) as IssueType,
          priority: data.priority ? parseInt(data.priority) : undefined,
          workingBranchId: data.workingBranchId || undefined,
        },
      })
    },
    [issueId, projectId, updateIssue]
  )

  // Keyboard shortcut for save (Cmd/Ctrl+S)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 's') {
        e.preventDefault()
        handleSubmit(onSubmit)()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [handleSubmit, onSubmit])

  const handleCancel = () => {
    navigate({
      to: '/projects/$projectId/issues',
      params: { projectId },
    })
  }

  const handleDiscardChanges = () => {
    setShowUnsavedDialog(false)
    blocker.proceed?.()
  }

  const handleKeepEditing = () => {
    setShowUnsavedDialog(false)
    blocker.reset?.()
  }

  if (isError) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="ghost" size="icon" asChild>
            <Link to="/projects/$projectId/issues" params={{ projectId }}>
              <ArrowLeft className="h-4 w-4" />
            </Link>
          </Button>
          <h2 className="text-xl font-semibold">Edit Issue</h2>
        </div>
        <div
          role="alert"
          className="bg-destructive/10 text-destructive border-destructive/20 rounded-md border p-4"
        >
          {error?.message || 'Issue not found'}
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link to="/projects/$projectId/issues" params={{ projectId }}>
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h2 className="text-xl font-semibold">Edit Issue</h2>
        {issue && (
          <span className="text-muted-foreground font-mono text-sm">
            {issue.id?.substring(0, 6)}
          </span>
        )}
      </div>

      {isLoading ? (
        <div data-testid="edit-form-skeleton" className="space-y-4">
          <Skeleton className="h-10 w-full max-w-md" />
          <Skeleton className="h-32 w-full max-w-2xl" />
          <Skeleton className="h-10 w-48" />
        </div>
      ) : (
        <form onSubmit={handleSubmit(onSubmit)} className="max-w-2xl space-y-6">
          {apiError && (
            <div
              role="alert"
              className="bg-destructive/10 text-destructive border-destructive/20 rounded-md border p-3 text-sm"
            >
              {apiError}
            </div>
          )}

          {/* Title */}
          <div className="space-y-2">
            <Label htmlFor="title">Title</Label>
            <Input
              id="title"
              placeholder="Issue title"
              aria-invalid={!!errors.title}
              {...register('title')}
            />
            {errors.title && <p className="text-destructive text-sm">{errors.title.message}</p>}
          </div>

          {/* Description with Markdown Preview */}
          <div className="space-y-2">
            <Label htmlFor="description">Description</Label>
            <Tabs defaultValue="edit" className="w-full">
              <TabsList>
                <TabsTrigger value="edit">Edit</TabsTrigger>
                <TabsTrigger value="preview">Preview</TabsTrigger>
              </TabsList>
              <TabsContent value="edit">
                <Textarea
                  id="description"
                  placeholder="Describe the issue (Markdown supported)"
                  className="min-h-[200px] font-mono"
                  {...register('description')}
                />
              </TabsContent>
              <TabsContent value="preview">
                <div className="border-input bg-background min-h-[200px] rounded-md border p-3">
                  {description ? (
                    <Markdown className="prose prose-sm dark:prose-invert max-w-none">
                      {description}
                    </Markdown>
                  ) : (
                    <p className="text-muted-foreground italic">No description</p>
                  )}
                </div>
              </TabsContent>
            </Tabs>
          </div>

          {/* Status and Type Row */}
          <div className="grid grid-cols-2 gap-4">
            {/* Status */}
            <div className="space-y-2">
              <Label htmlFor="status">Status</Label>
              <Controller
                control={control}
                name="status"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="status" className="w-full">
                      <SelectValue placeholder="Select status" />
                    </SelectTrigger>
                    <SelectContent>
                      {STATUS_OPTIONS.map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            {/* Type */}
            <div className="space-y-2">
              <Label htmlFor="type">Type</Label>
              <Controller
                control={control}
                name="type"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="type" className="w-full">
                      <SelectValue placeholder="Select type" />
                    </SelectTrigger>
                    <SelectContent>
                      {TYPE_OPTIONS.map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>
          </div>

          {/* Priority and Execution Mode Row */}
          <div className="grid grid-cols-2 gap-4">
            {/* Priority */}
            <div className="space-y-2">
              <Label htmlFor="priority">Priority</Label>
              <Controller
                control={control}
                name="priority"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="priority" className="w-full">
                      <SelectValue placeholder="Select priority" />
                    </SelectTrigger>
                    <SelectContent>
                      {PRIORITY_OPTIONS.map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            {/* Execution Mode */}
            <div className="space-y-2">
              <Label htmlFor="executionMode">Execution Mode</Label>
              <Controller
                control={control}
                name="executionMode"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="executionMode" className="w-full">
                      <SelectValue placeholder="Select mode" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="0">Series</SelectItem>
                      <SelectItem value="1">Parallel</SelectItem>
                    </SelectContent>
                  </Select>
                )}
              />
            </div>
          </div>

          {/* Working Branch */}
          <div className="space-y-2">
            <Label htmlFor="workingBranchId">Working Branch</Label>
            <Input
              id="workingBranchId"
              placeholder="feature/branch-name"
              {...register('workingBranchId')}
            />
            <p className="text-muted-foreground text-sm">Branch to use for this issue (optional)</p>
          </div>

          {/* Tags */}
          <div className="space-y-2">
            <Label htmlFor="tags">Tags</Label>
            <Input id="tags" placeholder="tag1, tag2, tag3" {...register('tags')} />
            <p className="text-muted-foreground text-sm">Comma-separated list of tags</p>
          </div>

          {/* Action Buttons */}
          <div className="flex gap-3">
            <Button type="submit" disabled={updateIssue.isPending}>
              {updateIssue.isPending ? 'Saving...' : 'Save Changes'}
            </Button>
            <Button type="button" variant="outline" onClick={handleCancel}>
              Cancel
            </Button>
          </div>

          {/* Keyboard shortcut hint */}
          <p className="text-muted-foreground text-xs">
            Press <kbd className="bg-muted rounded px-1 py-0.5">Cmd/Ctrl+S</kbd> to save
          </p>
        </form>
      )}

      {/* Unsaved Changes Dialog */}
      <AlertDialog open={showUnsavedDialog} onOpenChange={setShowUnsavedDialog}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Unsaved Changes</AlertDialogTitle>
            <AlertDialogDescription>
              You have unsaved changes. Are you sure you want to leave this page? Your changes will
              be lost.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel onClick={handleKeepEditing}>Keep Editing</AlertDialogCancel>
            <AlertDialogAction onClick={handleDiscardChanges} variant="destructive">
              Discard Changes
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}

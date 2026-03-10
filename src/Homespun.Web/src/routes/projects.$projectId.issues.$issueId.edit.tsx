import { createFileRoute, useParams, useNavigate, useBlocker, Link } from '@tanstack/react-router'
import { useForm, Controller } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useCallback, useState, useMemo } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Markdown } from '@/components/ui/markdown'
import { Loader } from '@/components/ui/loader'
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
import { AgentLauncherDialog, useGenerateBranchId } from '@/features/agents'
import { ISSUE_STATUS_OPTIONS, ISSUE_TYPE_OPTIONS } from '@/lib/issue-constants'
import { ArrowLeft, Play, Sparkles } from 'lucide-react'
import type { IssueStatus, IssueType, ExecutionMode, IssueResponse } from '@/api'
import { useBranchIdGenerationStore } from '@/features/issues/stores/branch-id-generation-store'

export const Route = createFileRoute('/projects/$projectId/issues/$issueId/edit')({
  component: EditIssue,
})

const PRIORITY_OPTIONS = [
  { value: 'none', label: 'No Priority' },
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

/** Helper to convert issue data to form values for comparison */
const issueToFormValues = (issue: IssueResponse): IssueFormData => ({
  title: issue.title ?? '',
  description: issue.description ?? '',
  status: String(issue.status ?? 0),
  type: String(issue.type ?? 0),
  priority: issue.priority != null ? String(issue.priority) : 'none',
  executionMode: String(issue.executionMode ?? 0),
  workingBranchId: issue.workingBranchId ?? '',
  tags: issue.tags?.join(', ') ?? '',
})

/** Helper to check if form values differ from original */
const hasFormChanges = (currentValues: IssueFormData, originalValues: IssueFormData): boolean => {
  return (
    currentValues.title !== originalValues.title ||
    currentValues.description !== originalValues.description ||
    currentValues.status !== originalValues.status ||
    currentValues.type !== originalValues.type ||
    currentValues.priority !== originalValues.priority ||
    currentValues.executionMode !== originalValues.executionMode ||
    currentValues.workingBranchId !== originalValues.workingBranchId ||
    currentValues.tags !== originalValues.tags
  )
}

/** Props for WorkingBranchField component */
interface WorkingBranchFieldProps {
  register: ReturnType<typeof useForm<IssueFormData>>['register']
  watch: ReturnType<typeof useForm<IssueFormData>>['watch']
  setValue: ReturnType<typeof useForm<IssueFormData>>['setValue']
  issueId: string
}

/** Working Branch field with AI-powered branch ID suggestion button */
function WorkingBranchField({ register, watch, setValue, issueId }: WorkingBranchFieldProps) {
  const title = watch('title')
  const generateBranchId = useGenerateBranchId()
  const isGenerating = useBranchIdGenerationStore((state) => state.isGenerating(issueId))

  const handleSuggest = useCallback(async () => {
    if (!title?.trim()) {
      return
    }
    try {
      const result = await generateBranchId.mutateAsync(title)
      setValue('workingBranchId', result.branchId, { shouldDirty: true })
    } catch {
      // Error is handled by the mutation
    }
  }, [title, generateBranchId, setValue])

  return (
    <div className="space-y-2">
      <Label htmlFor="workingBranchId">Working Branch ID</Label>
      <div className="flex gap-2">
        <div className="relative flex-1">
          <Input
            id="workingBranchId"
            placeholder="Custom branch identifier"
            className="pr-10"
            {...register('workingBranchId')}
          />
          {isGenerating && (
            <div className="absolute inset-y-0 right-0 flex items-center pr-3">
              <Loader variant="circular" size="sm" />
            </div>
          )}
        </div>
        <Button
          type="button"
          variant="outline"
          size="icon"
          onClick={handleSuggest}
          disabled={!title?.trim() || generateBranchId.isPending || isGenerating}
          title="Suggest branch ID based on title"
          aria-label="Suggest branch ID"
        >
          {generateBranchId.isPending ? (
            <Loader variant="circular" size="sm" />
          ) : (
            <Sparkles className="h-4 w-4" />
          )}
        </Button>
      </div>
      <p className="text-muted-foreground text-xs">
        {isGenerating
          ? 'AI is generating branch ID...'
          : 'AI-suggested or auto-generated from title'}
      </p>
    </div>
  )
}

export default function EditIssue() {
  const { projectId, issueId } = useParams({
    from: '/projects/$projectId/issues/$issueId/edit',
  })
  const navigate = useNavigate()
  const [apiError, setApiError] = useState<string | null>(null)
  const [showUnsavedDialog, setShowUnsavedDialog] = useState(false)
  const [agentLauncherOpen, setAgentLauncherOpen] = useState(false)

  // Listen for branch ID generation events
  // useBranchIdGenerationEvents(projectId)

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
      priority: 'none',
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
    formState: { errors },
    watch,
  } = form

  // Watch all form values for change detection
  const formValues = watch()

  // Watch description separately for preview (derived from formValues)
  const description = formValues.description

  // Compute whether form has actual changes compared to original issue
  const hasActualChanges = useMemo(() => {
    if (!issue) return false
    const originalValues = issueToFormValues(issue)
    return hasFormChanges(formValues, originalValues)
  }, [issue, formValues])

  // Reset form when issue data loads - use useEffect with proper dependencies
  useEffect(() => {
    if (issue) {
      reset(issueToFormValues(issue))
    }
  }, [issue, reset])

  // Create separate mutations for save and save&run
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

  const updateIssueAndRun = useUpdateIssue({
    onSuccess: () => {
      setAgentLauncherOpen(true)
    },
    onError: (err) => {
      setApiError(err.message || 'Failed to update issue')
    },
  })

  // Navigation blocker for unsaved changes - only block if actual changes exist
  const blocker = useBlocker({
    condition: hasActualChanges && !updateIssue.isPending && !updateIssueAndRun.isPending,
  })

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
          priority: data.priority && data.priority !== 'none' ? parseInt(data.priority) : undefined,
          executionMode: parseInt(data.executionMode) as ExecutionMode,
          workingBranchId: data.workingBranchId || undefined,
        },
      })
    },
    [issueId, projectId, updateIssue]
  )

  // Handler for save and run agent
  const handleSaveAndRunAgent = useCallback(async () => {
    // Trigger form validation and get values
    const isValid = await form.trigger()
    if (!isValid) return

    const data = form.getValues()
    setApiError(null)

    // Use the save & run mutation
    updateIssueAndRun.mutate({
      issueId,
      data: {
        projectId,
        title: data.title,
        description: data.description || undefined,
        status: parseInt(data.status) as IssueStatus,
        type: parseInt(data.type) as IssueType,
        priority: data.priority && data.priority !== 'none' ? parseInt(data.priority) : undefined,
        executionMode: parseInt(data.executionMode) as ExecutionMode,
        workingBranchId: data.workingBranchId || undefined,
      },
    })
  }, [form, issueId, projectId, updateIssueAndRun])

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
                  <Select
                    key={`status-${field.value}`}
                    value={field.value}
                    onValueChange={field.onChange}
                  >
                    <SelectTrigger id="status" className="w-full">
                      <SelectValue placeholder="Select status" />
                    </SelectTrigger>
                    <SelectContent>
                      {ISSUE_STATUS_OPTIONS.map((option) => (
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
                  <Select
                    key={`type-${field.value}`}
                    value={field.value}
                    onValueChange={field.onChange}
                  >
                    <SelectTrigger id="type" className="w-full">
                      <SelectValue placeholder="Select type" />
                    </SelectTrigger>
                    <SelectContent>
                      {ISSUE_TYPE_OPTIONS.map((option) => (
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
                  <Select
                    key={`priority-${field.value}`}
                    value={field.value}
                    onValueChange={field.onChange}
                  >
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
                  <Select
                    key={`executionMode-${field.value}`}
                    value={field.value}
                    onValueChange={field.onChange}
                  >
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
          <WorkingBranchField
            register={register}
            watch={watch}
            setValue={form.setValue}
            issueId={issueId}
          />

          {/* Tags */}
          <div className="space-y-2">
            <Label htmlFor="tags">Tags</Label>
            <Input id="tags" placeholder="tag1, tag2, tag3" {...register('tags')} />
            <p className="text-muted-foreground text-sm">Comma-separated list of tags</p>
          </div>

          {/* Action Buttons */}
          <div className="flex gap-3">
            <Button type="submit" disabled={updateIssue.isPending || updateIssueAndRun.isPending}>
              {updateIssue.isPending ? 'Saving...' : 'Save Changes'}
            </Button>
            <Button
              type="button"
              variant="secondary"
              disabled={updateIssue.isPending || updateIssueAndRun.isPending}
              onClick={handleSaveAndRunAgent}
              className="gap-1.5"
            >
              <Play className="h-3.5 w-3.5" />
              Save & Run Agent
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

      {/* Agent Launcher Dialog */}
      <AgentLauncherDialog
        open={agentLauncherOpen}
        onOpenChange={setAgentLauncherOpen}
        projectId={projectId}
        issueId={issueId}
      />
    </div>
  )
}

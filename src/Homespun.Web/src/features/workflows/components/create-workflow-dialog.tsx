import { useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  useCreateWorkflow,
  useWorkflowTemplates,
  useCreateFromTemplate,
} from '../hooks/use-workflows'

export interface CreateWorkflowDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  projectId: string
}

export function CreateWorkflowDialog({ open, onOpenChange, projectId }: CreateWorkflowDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        {open && <CreateWorkflowDialogContent projectId={projectId} onOpenChange={onOpenChange} />}
      </DialogContent>
    </Dialog>
  )
}

function CreateWorkflowDialogContent({
  projectId,
  onOpenChange,
}: {
  projectId: string
  onOpenChange: (open: boolean) => void
}) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)
  const navigate = useNavigate()
  const createWorkflow = useCreateWorkflow(projectId)
  const createFromTemplate = useCreateFromTemplate(projectId)
  const { templates, isLoading: templatesLoading } = useWorkflowTemplates()

  const isPending = createWorkflow.isPending || createFromTemplate.isPending

  const handleBlankCreate = async () => {
    const trimmedTitle = title.trim()
    if (!trimmedTitle) {
      setError('Title is required')
      return
    }
    setError(null)

    try {
      const workflow = await createWorkflow.mutateAsync({
        title: trimmedTitle,
        description: description.trim() || undefined,
      })
      onOpenChange(false)
      navigate({
        to: '/projects/$projectId/workflows/$workflowId',
        params: { projectId, workflowId: workflow.id! },
      })
    } catch {
      // Error handled by mutation
    }
  }

  const handleTemplateCreate = async (templateId: string) => {
    try {
      const workflow = await createFromTemplate.mutateAsync(templateId)
      onOpenChange(false)
      navigate({
        to: '/projects/$projectId/workflows/$workflowId',
        params: { projectId, workflowId: workflow.id! },
      })
    } catch {
      // Error handled by mutation
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Create Workflow</DialogTitle>
        <DialogDescription>Create a new workflow from scratch or use a template.</DialogDescription>
      </DialogHeader>

      <Tabs defaultValue="blank">
        <TabsList>
          <TabsTrigger value="blank">Blank</TabsTrigger>
          <TabsTrigger value="template">From Template</TabsTrigger>
        </TabsList>

        <TabsContent value="blank" className="space-y-4 pt-2">
          <div className="space-y-2">
            <Label htmlFor="workflow-title">Title</Label>
            <Input
              id="workflow-title"
              value={title}
              onChange={(e) => {
                setTitle(e.target.value)
                if (error) setError(null)
              }}
              placeholder="Workflow title"
            />
            {error && <p className="text-destructive text-sm">{error}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="workflow-description">Description</Label>
            <Textarea
              id="workflow-description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Optional description"
              rows={3}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
              Cancel
            </Button>
            <Button onClick={handleBlankCreate} disabled={isPending}>
              {createWorkflow.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Create
            </Button>
          </DialogFooter>
        </TabsContent>

        <TabsContent value="template" className="pt-2">
          {templatesLoading ? (
            <div className="flex justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin" />
            </div>
          ) : templates.length === 0 ? (
            <p className="text-muted-foreground py-8 text-center">No templates available.</p>
          ) : (
            <div className="space-y-2">
              {templates.map((template) => (
                <button
                  key={template.id}
                  className="border-border hover:bg-accent w-full rounded-lg border p-3 text-left transition-colors"
                  onClick={() => handleTemplateCreate(template.id!)}
                  disabled={isPending}
                >
                  <div className="font-medium">{template.title}</div>
                  {template.description && (
                    <p className="text-muted-foreground mt-1 text-sm">{template.description}</p>
                  )}
                  <p className="text-muted-foreground mt-1 text-xs">
                    {template.stepCount} {template.stepCount === 1 ? 'step' : 'steps'}
                  </p>
                </button>
              ))}
            </div>
          )}
        </TabsContent>
      </Tabs>
    </>
  )
}

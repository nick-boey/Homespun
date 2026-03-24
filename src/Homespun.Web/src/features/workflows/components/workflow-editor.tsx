import { useState, useCallback } from 'react'
import { ArrowUp, ArrowDown, Plus, Trash2, Save, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
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
import { WorkflowMermaidChart } from './workflow-mermaid-chart'
import { StepSettingsCard } from './step-settings-card'
import { useUpdateWorkflow } from '../hooks/use-workflows'
import { useMergedProjectPrompts } from '@/features/prompts/hooks'
import type { WorkflowStep } from '@/api/generated/types.gen'

interface WorkflowEditorProps {
  workflowId: string
  projectId: string
  initialSteps: WorkflowStep[]
}

function generateStepId(): string {
  return `step-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`
}

function createDefaultStep(type: WorkflowStep['stepType'] = 'agent'): WorkflowStep {
  return {
    id: generateStepId(),
    name: 'New Step',
    stepType: type,
    prompt: null,
    promptId: null,
    sessionMode: 'build',
    onSuccess: { type: 'nextStep' },
    onFailure: { type: 'exit' },
    maxRetries: 0,
    retryDelaySeconds: 30,
    condition: null,
  }
}

export function WorkflowEditor({ workflowId, projectId, initialSteps }: WorkflowEditorProps) {
  const [steps, setSteps] = useState<WorkflowStep[]>(initialSteps)
  const [selectedStepId, setSelectedStepId] = useState<string | null>(null)
  const [stepToRemove, setStepToRemove] = useState<string | null>(null)

  const updateMutation = useUpdateWorkflow(projectId)
  const { data: prompts } = useMergedProjectPrompts(projectId)

  const selectedStep = steps.find((s) => s.id === selectedStepId) ?? null
  const selectedIndex = selectedStep ? steps.indexOf(selectedStep) : -1

  const handleStepChange = useCallback((updated: WorkflowStep) => {
    setSteps((prev) => prev.map((s) => (s.id === updated.id ? updated : s)))
  }, [])

  function handleAddStep() {
    const newStep = createDefaultStep()
    setSteps((prev) => [...prev, newStep])
    setSelectedStepId(newStep.id)
  }

  function handleRemoveStep(stepId: string) {
    setSteps((prev) => prev.filter((s) => s.id !== stepId))
    if (selectedStepId === stepId) {
      setSelectedStepId(null)
    }
    setStepToRemove(null)
  }

  function handleMoveStep(direction: 'up' | 'down') {
    if (selectedIndex < 0) return
    const newIndex = direction === 'up' ? selectedIndex - 1 : selectedIndex + 1
    if (newIndex < 0 || newIndex >= steps.length) return

    setSteps((prev) => {
      const next = [...prev]
      const [moved] = next.splice(selectedIndex, 1)
      next.splice(newIndex, 0, moved)
      return next
    })
  }

  function handleSave() {
    updateMutation.mutate({
      workflowId,
      request: {
        projectId,
        steps,
      },
    })
  }

  function handleStepClick(stepId: string) {
    setSelectedStepId(stepId === selectedStepId ? null : stepId)
  }

  return (
    <div className="space-y-4">
      <div className="flex gap-4">
        {/* Left panel: Step list */}
        <div className="w-64 shrink-0 space-y-2">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-medium">Steps</h3>
            <Button
              data-testid="add-step-button"
              variant="outline"
              size="sm"
              onClick={handleAddStep}
            >
              <Plus className="mr-1 h-3 w-3" />
              Add
            </Button>
          </div>

          <div className="space-y-1" data-testid="step-list">
            {steps.map((step) => (
              <button
                key={step.id}
                data-testid={`step-item-${step.id}`}
                className={`flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-sm transition-colors ${
                  selectedStepId === step.id ? 'bg-accent text-accent-foreground' : 'hover:bg-muted'
                }`}
                onClick={() => handleStepClick(step.id!)}
              >
                <Badge variant="outline" className="shrink-0 text-xs">
                  {step.stepType}
                </Badge>
                <span className="truncate">{step.name}</span>
              </button>
            ))}
          </div>

          {selectedStep && (
            <div className="flex gap-1">
              <Button
                data-testid="move-step-up"
                variant="outline"
                size="sm"
                disabled={selectedIndex <= 0}
                onClick={() => handleMoveStep('up')}
              >
                <ArrowUp className="h-3 w-3" />
              </Button>
              <Button
                data-testid="move-step-down"
                variant="outline"
                size="sm"
                disabled={selectedIndex >= steps.length - 1}
                onClick={() => handleMoveStep('down')}
              >
                <ArrowDown className="h-3 w-3" />
              </Button>
              <Button
                data-testid="remove-step-button"
                variant="outline"
                size="sm"
                onClick={() => setStepToRemove(selectedStepId)}
              >
                <Trash2 className="h-3 w-3" />
              </Button>
            </div>
          )}
        </div>

        {/* Right panel: Step settings */}
        <div className="min-w-0 flex-1">
          {selectedStep ? (
            <StepSettingsCard
              step={selectedStep}
              allSteps={steps}
              prompts={prompts ?? []}
              projectId={projectId}
              onChange={handleStepChange}
            />
          ) : (
            <div className="border-border flex h-48 items-center justify-center rounded-lg border">
              <p className="text-muted-foreground text-sm">Select a step to edit its settings</p>
            </div>
          )}
        </div>
      </div>

      {/* Bottom: Mermaid chart */}
      <WorkflowMermaidChart steps={steps} onStepClick={handleStepClick} />

      {/* Save button */}
      <div className="flex justify-end">
        <Button
          data-testid="save-workflow-button"
          onClick={handleSave}
          disabled={updateMutation.isPending}
        >
          {updateMutation.isPending ? (
            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
          ) : (
            <Save className="mr-2 h-4 w-4" />
          )}
          Save
        </Button>
      </div>

      {/* Remove confirmation dialog */}
      <AlertDialog
        open={stepToRemove !== null}
        onOpenChange={(open) => !open && setStepToRemove(null)}
      >
        <AlertDialogContent data-testid="confirm-remove-dialog">
          <AlertDialogHeader>
            <AlertDialogTitle>Remove Step</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to remove this step? This action cannot be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              data-testid="confirm-remove-button"
              onClick={() => stepToRemove && handleRemoveStep(stepToRemove)}
            >
              Remove
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}

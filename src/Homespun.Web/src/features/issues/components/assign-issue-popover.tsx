import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { AssigneeCombobox } from './assignee-combobox'
import { useUpdateIssue } from '../hooks/use-update-issue'
import { useIssue } from '../hooks/use-issue'

export interface AssignIssueDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  projectId: string
  issueId: string
}

/**
 * Dialog for quickly assigning an issue to a user from the toolbar.
 */
export function AssignIssueDialog({
  open,
  onOpenChange,
  projectId,
  issueId,
}: AssignIssueDialogProps) {
  const { issue } = useIssue(issueId, projectId)
  const updateIssue = useUpdateIssue({
    onSuccess: () => {
      onOpenChange(false)
    },
  })

  const handleAssigneeChange = (assignee: string | null) => {
    updateIssue.mutate({
      issueId,
      data: {
        projectId,
        assignedTo: assignee || undefined,
      },
    })
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle>Assign Issue</DialogTitle>
          <DialogDescription>Select a user to assign this issue to.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4 pt-2">
          <AssigneeCombobox
            projectId={projectId}
            value={issue?.assignedTo ?? null}
            onChange={handleAssigneeChange}
            disabled={updateIssue.isPending}
          />
          {updateIssue.isError && (
            <p className="text-destructive text-sm">
              {updateIssue.error?.message || 'Failed to update assignee'}
            </p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

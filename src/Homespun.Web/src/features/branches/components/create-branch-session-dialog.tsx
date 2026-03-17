import { useState, useCallback } from 'react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  useCreateBranchSession,
  type CreateBranchSessionResult,
} from '../hooks/use-create-branch-session'

export interface CreateBranchSessionDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  projectId: string
  onSessionCreated?: (result: CreateBranchSessionResult) => void
  onError?: (error: Error) => void
}

// Git branch name validation
// Invalid characters: space, ~, ^, :, ?, *, [, \, @{, ..
const INVALID_BRANCH_CHARS = /[\s~^:?*[\]\\]|@\{|\.{2,}/
const INVALID_START_END = /^[./]|[./]$/

function validateBranchName(name: string): string | null {
  if (!name || !name.trim()) {
    return 'Branch name is required'
  }

  const trimmed = name.trim()

  if (INVALID_BRANCH_CHARS.test(trimmed)) {
    return 'Branch name contains invalid characters'
  }

  if (INVALID_START_END.test(trimmed)) {
    return 'Branch name cannot start or end with a dot or slash'
  }

  return null
}

function DialogContentInner({
  onOpenChange,
  projectId,
  onSessionCreated,
  onError,
}: Omit<CreateBranchSessionDialogProps, 'open'>) {
  // Initialize state with default values - no need for useEffect reset
  // since this component only renders when dialog is open
  const [branchName, setBranchName] = useState('')
  const [validationError, setValidationError] = useState<string | null>(null)

  const createBranchSession = useCreateBranchSession()

  const handleBranchNameChange = useCallback(
    (value: string) => {
      setBranchName(value)
      if (validationError) {
        setValidationError(validateBranchName(value))
      }
    },
    [validationError]
  )

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault()

      const error = validateBranchName(branchName)
      if (error) {
        setValidationError(error)
        return
      }

      try {
        const result = await createBranchSession.mutateAsync({
          projectId,
          branchName: branchName.trim(),
        })

        onSessionCreated?.(result)
        onOpenChange(false)
      } catch (err) {
        const error = err instanceof Error ? err : new Error('Failed to create session')
        onError?.(error)
      }
    },
    [branchName, projectId, createBranchSession, onSessionCreated, onOpenChange, onError]
  )

  const handleCancel = useCallback(() => {
    onOpenChange(false)
  }, [onOpenChange])

  const isSubmitting = createBranchSession.isPending

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>New Session</DialogTitle>
        <DialogDescription>
          Create a new agent session on a branch. The session will start in Plan mode.
        </DialogDescription>
      </DialogHeader>

      <form onSubmit={handleSubmit} className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="branch-name">Branch Name</Label>
          <Input
            id="branch-name"
            type="text"
            value={branchName}
            onChange={(e) => handleBranchNameChange(e.target.value)}
            placeholder="feature/my-branch"
            className="font-mono"
            autoComplete="off"
            autoFocus
            disabled={isSubmitting}
          />
          {validationError && <p className="text-destructive text-sm">{validationError}</p>}
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={handleCancel} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Creating...' : 'OK'}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  )
}

export function CreateBranchSessionDialog({
  open,
  onOpenChange,
  projectId,
  onSessionCreated,
  onError,
}: CreateBranchSessionDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {open && (
        <DialogContentInner
          onOpenChange={onOpenChange}
          projectId={projectId}
          onSessionCreated={onSessionCreated}
          onError={onError}
        />
      )}
    </Dialog>
  )
}

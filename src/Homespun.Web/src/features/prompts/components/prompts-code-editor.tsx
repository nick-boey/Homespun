import { useState, useEffect, useMemo } from 'react'
import { Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
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
import {
  serializePrompts,
  parsePrompts,
  calculateDiff,
  type PromptChanges,
} from '../utils/prompt-diff'
import type { AgentPrompt } from '@/api/generated/types.gen'

export interface PromptsCodeEditorProps {
  prompts: AgentPrompt[]
  onApply: (changes: PromptChanges) => Promise<void>
  isApplying: boolean
  /** IDs of global prompts that cannot be deleted from the project page */
  globalPromptIds?: string[]
}

export function PromptsCodeEditor({
  prompts,
  onApply,
  isApplying,
  globalPromptIds,
}: PromptsCodeEditorProps) {
  const originalJson = useMemo(() => serializePrompts(prompts), [prompts])
  const [editedJson, setEditedJson] = useState(originalJson)
  const [error, setError] = useState<string | null>(null)
  const [pendingChanges, setPendingChanges] = useState<PromptChanges | null>(null)
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)

  // Update editedJson when prompts change (e.g., after successful apply)
  useEffect(() => {
    setEditedJson(originalJson)
  }, [originalJson])

  const handleChange = (value: string) => {
    setEditedJson(value)
    // Clear error when user starts editing
    if (error) {
      setError(null)
    }
  }

  const handleApply = async () => {
    // Parse the edited JSON
    const parseResult = parsePrompts(editedJson)
    if (!parseResult.success) {
      setError(parseResult.error)
      return
    }

    // Calculate the diff
    const currentParsed = parsePrompts(originalJson)
    if (!currentParsed.success) {
      // This shouldn't happen since we serialized it ourselves
      setError('Internal error: could not parse original prompts')
      return
    }

    const changes = calculateDiff(currentParsed.data, parseResult.data)

    // Check if any global prompts are being deleted (not allowed on project page)
    if (globalPromptIds && globalPromptIds.length > 0) {
      const globalDeletes = changes.deletes.filter((id) => globalPromptIds.includes(id))
      if (globalDeletes.length > 0) {
        setError(
          'Cannot delete global prompts from the project page. Remove them from the global prompts page instead.'
        )
        return
      }
    }

    // If there are deletes, show confirmation dialog
    if (changes.deletes.length > 0) {
      setPendingChanges(changes)
      setShowDeleteConfirm(true)
      return
    }

    // Otherwise, apply directly
    await applyChanges(changes)
  }

  const applyChanges = async (changes: PromptChanges) => {
    try {
      await onApply(changes)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to apply changes')
    }
  }

  const handleConfirmDelete = async () => {
    if (pendingChanges) {
      await applyChanges(pendingChanges)
    }
    setShowDeleteConfirm(false)
    setPendingChanges(null)
  }

  const handleCancelDelete = () => {
    setShowDeleteConfirm(false)
    setPendingChanges(null)
  }

  const handleRevert = () => {
    setEditedJson(originalJson)
    setError(null)
  }

  return (
    <div className="space-y-4">
      <Textarea
        className="min-h-[400px] font-mono text-sm"
        value={editedJson}
        onChange={(e) => handleChange(e.target.value)}
        placeholder="[]"
      />

      {error && (
        <div className="text-destructive bg-destructive/10 rounded-md px-3 py-2 text-sm">
          {error}
        </div>
      )}

      <div className="flex justify-end gap-2">
        <Button variant="outline" onClick={handleRevert} disabled={isApplying}>
          Revert
        </Button>
        <Button onClick={handleApply} disabled={isApplying}>
          {isApplying ? (
            <>
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              Applying...
            </>
          ) : (
            'Apply'
          )}
        </Button>
      </div>

      <AlertDialog open={showDeleteConfirm} onOpenChange={setShowDeleteConfirm}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Confirm Deletion</AlertDialogTitle>
            <AlertDialogDescription>
              This will delete {pendingChanges?.deletes.length ?? 0} prompt(s). This action cannot
              be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel onClick={handleCancelDelete}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleConfirmDelete}>Confirm</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}

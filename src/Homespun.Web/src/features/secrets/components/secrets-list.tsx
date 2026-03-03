import { useState } from 'react'
import { Plus, AlertCircle, ShieldAlert } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useSecrets, useCreateSecret, useUpdateSecret, useDeleteSecret } from '../hooks'
import { SecretsEmptyState } from './secrets-empty-state'
import { SecretRow } from './secret-row'
import { SecretFormDialog } from './secret-form-dialog'

export interface SecretsListProps {
  projectId: string
}

export function SecretsList({ projectId }: SecretsListProps) {
  const { secrets, isLoading, isError, error } = useSecrets(projectId)
  const createSecret = useCreateSecret()
  const updateSecret = useUpdateSecret()
  const deleteSecret = useDeleteSecret()

  const [isAddDialogOpen, setIsAddDialogOpen] = useState(false)
  const [editingSecret, setEditingSecret] = useState<string | null>(null)
  const [deletingSecret, setDeletingSecret] = useState<string | null>(null)

  const handleCreate = async (data: { name?: string; value: string }) => {
    if (!data.name) return

    await createSecret.mutateAsync({
      projectId,
      name: data.name,
      value: data.value,
    })
    setIsAddDialogOpen(false)
  }

  const handleUpdate = async (data: { value: string }) => {
    if (!editingSecret) return

    await updateSecret.mutateAsync({
      projectId,
      name: editingSecret,
      value: data.value,
    })
    setEditingSecret(null)
  }

  const handleDelete = async (name: string) => {
    setDeletingSecret(name)
    try {
      await deleteSecret.mutateAsync({
        projectId,
        name,
      })
    } finally {
      setDeletingSecret(null)
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">Secrets</h2>
          <p className="text-muted-foreground text-sm">
            Manage environment variables for this project.
          </p>
        </div>
        <Button onClick={() => setIsAddDialogOpen(true)}>
          <Plus className="mr-2 h-4 w-4" />
          Add Secret
        </Button>
      </div>

      {/* Security notice */}
      <div className="bg-muted/50 flex items-start gap-3 rounded-lg p-4">
        <ShieldAlert className="text-muted-foreground mt-0.5 h-5 w-5 flex-shrink-0" />
        <div>
          <p className="text-sm font-medium">Security Notice</p>
          <p className="text-muted-foreground text-sm">
            Secret values are never displayed after they are saved. You can only replace existing
            values.
          </p>
        </div>
      </div>

      {/* Loading state */}
      {isLoading && (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => (
            <div
              key={i}
              className="border-border flex items-center gap-4 rounded-lg border px-4 py-3"
            >
              <Skeleton className="h-5 w-32" />
              <Skeleton className="h-4 w-24" />
              <div className="flex-1" />
              <Skeleton className="h-8 w-8" />
              <Skeleton className="h-8 w-8" />
            </div>
          ))}
        </div>
      )}

      {/* Error state */}
      {isError && (
        <div className="border-destructive bg-destructive/10 flex items-start gap-3 rounded-lg border p-4">
          <AlertCircle className="text-destructive mt-0.5 h-5 w-5 flex-shrink-0" />
          <div>
            <p className="text-destructive text-sm font-medium">Failed to fetch secrets</p>
            <p className="text-destructive/80 text-sm">{error?.message}</p>
          </div>
        </div>
      )}

      {/* Empty state */}
      {!isLoading && !isError && secrets.length === 0 && <SecretsEmptyState />}

      {/* Secrets list */}
      {!isLoading && !isError && secrets.length > 0 && (
        <div className="space-y-3">
          {secrets.map((secret) => (
            <SecretRow
              key={secret.name}
              secret={secret}
              onEdit={() => setEditingSecret(secret.name!)}
              onDelete={() => handleDelete(secret.name!)}
              isDeleting={deletingSecret === secret.name}
            />
          ))}
        </div>
      )}

      {/* Add secret dialog */}
      <SecretFormDialog
        open={isAddDialogOpen}
        onOpenChange={setIsAddDialogOpen}
        onSubmit={handleCreate}
        isSubmitting={createSecret.isPending}
        mode="create"
      />

      {/* Edit secret dialog */}
      <SecretFormDialog
        open={!!editingSecret}
        onOpenChange={(open) => !open && setEditingSecret(null)}
        onSubmit={handleUpdate}
        isSubmitting={updateSecret.isPending}
        mode="edit"
        secretName={editingSecret ?? undefined}
      />
    </div>
  )
}

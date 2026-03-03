import { useState } from 'react'
import { Plus, Trash2, Key, Clock, Pencil, EyeOff } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
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
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useSecrets, useDeleteSecret } from '../hooks'
import { SecretDialog } from './secret-dialog'
import type { SecretInfo } from '@/api/generated/types.gen'

interface SecretsListProps {
  projectId: string
}

export function SecretsList({ projectId }: SecretsListProps) {
  const { secrets, isLoading, isError } = useSecrets(projectId)
  const deleteSecret = useDeleteSecret(projectId)

  const [isCreating, setIsCreating] = useState(false)
  const [editingSecret, setEditingSecret] = useState<string | null>(null)
  const [deletingSecret, setDeletingSecret] = useState<SecretInfo | null>(null)

  const handleDelete = async () => {
    if (!deletingSecret?.name) return
    await deleteSecret.mutateAsync(deletingSecret.name)
    setDeletingSecret(null)
  }

  if (isLoading) {
    return <SecretsListSkeleton />
  }

  if (isError) {
    return (
      <div className="border-border rounded-lg border p-8 text-center">
        <p className="text-muted-foreground">
          Unable to load secrets. Please try refreshing the page.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">Environment Secrets</h2>
          <p className="text-muted-foreground text-sm">
            Manage environment variables that are injected into agent containers.
          </p>
        </div>
        <Button onClick={() => setIsCreating(true)} className="gap-1.5">
          <Plus className="h-4 w-4" />
          Add Secret
        </Button>
      </div>

      {/* Security notice */}
      <Card className="border-amber-500/50 bg-amber-500/5">
        <CardContent className="flex items-center gap-3 py-3">
          <EyeOff className="h-5 w-5 text-amber-600" />
          <p className="text-sm text-amber-700 dark:text-amber-400">
            Secret values are never displayed after saving. You can update a secret's value, but
            you cannot view it.
          </p>
        </CardContent>
      </Card>

      {/* Secrets table */}
      {secrets.length === 0 ? (
        <Card>
          <CardContent className="py-8 text-center">
            <Key className="text-muted-foreground mx-auto mb-3 h-10 w-10" />
            <p className="text-muted-foreground mb-4">
              No secrets configured for this project yet.
            </p>
            <Button variant="outline" onClick={() => setIsCreating(true)}>
              Add First Secret
            </Button>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardHeader className="pb-0">
            <CardTitle className="text-base">Configured Secrets</CardTitle>
            <CardDescription>
              {secrets.length} secret{secrets.length !== 1 ? 's' : ''} configured
            </CardDescription>
          </CardHeader>
          <CardContent className="pt-4">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Value</TableHead>
                  <TableHead>Last Modified</TableHead>
                  <TableHead className="w-24">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {secrets.map((secret) => (
                  <TableRow key={secret.name}>
                    <TableCell className="font-mono text-sm">{secret.name}</TableCell>
                    <TableCell>
                      <span className="text-muted-foreground inline-flex items-center gap-1.5 text-sm">
                        <EyeOff className="h-3.5 w-3.5" />
                        ••••••••
                      </span>
                    </TableCell>
                    <TableCell className="text-muted-foreground text-sm">
                      {secret.lastModified ? (
                        <span className="inline-flex items-center gap-1.5">
                          <Clock className="h-3.5 w-3.5" />
                          {formatDate(secret.lastModified)}
                        </span>
                      ) : (
                        '—'
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="flex gap-1">
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => setEditingSecret(secret.name)}
                          aria-label="Update secret value"
                        >
                          <Pencil className="h-4 w-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={() => setDeletingSecret(secret)}
                          aria-label="Delete secret"
                        >
                          <Trash2 className="h-4 w-4" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      {/* Create/Edit Dialog */}
      <SecretDialog
        open={isCreating || !!editingSecret}
        onOpenChange={(open) => {
          if (!open) {
            setIsCreating(false)
            setEditingSecret(null)
          }
        }}
        projectId={projectId}
        editingName={editingSecret ?? undefined}
      />

      {/* Delete Confirmation */}
      <AlertDialog open={!!deletingSecret} onOpenChange={() => setDeletingSecret(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Secret</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete the secret "{deletingSecret?.name}"? This action
              cannot be undone and may break agent processes that depend on this variable.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleDelete}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}

function SecretsListSkeleton() {
  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div className="space-y-2">
          <Skeleton className="h-6 w-40" />
          <Skeleton className="h-4 w-72" />
        </div>
        <Skeleton className="h-9 w-28" />
      </div>
      <Skeleton className="h-12 w-full" />
      <Card>
        <CardContent className="py-4">
          <div className="space-y-3">
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

function formatDate(dateString: string): string {
  const date = new Date(dateString)
  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

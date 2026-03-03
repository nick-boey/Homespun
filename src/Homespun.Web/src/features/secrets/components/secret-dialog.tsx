import { useState, useEffect } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogCancel,
} from '@/components/ui/alert-dialog'
import { Loader } from '@/components/ui/loader'
import { useCreateSecret, useUpdateSecret } from '../hooks'

interface SecretDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  projectId: string
  editingName?: string
}

// Valid environment variable name pattern
const ENV_VAR_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/

export function SecretDialog({ open, onOpenChange, projectId, editingName }: SecretDialogProps) {
  const isEditing = !!editingName
  const createSecret = useCreateSecret(projectId)
  const updateSecret = useUpdateSecret(projectId)

  const [name, setName] = useState('')
  const [value, setValue] = useState('')
  const [showValue, setShowValue] = useState(false)
  const [nameError, setNameError] = useState<string | null>(null)

  // Reset form when dialog opens/closes
  useEffect(() => {
    if (open) {
      if (editingName) {
        setName(editingName)
      } else {
        setName('')
      }
      setValue('')
      setShowValue(false)
      setNameError(null)
    }
  }, [open, editingName])

  const validateName = (input: string): boolean => {
    if (!input.trim()) {
      setNameError('Name is required')
      return false
    }
    if (!ENV_VAR_PATTERN.test(input)) {
      setNameError(
        'Invalid name. Must start with a letter or underscore, and contain only letters, numbers, and underscores.'
      )
      return false
    }
    setNameError(null)
    return true
  }

  const handleNameChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newName = e.target.value.toUpperCase()
    setName(newName)
    if (newName) {
      validateName(newName)
    } else {
      setNameError(null)
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (!validateName(name) || !value.trim()) return

    if (isEditing) {
      await updateSecret.mutateAsync({
        name: editingName,
        value: value.trim(),
      })
    } else {
      await createSecret.mutateAsync({
        name: name.trim(),
        value: value.trim(),
      })
    }

    onOpenChange(false)
  }

  const isLoading = createSecret.isPending || updateSecret.isPending
  const canSubmit = !isLoading && !nameError && name.trim() && value.trim()

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent className="sm:max-w-md">
        <form onSubmit={handleSubmit}>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {isEditing ? 'Update Secret' : 'Add Secret'}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {isEditing
                ? `Update the value for "${editingName}". The current value cannot be displayed.`
                : 'Add a new environment variable that will be available to agents.'}
            </AlertDialogDescription>
          </AlertDialogHeader>

          <div className="grid gap-4 py-4">
            <div className="grid gap-2">
              <Label htmlFor="name">Name</Label>
              <Input
                id="name"
                value={name}
                onChange={handleNameChange}
                placeholder="e.g., API_KEY"
                disabled={isEditing}
                className={nameError ? 'border-destructive' : ''}
              />
              {nameError && <p className="text-destructive text-xs">{nameError}</p>}
              {!isEditing && (
                <p className="text-muted-foreground text-xs">
                  Environment variable names are automatically converted to uppercase.
                </p>
              )}
            </div>

            <div className="grid gap-2">
              <Label htmlFor="value">Value</Label>
              <div className="relative">
                <Input
                  id="value"
                  type={showValue ? 'text' : 'password'}
                  value={value}
                  onChange={(e) => setValue(e.target.value)}
                  placeholder={isEditing ? 'Enter new value' : 'Enter secret value'}
                  className="pr-10"
                  required
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="absolute right-0 top-0 h-full px-3 hover:bg-transparent"
                  onClick={() => setShowValue(!showValue)}
                  aria-label={showValue ? 'Hide value' : 'Show value'}
                >
                  {showValue ? (
                    <EyeOff className="h-4 w-4" />
                  ) : (
                    <Eye className="h-4 w-4" />
                  )}
                </Button>
              </div>
              <p className="text-muted-foreground text-xs">
                The value will be stored securely and never displayed after saving.
              </p>
            </div>
          </div>

          <AlertDialogFooter>
            <AlertDialogCancel type="button" disabled={isLoading}>
              Cancel
            </AlertDialogCancel>
            <Button type="submit" disabled={!canSubmit}>
              {isLoading && <Loader variant="circular" size="sm" className="mr-2" />}
              {isEditing ? 'Update Secret' : 'Add Secret'}
            </Button>
          </AlertDialogFooter>
        </form>
      </AlertDialogContent>
    </AlertDialog>
  )
}

import { useState, useCallback } from 'react'
import { AlertTriangle } from 'lucide-react'
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

export interface SecretFormDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (data: { name?: string; value: string }) => void
  isSubmitting: boolean
  mode: 'create' | 'edit'
  secretName?: string
}

// Valid environment variable name: starts with letter or underscore, followed by letters, numbers, or underscores
const ENV_VAR_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/

function SecretFormContent({
  onOpenChange,
  onSubmit,
  isSubmitting,
  mode,
  secretName,
}: Omit<SecretFormDialogProps, 'open'>) {
  const [name, setName] = useState('')
  const [value, setValue] = useState('')
  const [nameError, setNameError] = useState<string | null>(null)
  const [valueError, setValueError] = useState<string | null>(null)

  const validateName = useCallback((nameValue: string): boolean => {
    if (!nameValue.trim()) {
      setNameError('Name is required')
      return false
    }
    if (!ENV_VAR_PATTERN.test(nameValue)) {
      setNameError(
        'Name must start with a letter or underscore and contain only letters, numbers, and underscores'
      )
      return false
    }
    setNameError(null)
    return true
  }, [])

  const validateValue = useCallback((valueInput: string): boolean => {
    if (!valueInput) {
      setValueError('Value is required')
      return false
    }
    setValueError(null)
    return true
  }, [])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()

    if (mode === 'create') {
      const isNameValid = validateName(name)
      const isValueValid = validateValue(value)

      if (!isNameValid || !isValueValid) {
        return
      }

      onSubmit({ name, value })
    } else {
      const isValueValid = validateValue(value)

      if (!isValueValid) {
        return
      }

      onSubmit({ value })
    }

    // Clear sensitive data from state after submission
    setValue('')
  }

  const handleCancel = () => {
    setValue('') // Clear value for security
    onOpenChange(false)
  }

  return (
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{mode === 'create' ? 'Add Secret' : 'Update Secret'}</DialogTitle>
        <DialogDescription>
          {mode === 'create'
            ? 'Add a new environment variable secret for this project.'
            : `Update the value for ${secretName}.`}
        </DialogDescription>
      </DialogHeader>

      <form onSubmit={handleSubmit} className="space-y-4">
        {mode === 'create' ? (
          <div className="space-y-2">
            <Label htmlFor="secret-name">Name</Label>
            <Input
              id="secret-name"
              type="text"
              value={name}
              onChange={(e) => {
                setName(e.target.value.toUpperCase())
                if (nameError) validateName(e.target.value.toUpperCase())
              }}
              placeholder="API_KEY"
              className="font-mono"
              autoComplete="off"
            />
            {nameError && <p className="text-destructive text-sm">{nameError}</p>}
          </div>
        ) : (
          <div className="space-y-2">
            <Label>Secret Name</Label>
            <p className="font-mono text-sm font-medium">{secretName}</p>
          </div>
        )}

        <div className="space-y-2">
          <Label htmlFor="secret-value">{mode === 'create' ? 'Value' : 'New Value'}</Label>
          <Input
            id="secret-value"
            type="password"
            value={value}
            onChange={(e) => {
              setValue(e.target.value)
              if (valueError) validateValue(e.target.value)
            }}
            placeholder="Enter secret value"
            autoComplete="new-password"
          />
          {valueError && <p className="text-destructive text-sm">{valueError}</p>}
        </div>

        <div className="bg-muted/50 flex items-start gap-2 rounded-md p-3">
          <AlertTriangle className="text-warning mt-0.5 h-4 w-4 flex-shrink-0" />
          <p className="text-muted-foreground text-sm">
            Secret values cannot be viewed after saving. Make sure to store your secrets securely.
          </p>
        </div>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={handleCancel}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting}>
            {mode === 'create' ? 'Add' : 'Update'}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  )
}

export function SecretFormDialog({
  open,
  onOpenChange,
  onSubmit,
  isSubmitting,
  mode,
  secretName,
}: SecretFormDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {open && (
        <SecretFormContent
          onOpenChange={onOpenChange}
          onSubmit={onSubmit}
          isSubmitting={isSubmitting}
          mode={mode}
          secretName={secretName}
        />
      )}
    </Dialog>
  )
}

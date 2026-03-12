import * as React from 'react'
import { Check, ChevronsUpDown, User, X } from 'lucide-react'

import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { useProjectAssignees } from '../hooks/use-project-assignees'
import { Loader } from '@/components/ui/loader'

export interface AssigneeComboboxProps {
  projectId: string
  value: string | null
  onChange: (value: string | null) => void
  disabled?: boolean
  className?: string
}

/**
 * Combobox for selecting an assignee from project's existing assignees.
 * Shows autocomplete suggestions and allows free-form email entry.
 */
export function AssigneeCombobox({
  projectId,
  value,
  onChange,
  disabled = false,
  className,
}: AssigneeComboboxProps) {
  const [open, setOpen] = React.useState(false)
  const [inputValue, setInputValue] = React.useState('')
  const { assignees, isLoading } = useProjectAssignees(projectId)

  // Filter assignees based on input
  const filteredAssignees = React.useMemo(() => {
    if (!inputValue) return assignees
    const search = inputValue.toLowerCase()
    return assignees.filter((email) => email.toLowerCase().includes(search))
  }, [assignees, inputValue])

  // Check if input is a valid email not in the list
  const isCustomEmail = React.useMemo(() => {
    if (!inputValue) return false
    const search = inputValue.toLowerCase()
    const isEmail = inputValue.includes('@') && inputValue.includes('.')
    const notInList = !assignees.some((email) => email.toLowerCase() === search)
    return isEmail && notInList
  }, [inputValue, assignees])

  const handleSelect = (selectedValue: string) => {
    onChange(selectedValue)
    setInputValue('')
    setOpen(false)
  }

  const handleClear = (e: React.MouseEvent) => {
    e.stopPropagation()
    onChange(null)
    setInputValue('')
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label="Select assignee"
          disabled={disabled}
          className={cn('w-full justify-between', className)}
        >
          <span className="flex items-center gap-2 truncate">
            <User className="h-4 w-4 shrink-0 opacity-50" />
            {value ? (
              <span className="truncate">{value}</span>
            ) : (
              <span className="text-muted-foreground">Select assignee...</span>
            )}
          </span>
          <span className="flex items-center gap-1">
            {value && (
              <X
                className="h-4 w-4 shrink-0 opacity-50 hover:opacity-100"
                onClick={handleClear}
                aria-label="Clear assignee"
              />
            )}
            <ChevronsUpDown className="h-4 w-4 shrink-0 opacity-50" />
          </span>
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-[300px] p-0" align="start">
        <Command shouldFilter={false}>
          <CommandInput
            placeholder="Search or enter email..."
            value={inputValue}
            onValueChange={setInputValue}
          />
          <CommandList>
            {isLoading ? (
              <div className="flex items-center justify-center py-6">
                <Loader variant="circular" size="sm" />
              </div>
            ) : (
              <>
                <CommandEmpty>
                  {isCustomEmail ? (
                    <button
                      type="button"
                      className="text-primary hover:underline"
                      onClick={() => handleSelect(inputValue)}
                    >
                      Use &quot;{inputValue}&quot;
                    </button>
                  ) : (
                    'No assignees found.'
                  )}
                </CommandEmpty>
                <CommandGroup>
                  {filteredAssignees.map((email) => (
                    <CommandItem key={email} value={email} onSelect={() => handleSelect(email)}>
                      <Check
                        className={cn(
                          'mr-2 h-4 w-4',
                          value === email ? 'opacity-100' : 'opacity-0'
                        )}
                      />
                      {email}
                    </CommandItem>
                  ))}
                  {isCustomEmail && filteredAssignees.length > 0 && (
                    <CommandItem value={inputValue} onSelect={() => handleSelect(inputValue)}>
                      <Check className="mr-2 h-4 w-4 opacity-0" />
                      Use &quot;{inputValue}&quot;
                    </CommandItem>
                  )}
                </CommandGroup>
              </>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}

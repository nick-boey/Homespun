import * as React from 'react'
import { File, GitPullRequest } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Loader } from '@/components/ui/loader'
import type { SearchablePrResponse } from '@/api'
import { useFuzzySearch, type SearchableItem } from '../hooks/use-fuzzy-search'
import type { TriggerType } from '../hooks/use-mention-trigger'

export interface MentionSelection {
  type: TriggerType
  value: string
}

export interface MentionSearchPopupProps {
  /** Whether the popup is open */
  open: boolean
  /** The type of trigger (@ for files, # for PRs) */
  triggerType: TriggerType
  /** Current search query */
  query: string
  /** List of files for @ search */
  files: string[]
  /** List of PRs for # search */
  prs: SearchablePrResponse[]
  /** Called when an item is selected */
  onSelect: (selection: MentionSelection) => void
  /** Called when popup should close (Escape key) */
  onClose: () => void
  /** Whether files are loading */
  isLoadingFiles?: boolean
  /** Whether PRs are loading */
  isLoadingPrs?: boolean
  /** Optional class name for positioning */
  className?: string
}

const MAX_RESULTS = 20

/**
 * Popup component for @ and # mention search.
 * Shows file or PR search results based on trigger type.
 */
export function MentionSearchPopup({
  open,
  triggerType,
  query,
  files,
  prs,
  onSelect,
  onClose,
  isLoadingFiles = false,
  isLoadingPrs = false,
  className,
}: MentionSearchPopupProps) {
  const [selectedIndex, setSelectedIndex] = React.useState(0)
  const listRef = React.useRef<HTMLUListElement>(null)

  // Convert files to searchable items
  const fileItems: SearchableItem[] = React.useMemo(
    () => files.map((f) => ({ id: f, displayText: f })),
    [files]
  )

  // Convert PRs to searchable items
  const prItems: SearchableItem[] = React.useMemo(
    () =>
      prs.map((pr) => ({
        id: String(pr.number),
        displayText: `#${pr.number} - ${pr.title}`,
        metadata: { number: pr.number, title: pr.title },
      })),
    [prs]
  )

  // Get filtered results based on trigger type
  const searchItems = triggerType === '@' ? fileItems : prItems
  const filteredItems = useFuzzySearch(searchItems, query, MAX_RESULTS)

  const isLoading = triggerType === '@' ? isLoadingFiles : isLoadingPrs
  const isEmpty = !isLoading && filteredItems.length === 0

  // Selection handler - defined before effects that use it
  const handleSelectItem = React.useCallback(
    (item: SearchableItem) => {
      onSelect({ type: triggerType, value: item.id })
    },
    [onSelect, triggerType]
  )

  // Reset selection when items change
  React.useEffect(() => {
    setSelectedIndex(0)
  }, [query, triggerType])

  // Handle keyboard navigation
  React.useEffect(() => {
    if (!open) return

    const handleKeyDown = (e: KeyboardEvent) => {
      switch (e.key) {
        case 'Escape':
          e.preventDefault()
          onClose()
          break
        case 'ArrowDown':
          e.preventDefault()
          setSelectedIndex((prev) => Math.min(prev + 1, filteredItems.length - 1))
          break
        case 'ArrowUp':
          e.preventDefault()
          setSelectedIndex((prev) => Math.max(prev - 1, 0))
          break
        case 'Enter':
          e.preventDefault()
          if (filteredItems[selectedIndex]) {
            handleSelectItem(filteredItems[selectedIndex])
          }
          break
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [open, filteredItems, selectedIndex, onClose, handleSelectItem])

  // Scroll selected item into view
  React.useEffect(() => {
    if (listRef.current && filteredItems.length > 0) {
      const selectedEl = listRef.current.children[selectedIndex] as HTMLElement
      selectedEl?.scrollIntoView({ block: 'nearest' })
    }
  }, [selectedIndex, filteredItems.length])

  if (!open) return null

  return (
    <div
      className={cn(
        'bg-popover text-popover-foreground absolute z-50 w-80 rounded-md border shadow-md',
        className
      )}
      role="listbox"
      aria-label={triggerType === '@' ? 'File search results' : 'PR search results'}
    >
      {isLoading ? (
        <div className="flex items-center justify-center py-6" data-testid="search-loading">
          <Loader variant="circular" size="sm" />
        </div>
      ) : isEmpty ? (
        <div className="text-muted-foreground px-3 py-4 text-center text-sm">
          {triggerType === '@' ? 'No files found' : 'No pull requests found'}
        </div>
      ) : (
        <ul ref={listRef} className="max-h-60 overflow-auto p-1">
          {filteredItems.map((item, index) => (
            <li
              key={item.id}
              role="option"
              aria-selected={index === selectedIndex}
              className={cn(
                'flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-sm',
                index === selectedIndex
                  ? 'bg-accent text-accent-foreground'
                  : 'hover:bg-accent hover:text-accent-foreground'
              )}
              onClick={() => handleSelectItem(item)}
              onMouseEnter={() => setSelectedIndex(index)}
            >
              {triggerType === '@' ? (
                <>
                  <File className="h-4 w-4 shrink-0 opacity-50" />
                  <span className="truncate">{item.displayText}</span>
                </>
              ) : (
                <>
                  <GitPullRequest className="h-4 w-4 shrink-0 opacity-50" />
                  <span className="shrink-0 font-medium">#{item.id}</span>
                  <span className="text-muted-foreground truncate">
                    {(item.metadata?.title as string) || ''}
                  </span>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

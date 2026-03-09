import {
  ArrowUpFromLine,
  ArrowDownFromLine,
  CornerRightUp,
  CornerLeftDown,
  Undo2,
  Redo2,
  Pencil,
  Play,
  Minus,
  Plus,
  Search,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { ButtonGroup } from '@/components/ui/button-group'
import { Input } from '@/components/ui/input'
import { Separator } from '@/components/ui/separator'
import { useIssueHistory } from '../hooks/use-issue-history'
import { cn } from '@/lib/utils'
import { useMobile } from '@/hooks'

export interface ProjectToolbarProps {
  projectId: string
  selectedIssueId: string | null
  onCreateAbove: () => void
  onCreateBelow: () => void
  onMakeChild: () => void
  onMakeParent: () => void
  /** Whether the "Make Child Of" button is in active selection mode */
  childOfActive?: boolean
  /** Whether the "Make Parent Of" button is in active selection mode */
  parentOfActive?: boolean
  onEditIssue: () => void
  onOpenAgentLauncher: () => void
  depth: number
  onDepthChange: (depth: number) => void
  searchQuery: string
  onSearchChange: (query: string) => void
  searchMatchCount: number
  onNextMatch: () => void
  onPreviousMatch: () => void
  onEmbedSearch: () => void
}

export function ProjectToolbar({
  projectId,
  selectedIssueId,
  onCreateAbove,
  onCreateBelow,
  onMakeChild,
  onMakeParent,
  childOfActive = false,
  parentOfActive = false,
  onEditIssue,
  onOpenAgentLauncher,
  depth,
  onDepthChange,
  searchQuery,
  onSearchChange,
  searchMatchCount,
}: ProjectToolbarProps) {
  const { canUndo, canRedo, undoDescription, redoDescription, undo, redo, isUndoing, isRedoing } =
    useIssueHistory(projectId)
  const isMobile = useMobile()

  const hasIssueSelected = selectedIssueId !== null

  // Use touch-friendly sizes on mobile
  const buttonSize = isMobile ? 'icon-touch' : 'icon-sm'

  return (
    <div
      role="toolbar"
      aria-label="Issue management toolbar"
      className={cn(
        'sticky top-[4px] z-10 md:top-[4px]',
        'flex items-center gap-2 overflow-x-auto',
        'border-border bg-background/80 rounded-lg border px-2 backdrop-blur-sm',
        // More padding on mobile for touch
        isMobile ? 'py-2' : 'py-1.5',
        'scrollbar-thin scrollbar-thumb-muted scrollbar-track-transparent'
      )}
    >
      {/* Creation buttons group */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onCreateAbove}
          aria-label="Create above (Shift+O)"
          title="Create above (Shift+O)"
        >
          <ArrowUpFromLine className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onCreateBelow}
          aria-label="Create below (O)"
          title="Create below (O)"
        >
          <ArrowDownFromLine className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Hierarchy buttons group */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onMakeChild}
          disabled={!hasIssueSelected}
          aria-label="Make child of another (click to select target)"
          title="Make child of another (click to select target)"
          className={cn(childOfActive && 'ring-ring ring-2')}
        >
          <CornerRightUp className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={onMakeParent}
          disabled={!hasIssueSelected}
          aria-label="Make parent of another (click to select target)"
          title="Make parent of another (click to select target)"
          className={cn(parentOfActive && 'ring-ring ring-2')}
        >
          <CornerLeftDown className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Undo/Redo buttons group */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={() => undo()}
          disabled={!canUndo || isUndoing}
          aria-label="Undo (u)"
          title={undoDescription ? `Undo: ${undoDescription}` : 'Undo (u)'}
        >
          <Undo2 className="h-4 w-4" />
        </Button>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={() => redo()}
          disabled={!canRedo || isRedoing}
          aria-label="Redo"
          title={redoDescription ? `Redo: ${redoDescription}` : 'Redo'}
        >
          <Redo2 className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Edit button */}
      <Button
        variant="outline"
        size={buttonSize}
        onClick={onEditIssue}
        disabled={!hasIssueSelected}
        aria-label="Edit issue"
        title="Edit issue"
      >
        <Pencil className="h-4 w-4" />
      </Button>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Agent Run button */}
      <Button
        variant="outline"
        size={buttonSize}
        onClick={onOpenAgentLauncher}
        aria-label="Run agent (e)"
        title="Run agent (e)"
        data-testid="toolbar-run-agent"
      >
        <Play className="h-4 w-4" />
      </Button>

      {/* Spacer */}
      <div className="flex-1" />

      {/* Depth controls (right side) */}
      <ButtonGroup>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={() => onDepthChange(depth - 1)}
          disabled={depth <= 1}
          aria-label="Decrease depth ([)"
          title="Decrease depth ([)"
        >
          <Minus className="h-4 w-4" />
        </Button>
        <span className="border-border text-muted-foreground flex min-w-[1.5rem] items-center justify-center border-y bg-transparent text-center text-sm">
          {depth}
        </span>
        <Button
          variant="outline"
          size={buttonSize}
          onClick={() => onDepthChange(depth + 1)}
          aria-label="Increase depth (])"
          title="Increase depth (])"
        >
          <Plus className="h-4 w-4" />
        </Button>
      </ButtonGroup>

      <Separator orientation="vertical" className="mx-1 h-6" />

      {/* Search input (right side) */}
      <div className="relative flex items-center">
        <Search className="text-muted-foreground absolute left-2 h-4 w-4" aria-hidden="true" />
        <Input
          type="search"
          role="searchbox"
          placeholder={isMobile ? 'Search...' : 'Search issues (/)...'}
          value={searchQuery}
          onChange={(e) => onSearchChange(e.target.value)}
          className={cn(
            'pr-8 pl-8',
            // Touch-friendly height on mobile
            isMobile ? 'h-11 w-[140px]' : 'h-8 w-[180px]'
          )}
          aria-label="Search issues"
        />
        {searchQuery && searchMatchCount > 0 && (
          <span
            data-testid="search-match-count"
            className="bg-muted text-muted-foreground absolute right-2 rounded px-1.5 py-0.5 text-xs"
          >
            {searchMatchCount}
          </span>
        )}
      </div>
    </div>
  )
}

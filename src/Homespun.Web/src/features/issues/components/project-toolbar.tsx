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
import { Input } from '@/components/ui/input'
import { useIssueHistory } from '../hooks/use-issue-history'
import { cn } from '@/lib/utils'

export interface ProjectToolbarProps {
  projectId: string
  selectedIssueId: string | null
  onCreateAbove: () => void
  onCreateBelow: () => void
  onMakeChild: () => void
  onMakeParent: () => void
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

  const hasIssueSelected = selectedIssueId !== null

  return (
    <div
      role="toolbar"
      aria-label="Issue management toolbar"
      className={cn(
        'sticky top-[52px] z-10',
        'flex items-center gap-1 overflow-x-auto',
        'bg-background border-border border-b px-2 py-1.5',
        'scrollbar-thin scrollbar-thumb-muted scrollbar-track-transparent'
      )}
    >
      {/* Creation buttons group */}
      <div className="flex items-center gap-0.5">
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={onCreateAbove}
          aria-label="Create above (Shift+O)"
          title="Create above (Shift+O)"
        >
          <ArrowUpFromLine className="h-4 w-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={onCreateBelow}
          aria-label="Create below (O)"
          title="Create below (O)"
        >
          <ArrowDownFromLine className="h-4 w-4" />
        </Button>
      </div>

      <div className="bg-border mx-1 h-6 w-px" aria-hidden="true" />

      {/* Hierarchy buttons group */}
      <div className="flex items-center gap-0.5">
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={onMakeChild}
          aria-label="Make child of another"
          title="Make child of another"
        >
          <CornerRightUp className="h-4 w-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={onMakeParent}
          aria-label="Make parent of another"
          title="Make parent of another"
        >
          <CornerLeftDown className="h-4 w-4" />
        </Button>
      </div>

      <div className="bg-border mx-1 h-6 w-px" aria-hidden="true" />

      {/* Undo/Redo buttons group */}
      <div className="flex items-center gap-0.5">
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={() => undo()}
          disabled={!canUndo || isUndoing}
          aria-label="Undo (u)"
          title={undoDescription ? `Undo: ${undoDescription}` : 'Undo (u)'}
        >
          <Undo2 className="h-4 w-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={() => redo()}
          disabled={!canRedo || isRedoing}
          aria-label="Redo"
          title={redoDescription ? `Redo: ${redoDescription}` : 'Redo'}
        >
          <Redo2 className="h-4 w-4" />
        </Button>
      </div>

      <div className="bg-border mx-1 h-6 w-px" aria-hidden="true" />

      {/* Edit button */}
      <Button
        variant="ghost"
        size="icon-sm"
        onClick={onEditIssue}
        disabled={!hasIssueSelected}
        aria-label="Edit issue"
        title="Edit issue"
      >
        <Pencil className="h-4 w-4" />
      </Button>

      <div className="bg-border mx-1 h-6 w-px" aria-hidden="true" />

      {/* Agent Run button */}
      <Button
        variant="ghost"
        size="icon-sm"
        onClick={onOpenAgentLauncher}
        aria-label="Run agent (e)"
        title="Run agent (e)"
      >
        <Play className="h-4 w-4" />
      </Button>

      {/* Spacer */}
      <div className="flex-1" />

      {/* Depth controls (right side) */}
      <div className="flex items-center gap-0.5">
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={() => onDepthChange(depth - 1)}
          disabled={depth <= 1}
          aria-label="Decrease depth ([)"
          title="Decrease depth ([)"
        >
          <Minus className="h-4 w-4" />
        </Button>
        <span className="text-muted-foreground min-w-[1.5rem] text-center text-sm">{depth}</span>
        <Button
          variant="ghost"
          size="icon-sm"
          onClick={() => onDepthChange(depth + 1)}
          aria-label="Increase depth (])"
          title="Increase depth (])"
        >
          <Plus className="h-4 w-4" />
        </Button>
      </div>

      <div className="bg-border mx-1 h-6 w-px" aria-hidden="true" />

      {/* Search input (right side) */}
      <div className="relative flex items-center">
        <Search className="text-muted-foreground absolute left-2 h-4 w-4" aria-hidden="true" />
        <Input
          type="search"
          role="searchbox"
          placeholder="Search issues (/)..."
          value={searchQuery}
          onChange={(e) => onSearchChange(e.target.value)}
          className="h-8 w-[180px] pr-8 pl-8"
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

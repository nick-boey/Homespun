import { GitBranch, Trash2, ExternalLink, AlertCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Link } from '@tanstack/react-router'
import type { EnrichedCloneInfo } from '@/api/generated/types.gen'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'

export interface EnrichedCloneCardProps {
  clone: EnrichedCloneInfo
  projectId: string
  onDelete: () => void
  isDeleting?: boolean
}

export function EnrichedCloneCard({
  clone,
  projectId,
  onDelete,
  isDeleting,
}: EnrichedCloneCardProps) {
  const branchName =
    clone.clone.expectedBranch ?? clone.clone.branch?.replace('refs/heads/', '') ?? 'unknown'
  const folderName = clone.clone.folderName
  const commitSha = clone.clone.headCommit?.slice(0, 7) ?? 'unknown'

  return (
    <Card className={clone.isDeletable ? 'border-muted border-dashed' : ''}>
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-4">
          {/* Left side: Clone info */}
          <div className="flex-1 space-y-2">
            {/* Branch name and folder */}
            <div className="flex items-center gap-2">
              <GitBranch className="text-muted-foreground h-4 w-4" />
              <span className="font-medium">{branchName}</span>
              {folderName !== branchName && (
                <span className="text-muted-foreground text-sm">({folderName})</span>
              )}
            </div>

            {/* Commit info */}
            <div className="text-muted-foreground text-sm">
              Commit: <code>{commitSha}</code>
            </div>

            {/* Linked Issue */}
            {clone.linkedIssue && (
              <div className="flex items-center gap-2">
                <span className="text-sm">Issue:</span>
                <Link
                  to="/projects/$projectId/issues"
                  params={{ projectId }}
                  search={{ selected: clone.linkedIssueId }}
                  className="text-sm hover:underline"
                >
                  {clone.linkedIssue.title}
                </Link>
                <IssueStatusBadge status={clone.linkedIssue.status ?? ''} />
              </div>
            )}

            {/* Issue not found warning */}
            {clone.linkedIssueId && !clone.linkedIssue && (
              <div className="flex items-center gap-2 text-sm text-amber-600">
                <AlertCircle className="h-4 w-4" />
                Issue {clone.linkedIssueId} not found (deleted)
              </div>
            )}

            {/* Linked PR */}
            {clone.linkedPr && (
              <div className="flex items-center gap-2">
                <span className="text-sm">PR:</span>
                <a
                  href={clone.linkedPr.htmlUrl ?? '#'}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-1 text-sm hover:underline"
                >
                  #{clone.linkedPr.number}: {clone.linkedPr.title}
                  <ExternalLink className="h-3 w-3" />
                </a>
                <PrStatusBadge status={clone.linkedPr.status} />
              </div>
            )}

            {/* Deletion reason */}
            {clone.isDeletable && clone.deletionReason && (
              <div className="text-muted-foreground text-sm italic">{clone.deletionReason}</div>
            )}
          </div>

          {/* Right side: Actions */}
          <div className="flex items-center gap-2">
            {clone.isDeletable && (
              <AlertDialog>
                <AlertDialogTrigger asChild>
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={isDeleting}
                    className="text-destructive hover:text-destructive"
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Delete Clone</AlertDialogTitle>
                    <AlertDialogDescription>
                      Are you sure you want to delete the clone for branch &quot;{branchName}&quot;?
                      This will permanently remove the clone folder.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Cancel</AlertDialogCancel>
                    <AlertDialogAction onClick={onDelete}>Delete</AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function IssueStatusBadge({ status }: { status: string }) {
  const variant =
    (
      {
        complete: 'secondary',
        archived: 'secondary',
        closed: 'secondary',
        open: 'default',
        progress: 'default',
        review: 'default',
      } as Record<string, 'secondary' | 'default' | 'outline'>
    )[status.toLowerCase()] ?? 'outline'

  return <Badge variant={variant}>{status}</Badge>
}

function PrStatusBadge({ status }: { status: string }) {
  const variant =
    (
      {
        merged: 'secondary',
        closed: 'destructive',
        inProgress: 'default',
        readyForReview: 'default',
        checksFailing: 'destructive',
        conflict: 'destructive',
        readyForMerging: 'default',
      } as Record<string, 'secondary' | 'default' | 'destructive' | 'outline'>
    )[status] ?? 'outline'

  return <Badge variant={variant}>{status}</Badge>
}

import { memo, useState, useCallback } from 'react'
import { RefreshCw, AlertCircle } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { type PullRequestInfo } from '@/api'
import { useOpenPullRequests, useMergedPullRequests, useSyncPullRequests } from '../hooks'
import { PrRow } from './pr-row'
import { PrRowSkeleton } from './pr-row-skeleton'
import { OpenPrDetailPanel } from './open-pr-detail-panel'
import { MergedPrDetailPanel } from './merged-pr-detail-panel'

export interface PullRequestsTabProps {
  projectId: string
  onViewIssue?: (issueId: string) => void
  onStartAgent?: (branchName: string) => void
  className?: string
}

/**
 * Main tab component for displaying pull requests.
 * Shows open PRs, recently merged PRs, and provides sync functionality.
 */
export const PullRequestsTab = memo(function PullRequestsTab({
  projectId,
  onViewIssue,
  onStartAgent,
  className,
}: PullRequestsTabProps) {
  const [selectedPr, setSelectedPr] = useState<PullRequestInfo | null>(null)
  const [selectedPrType, setSelectedPrType] = useState<'open' | 'merged' | null>(null)

  const {
    pullRequests: openPRs,
    isLoading: isLoadingOpen,
    isError: isOpenError,
    refetch: refetchOpen,
  } = useOpenPullRequests(projectId)

  const {
    pullRequests: mergedPRs,
    isLoading: isLoadingMerged,
    isError: isMergedError,
    refetch: refetchMerged,
  } = useMergedPullRequests(projectId)

  const { syncPullRequests, isPending: isSyncing } = useSyncPullRequests()

  const handleSync = useCallback(async () => {
    await syncPullRequests(projectId)
    refetchOpen()
    refetchMerged()
  }, [projectId, syncPullRequests, refetchOpen, refetchMerged])

  const handleSelectOpenPr = useCallback((pr: PullRequestInfo) => {
    setSelectedPr(pr)
    setSelectedPrType('open')
  }, [])

  const handleSelectMergedPr = useCallback((pr: PullRequestInfo) => {
    setSelectedPr(pr)
    setSelectedPrType('merged')
  }, [])

  const handleCloseDetail = useCallback(() => {
    setSelectedPr(null)
    setSelectedPrType(null)
  }, [])

  const isLoading = isLoadingOpen || isLoadingMerged
  const hasError = isOpenError || isMergedError

  return (
    <div className={cn('flex h-full gap-4', className)}>
      {/* PR List */}
      <div className="min-w-0 flex-1 space-y-6 overflow-auto">
        {/* Header with sync button */}
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold">Pull Requests</h2>
          <Button variant="outline" size="sm" onClick={handleSync} disabled={isSyncing}>
            <RefreshCw className={cn('mr-2 h-4 w-4', isSyncing && 'animate-spin')} />
            Sync from GitHub
          </Button>
        </div>

        {/* Error state */}
        {hasError && (
          <div className="border-destructive/50 bg-destructive/10 text-destructive flex items-center gap-2 rounded-lg border p-4">
            <AlertCircle className="h-5 w-5" />
            <span>Failed to load pull requests. Please try again.</span>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                refetchOpen()
                refetchMerged()
              }}
            >
              Retry
            </Button>
          </div>
        )}

        {/* Open PRs section */}
        <section>
          <h3 className="text-muted-foreground mb-3 text-sm font-medium">Open Pull Requests</h3>
          <div className="space-y-2">
            {isLoading ? (
              <>
                <PrRowSkeleton />
                <PrRowSkeleton />
                <PrRowSkeleton />
              </>
            ) : openPRs && openPRs.length > 0 ? (
              openPRs.map((prWithStatus) => (
                <PrRow
                  key={prWithStatus.pullRequest?.number}
                  pr={prWithStatus.pullRequest!}
                  isSelected={
                    selectedPr?.number === prWithStatus.pullRequest?.number &&
                    selectedPrType === 'open'
                  }
                  onSelect={handleSelectOpenPr}
                />
              ))
            ) : (
              <p className="text-muted-foreground py-4 text-center text-sm">
                No open pull requests
              </p>
            )}
          </div>
        </section>

        {/* Merged PRs section */}
        <section>
          <h3 className="text-muted-foreground mb-3 text-sm font-medium">Recently Merged</h3>
          <div className="space-y-2">
            {isLoading ? (
              <>
                <PrRowSkeleton />
                <PrRowSkeleton />
              </>
            ) : mergedPRs && mergedPRs.length > 0 ? (
              mergedPRs.map((prWithTime) => (
                <PrRow
                  key={prWithTime.pullRequest?.number}
                  pr={prWithTime.pullRequest!}
                  isSelected={
                    selectedPr?.number === prWithTime.pullRequest?.number &&
                    selectedPrType === 'merged'
                  }
                  onSelect={handleSelectMergedPr}
                />
              ))
            ) : (
              <p className="text-muted-foreground py-4 text-center text-sm">
                No recently merged pull requests
              </p>
            )}
          </div>
        </section>
      </div>

      {/* Detail Panel */}
      {selectedPr && (
        <div className="w-96 shrink-0">
          {selectedPrType === 'open' ? (
            <OpenPrDetailPanel
              pr={selectedPr}
              onClose={handleCloseDetail}
              onViewIssue={onViewIssue}
              onStartAgent={onStartAgent}
            />
          ) : (
            <MergedPrDetailPanel
              pr={selectedPr}
              onClose={handleCloseDetail}
              onViewIssue={onViewIssue}
              timeSpentMinutes={
                mergedPRs?.find((p) => p.pullRequest?.number === selectedPr.number)?.time
              }
            />
          )}
        </div>
      )}
    </div>
  )
})

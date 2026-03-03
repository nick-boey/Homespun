import { memo, useState, useCallback } from 'react'
import { RefreshCw, X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { type PullRequestInfo } from '@/api'
import { useOpenPullRequests, useMergedPullRequests, useSyncPullRequests } from '../hooks'
import { PrRow } from './pr-row'
import { PrRowSkeleton } from './pr-row-skeleton'
import { OpenPrDetailPanel } from './open-pr-detail-panel'
import { MergedPrDetailPanel } from './merged-pr-detail-panel'
import { ErrorFallback } from '@/components/error-boundary'
import { useMobile } from '@/hooks'

export interface PullRequestsTabProps {
  projectId: string
  onViewIssue?: (issueId: string) => void
  onStartAgent?: (branchName: string) => void
  className?: string
}

/**
 * Main tab component for displaying pull requests.
 * Shows open PRs, recently merged PRs, and provides sync functionality.
 * On mobile, detail panel appears as a full-screen overlay.
 */
export const PullRequestsTab = memo(function PullRequestsTab({
  projectId,
  onViewIssue,
  onStartAgent,
  className,
}: PullRequestsTabProps) {
  const isMobile = useMobile()
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

  // On mobile, hide list when detail is selected
  const showList = !isMobile || !selectedPr

  return (
    <div className={cn('flex h-full flex-col gap-4 md:flex-row', className)}>
      {/* PR List */}
      {showList && (
        <div className="min-w-0 flex-1 space-y-6 overflow-auto">
          {/* Header with sync button */}
          <div className="flex items-center justify-between gap-2">
            <h2 className="text-lg font-semibold">Pull Requests</h2>
            <Button
              variant="outline"
              size="sm"
              onClick={handleSync}
              disabled={isSyncing}
              className="min-h-[44px] md:min-h-0"
            >
              <RefreshCw className={cn('mr-2 h-4 w-4', isSyncing && 'animate-spin')} />
              <span className="hidden sm:inline">Sync from GitHub</span>
              <span className="sm:hidden">Sync</span>
            </Button>
          </div>

          {/* Error state */}
          {hasError && (
            <ErrorFallback
              title="Failed to load pull requests"
              description="Unable to fetch pull requests. Please try again."
              variant="compact"
              onRetry={() => {
                refetchOpen()
                refetchMerged()
              }}
            />
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
      )}

      {/* Detail Panel - full screen on mobile */}
      {selectedPr && (
        <div
          className={cn(
            'shrink-0',
            // Mobile: full width, show back button
            isMobile ? 'w-full' : 'w-96'
          )}
        >
          {/* Mobile back button */}
          {isMobile && (
            <Button
              variant="ghost"
              size="sm"
              onClick={handleCloseDetail}
              className="mb-3 min-h-[44px] gap-2"
            >
              <X className="h-4 w-4" />
              Close
            </Button>
          )}
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

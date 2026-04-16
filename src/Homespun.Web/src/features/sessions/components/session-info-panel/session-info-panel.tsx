import * as React from 'react'
import { X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useMobile } from '@/hooks/use-mobile'
import type { ClaudeSession } from '@/types/signalr'
import type { AGUIMessage } from '../../utils/agui-reducer'
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs'
import { Button } from '@/components/ui/button'
import { BottomSheet } from '../bottom-sheet'
import { SessionIssueTab } from './session-issue-tab'
import { SessionPrTab } from './session-pr-tab'
import { SessionTodosTab } from './session-todos-tab'
import { SessionFilesTab } from './session-files-tab'
import { SessionPlansTab } from './session-plans-tab'
import { SessionBranchTab } from './session-branch-tab'
import { SessionHistoryTab } from './session-history-tab'

interface SessionInfoPanelProps {
  session: ClaudeSession
  /**
   * Current AG-UI reducer messages for the session. Used by the Todos tab to parse
   * the latest TodoWrite tool input. Optional (default `[]`) because some tests
   * render the panel without driving the envelope stream.
   */
  messages?: AGUIMessage[]
  isOpen: boolean
  onOpenChange: (open: boolean) => void
  defaultOpen?: boolean
  viewingHistoricalSessionId?: string | null
  onSelectHistoricalSession?: (sessionId: string) => void
}

export function SessionInfoPanel({
  session,
  messages = [],
  isOpen,
  onOpenChange,
  defaultOpen = false,
  viewingHistoricalSessionId,
  onSelectHistoricalSession,
}: SessionInfoPanelProps) {
  const isMobile = useMobile()

  // Initialize from localStorage on mount
  React.useEffect(() => {
    const storedValue = localStorage.getItem('sessionInfoPanelOpen')
    if (storedValue !== null) {
      const shouldBeOpen = storedValue === 'true'
      if (shouldBeOpen !== isOpen) {
        onOpenChange(shouldBeOpen)
      }
    } else if (defaultOpen !== undefined && defaultOpen !== isOpen) {
      onOpenChange(defaultOpen)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Save state to localStorage when it changes
  React.useEffect(() => {
    localStorage.setItem('sessionInfoPanelOpen', String(isOpen))
  }, [isOpen])

  const content = (
    <Tabs defaultValue="issue" className="flex h-full flex-col">
      <TabsList
        variant="line"
        className="scrollbar-thin scrollbar-track-transparent scrollbar-thumb-muted w-full justify-start overflow-x-auto border-b px-4"
      >
        <TabsTrigger value="issue" className="shrink-0">
          Issue
        </TabsTrigger>
        <TabsTrigger value="pr" className="shrink-0">
          PR
        </TabsTrigger>
        <TabsTrigger value="todos" className="shrink-0">
          To-do's
        </TabsTrigger>
        <TabsTrigger value="files" className="shrink-0">
          Files
        </TabsTrigger>
        <TabsTrigger value="plans" className="shrink-0">
          Plans
        </TabsTrigger>
        <TabsTrigger value="branch" className="shrink-0">
          Branch
        </TabsTrigger>
        <TabsTrigger value="sessions" className="shrink-0">
          Sessions
        </TabsTrigger>
      </TabsList>

      <div className="flex-1 overflow-y-auto">
        <TabsContent value="issue" className="mt-0 p-4">
          <SessionIssueTab session={session} />
        </TabsContent>
        <TabsContent value="pr" className="mt-0 p-4">
          <SessionPrTab session={session} />
        </TabsContent>
        <TabsContent value="todos" className="mt-0 p-4">
          <SessionTodosTab messages={messages} />
        </TabsContent>
        <TabsContent value="files" className="mt-0 p-4">
          <SessionFilesTab session={session} />
        </TabsContent>
        <TabsContent value="plans" className="mt-0 p-4">
          <SessionPlansTab session={session} />
        </TabsContent>
        <TabsContent value="branch" className="mt-0 p-4">
          <SessionBranchTab session={session} />
        </TabsContent>
        <TabsContent value="sessions" className="mt-0 p-4">
          <SessionHistoryTab
            session={session}
            currentSessionId={session.id ?? undefined}
            viewingHistoricalSessionId={viewingHistoricalSessionId}
            onSelectSession={onSelectHistoricalSession}
          />
        </TabsContent>
      </div>
    </Tabs>
  )

  if (isMobile) {
    return (
      <BottomSheet open={isOpen} onOpenChange={onOpenChange} title="Session Info" heightMode="full">
        {content}
      </BottomSheet>
    )
  }

  // Desktop layout - side panel
  return (
    <div
      data-testid="session-info-panel-desktop"
      className={cn(
        'bg-background fixed top-0 right-0 h-full w-80 border-l shadow-lg',
        'z-40 transform transition-transform duration-300 ease-in-out',
        isOpen ? 'translate-x-0' : 'translate-x-full'
      )}
    >
      <div className="flex items-center justify-between border-b p-4">
        <h2 className="text-lg font-semibold">Session Info</h2>
        <Button
          size="icon"
          variant="ghost"
          onClick={() => onOpenChange(false)}
          aria-label="Close panel"
        >
          <X className="h-4 w-4" />
        </Button>
      </div>
      {content}
    </div>
  )
}

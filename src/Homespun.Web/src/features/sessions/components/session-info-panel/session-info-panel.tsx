import * as React from 'react'
import { X } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useMobile } from '@/hooks/use-mobile'
import type { ClaudeSession } from '@/api/generated'
import {
  Tabs,
  TabsList,
  TabsTrigger,
  TabsContent,
} from '@/components/ui/tabs'
import { Button } from '@/components/ui/button'
import { BottomSheet } from '../bottom-sheet'
import { SessionIssueTab } from './session-issue-tab'
import { SessionPrTab } from './session-pr-tab'
import { SessionTodosTab } from './session-todos-tab'
import { SessionFilesTab } from './session-files-tab'
import { SessionPlansTab } from './session-plans-tab'

interface SessionInfoPanelProps {
  session: ClaudeSession
  isOpen: boolean
  onOpenChange: (open: boolean) => void
  defaultOpen?: boolean
}

export function SessionInfoPanel({
  session,
  isOpen,
  onOpenChange,
  defaultOpen = false,
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
  }, [])

  // Save state to localStorage when it changes
  React.useEffect(() => {
    localStorage.setItem('sessionInfoPanelOpen', String(isOpen))
  }, [isOpen])

  const content = (
    <Tabs defaultValue="issue" className="flex flex-col h-full">
      <TabsList variant="line" className="w-full justify-start px-4 border-b">
        <TabsTrigger value="issue">Issue</TabsTrigger>
        <TabsTrigger value="pr">PR</TabsTrigger>
        <TabsTrigger value="todos">To-do's</TabsTrigger>
        <TabsTrigger value="files">Files</TabsTrigger>
        <TabsTrigger value="plans">Plans</TabsTrigger>
      </TabsList>

      <div className="flex-1 overflow-y-auto">
        <TabsContent value="issue" className="mt-0 p-4">
          <SessionIssueTab session={session} />
        </TabsContent>
        <TabsContent value="pr" className="mt-0 p-4">
          <SessionPrTab session={session} />
        </TabsContent>
        <TabsContent value="todos" className="mt-0 p-4">
          <SessionTodosTab session={session} />
        </TabsContent>
        <TabsContent value="files" className="mt-0 p-4">
          <SessionFilesTab session={session} />
        </TabsContent>
        <TabsContent value="plans" className="mt-0 p-4">
          <SessionPlansTab session={session} />
        </TabsContent>
      </div>
    </Tabs>
  )

  if (isMobile) {
    return (
      <BottomSheet
        open={isOpen}
        onOpenChange={onOpenChange}
        title="Session Info"
        heightMode="full"
      >
        {content}
      </BottomSheet>
    )
  }

  // Desktop layout - side panel
  return (
    <div
      data-testid="session-info-panel-desktop"
      className={cn(
        'fixed right-0 top-0 h-full w-80 bg-background border-l shadow-lg',
        'transform transition-transform duration-300 ease-in-out z-40',
        isOpen ? 'translate-x-0' : 'translate-x-full'
      )}
    >
      <div className="flex items-center justify-between p-4 border-b">
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
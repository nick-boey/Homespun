import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from '@tanstack/react-router'

import { ClaudeSessionStatus, SessionMode } from '@/api'
import type { SessionSummary } from '@/api/generated/types.gen'
import { TooltipProvider } from '@/components/ui/tooltip'

import { SidebarSessionRow } from './sidebar-session-row'

function makeSession(overrides: Partial<SessionSummary>): SessionSummary {
  return {
    id: 'session-1',
    entityId: 'entity-1',
    projectId: 'project-1',
    model: 'sonnet',
    mode: SessionMode.BUILD,
    status: ClaudeSessionStatus.RUNNING,
    createdAt: '2024-01-01T10:00:00Z',
    lastActivityAt: '2024-01-01T10:00:00Z',
    ...overrides,
  }
}

function makeRouter(content: React.ReactNode) {
  const rootRoute = createRootRoute({
    component: () => (
      <TooltipProvider>
        <div className="bg-sidebar w-64 rounded border p-2">{content}</div>
      </TooltipProvider>
    ),
  })
  const sessionRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/sessions/$sessionId',
    component: () => null,
  })
  return createRouter({
    routeTree: rootRoute.addChildren([sessionRoute]),
    history: createMemoryHistory({ initialEntries: ['/'] }),
  })
}

interface RowStoryArgs {
  status: ClaudeSessionStatus
  title: string
  isActive?: boolean
}

function RowStory({ status, title, isActive }: RowStoryArgs) {
  const router = makeRouter(
    <SidebarSessionRow session={makeSession({ status })} title={title} isActive={isActive} />
  )
  return <RouterProvider router={router} />
}

const meta: Meta<typeof RowStory> = {
  title: 'sessions/SidebarSessionRow',
  component: RowStory,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof RowStory>

export const Green: Story = {
  args: { status: ClaudeSessionStatus.RUNNING, title: 'Implement login flow' },
}

export const Yellow: Story = {
  args: { status: ClaudeSessionStatus.WAITING_FOR_INPUT, title: 'Refactor session reducer' },
}

export const Purple: Story = {
  args: {
    status: ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER,
    title: 'Confirm migration target',
  },
}

export const Orange: Story = {
  args: {
    status: ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION,
    title: 'Approve schema migration plan',
  },
}

export const Red: Story = {
  args: { status: ClaudeSessionStatus.ERROR, title: 'Failed to start agent container' },
}

export const TruncatedTitle: Story = {
  args: {
    status: ClaudeSessionStatus.RUNNING,
    title: 'A really long session title that overflows the sidebar width and must be truncated',
  },
}

export const Active: Story = {
  args: {
    status: ClaudeSessionStatus.RUNNING,
    title: 'Implement login flow',
    isActive: true,
  },
}

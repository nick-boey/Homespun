import type { Meta, StoryObj } from '@storybook/react-vite'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
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
import { allSessionsQueryKey, sessionsQueryKey } from '@/features/sessions/hooks/use-sessions'

import { SidebarSessionList } from './sidebar-session-list'

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

const FIXTURE_SESSIONS: SessionSummary[] = [
  makeSession({
    id: 's-old-running',
    entityId: 'Implement OAuth refresh',
    status: ClaudeSessionStatus.RUNNING,
    createdAt: '2024-01-01T08:00:00Z',
  }),
  makeSession({
    id: 's-yellow',
    entityId: 'Add session sidebar — review',
    status: ClaudeSessionStatus.WAITING_FOR_INPUT,
    createdAt: '2024-01-01T09:00:00Z',
  }),
  makeSession({
    id: 's-purple',
    entityId: 'Confirm migration target',
    status: ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER,
    createdAt: '2024-01-01T09:30:00Z',
  }),
  makeSession({
    id: 's-orange',
    entityId: 'Approve schema migration plan',
    status: ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION,
    createdAt: '2024-01-01T10:00:00Z',
  }),
  makeSession({
    id: 's-red',
    entityId: 'Failed to start agent container',
    status: ClaudeSessionStatus.ERROR,
    createdAt: '2024-01-01T10:30:00Z',
  }),
  makeSession({
    id: 's-stopped',
    entityId: 'Stopped — should NOT render',
    status: ClaudeSessionStatus.STOPPED,
    createdAt: '2024-01-01T07:00:00Z',
  }),
]

function makeRouter(content: React.ReactNode) {
  const rootRoute = createRootRoute({
    component: () => (
      <TooltipProvider>
        <div className="bg-sidebar w-64 rounded border p-3">{content}</div>
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

function makeQueryClient(sessions: SessionSummary[]) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  client.setQueryData(allSessionsQueryKey, sessions)
  client.setQueryData(sessionsQueryKey, sessions)
  return client
}

interface ListStoryArgs {
  projectId: string
  sessions: SessionSummary[]
}

function ListStory({ projectId, sessions }: ListStoryArgs) {
  const queryClient = makeQueryClient(sessions)
  const router = makeRouter(
    <QueryClientProvider client={queryClient}>
      <SidebarSessionList projectId={projectId} />
    </QueryClientProvider>
  )
  return <RouterProvider router={router} />
}

const meta: Meta<typeof ListStory> = {
  title: 'sessions/SidebarSessionList',
  component: ListStory,
  parameters: { layout: 'centered' },
}

export default meta
type Story = StoryObj<typeof ListStory>

export const MultiSessionMixedColors: Story = {
  args: {
    projectId: 'project-1',
    sessions: FIXTURE_SESSIONS,
  },
}

export const EmptyProject: Story = {
  args: {
    projectId: 'project-without-sessions',
    sessions: FIXTURE_SESSIONS,
  },
}

export const SingleSession: Story = {
  args: {
    projectId: 'project-1',
    sessions: [
      makeSession({
        id: 's-only',
        entityId: 'Migrate to AUI runtime',
        status: ClaudeSessionStatus.RUNNING,
      }),
    ],
  },
}

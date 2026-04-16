import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SessionSkillsTab } from './session-skills-tab'
import { Skills, SkillCategory } from '@/api'
import type { ReactNode } from 'react'
import type { DiscoveredSkills } from '@/api/generated/types.gen'
import type { ClaudeSession } from '@/types/signalr'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Skills: {
      getApiSkillsProjectByProjectId: vi.fn(),
    },
  }
})

const mockGetSkills = vi.mocked(Skills.getApiSkillsProjectByProjectId)

function mockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function renderWithClient(ui: ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>)
}

const SESSION: ClaudeSession = {
  id: 'session-1',
  projectId: 'project-1',
  entityId: 'issue-1',
} as ClaudeSession

describe('SessionSkillsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('lists discovered general skills with name and description', async () => {
    mockGetSkills.mockResolvedValueOnce(
      mockResponse<DiscoveredSkills>({
        openSpec: [],
        homespun: [],
        general: [
          {
            name: 'writing-plans',
            description: 'Use when you have a spec or requirements for a multi-step task',
            category: SkillCategory.GENERAL,
          },
          {
            name: 'brainstorming',
            description: 'Use before any creative work',
            category: SkillCategory.GENERAL,
          },
        ],
      })
    )

    renderWithClient(<SessionSkillsTab session={SESSION} />)

    expect(await screen.findByText('writing-plans')).toBeInTheDocument()
    expect(screen.getByText(/Use when you have a spec or requirements/i)).toBeInTheDocument()
    expect(screen.getByText('brainstorming')).toBeInTheDocument()
    expect(screen.getByText(/Use before any creative work/i)).toBeInTheDocument()
  })

  it('shows an empty state when no general skills are available', async () => {
    mockGetSkills.mockResolvedValueOnce(
      mockResponse<DiscoveredSkills>({ openSpec: [], homespun: [], general: [] })
    )

    renderWithClient(<SessionSkillsTab session={SESSION} />)

    expect(await screen.findByText(/no skills available/i)).toBeInTheDocument()
  })

  it('renders nothing useful without a projectId', () => {
    const session = { ...SESSION, projectId: null } as unknown as ClaudeSession

    renderWithClient(<SessionSkillsTab session={session} />)

    expect(mockGetSkills).not.toHaveBeenCalled()
  })
})

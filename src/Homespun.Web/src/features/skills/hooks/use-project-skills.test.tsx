import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useProjectSkills } from './use-project-skills'
import { Skills, SkillCategory, SkillArgKind, SessionMode } from '@/api'
import type { ReactNode } from 'react'
import type { DiscoveredSkills } from '@/api/generated/types.gen'

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

function createMockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useProjectSkills', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches all skill categories for a project', async () => {
    const mockSkills: DiscoveredSkills = {
      openSpec: [
        {
          name: 'openspec-apply-change',
          description: 'Apply a change',
          category: SkillCategory.OPEN_SPEC,
        },
      ],
      homespun: [
        {
          name: 'fix-bug',
          description: 'Fix a bug',
          category: SkillCategory.HOMESPUN,
          mode: SessionMode.BUILD,
          args: [{ name: 'issue-id', kind: SkillArgKind.ISSUE, label: 'Issue ID' }],
        },
      ],
      general: [
        {
          name: 'writing-plans',
          description: 'Write a plan',
          category: SkillCategory.GENERAL,
        },
      ],
    }

    mockGetSkills.mockResolvedValueOnce(createMockResponse(mockSkills))

    const { result } = renderHook(() => useProjectSkills('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toEqual(mockSkills)
    expect(mockGetSkills).toHaveBeenCalledWith({
      path: { projectId: 'project-123' },
    })
  })

  it('returns empty arrays when no skills exist', async () => {
    mockGetSkills.mockResolvedValueOnce(
      createMockResponse<DiscoveredSkills>({ openSpec: [], homespun: [], general: [] })
    )

    const { result } = renderHook(() => useProjectSkills('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toEqual({ openSpec: [], homespun: [], general: [] })
  })

  it('is disabled when projectId is empty', () => {
    const { result } = renderHook(() => useProjectSkills(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isFetching).toBe(false)
    expect(mockGetSkills).not.toHaveBeenCalled()
  })
})

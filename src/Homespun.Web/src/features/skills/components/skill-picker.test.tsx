import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SkillPicker } from './skill-picker'
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

function mockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function renderWithClient(ui: ReactNode) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>)
}

const HOMESPUN_SKILLS: DiscoveredSkills = {
  openSpec: [],
  general: [],
  homespun: [
    {
      name: 'fix-bug',
      description: 'Fix a bug described in a Fleece issue',
      category: SkillCategory.HOMESPUN,
      mode: SessionMode.BUILD,
      args: [
        { name: 'issue-id', kind: SkillArgKind.ISSUE, label: 'Issue ID' },
        { name: 'notes', kind: SkillArgKind.FREE_TEXT, label: 'Notes' },
      ],
    },
    {
      name: 'plan-feature',
      description: 'Plan a feature',
      category: SkillCategory.HOMESPUN,
      mode: SessionMode.PLAN,
      args: [{ name: 'topic', kind: SkillArgKind.FREE_TEXT, label: 'Topic' }],
    },
  ],
}

describe('SkillPicker', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the "None" option by default and lets you pick a skill', async () => {
    mockGetSkills.mockResolvedValueOnce(mockResponse(HOMESPUN_SKILLS))
    const onSkillChange = vi.fn()
    const onArgsChange = vi.fn()

    renderWithClient(
      <SkillPicker
        projectId="project-1"
        category={SkillCategory.HOMESPUN}
        selectedSkillName={null}
        onSkillChange={onSkillChange}
        argValues={{}}
        onArgValuesChange={onArgsChange}
      />
    )

    const trigger = await screen.findByRole('combobox', { name: /select skill/i })
    await userEvent.click(trigger)

    const listbox = await screen.findByRole('listbox')
    expect(within(listbox).getByText(/none/i)).toBeInTheDocument()
    expect(within(listbox).getByText(/fix-bug/)).toBeInTheDocument()
    expect(within(listbox).getByText(/plan-feature/)).toBeInTheDocument()

    await userEvent.click(within(listbox).getByText(/fix-bug/))
    expect(onSkillChange).toHaveBeenCalledWith('fix-bug')
  })

  it('renders one arg input per homespun-arg of the selected skill', async () => {
    mockGetSkills.mockResolvedValueOnce(mockResponse(HOMESPUN_SKILLS))

    renderWithClient(
      <SkillPicker
        projectId="project-1"
        category={SkillCategory.HOMESPUN}
        selectedSkillName="fix-bug"
        onSkillChange={vi.fn()}
        argValues={{}}
        onArgValuesChange={vi.fn()}
      />
    )

    expect(await screen.findByLabelText('Issue ID')).toBeInTheDocument()
    expect(screen.getByLabelText('Notes')).toBeInTheDocument()
  })

  it('renders no arg inputs when no skill is selected', async () => {
    mockGetSkills.mockResolvedValueOnce(mockResponse(HOMESPUN_SKILLS))

    renderWithClient(
      <SkillPicker
        projectId="project-1"
        category={SkillCategory.HOMESPUN}
        selectedSkillName={null}
        onSkillChange={vi.fn()}
        argValues={{}}
        onArgValuesChange={vi.fn()}
      />
    )

    // Wait for skills to load
    await screen.findByRole('combobox', { name: /select skill/i })

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('composes arg values via onArgValuesChange as the user types', async () => {
    mockGetSkills.mockResolvedValueOnce(mockResponse(HOMESPUN_SKILLS))
    const onArgsChange = vi.fn()

    renderWithClient(
      <SkillPicker
        projectId="project-1"
        category={SkillCategory.HOMESPUN}
        selectedSkillName="plan-feature"
        onSkillChange={vi.fn()}
        argValues={{}}
        onArgValuesChange={onArgsChange}
      />
    )

    const input = await screen.findByLabelText('Topic')
    await userEvent.type(input, 'a')

    expect(onArgsChange).toHaveBeenLastCalledWith({ topic: 'a' })
  })

  it('filters to the requested category (homespun)', async () => {
    mockGetSkills.mockResolvedValueOnce(
      mockResponse<DiscoveredSkills>({
        homespun: [
          {
            name: 'fix-bug',
            description: 'Fix',
            category: SkillCategory.HOMESPUN,
          },
        ],
        openSpec: [
          {
            name: 'openspec-apply-change',
            description: 'Apply',
            category: SkillCategory.OPEN_SPEC,
          },
        ],
        general: [],
      })
    )

    renderWithClient(
      <SkillPicker
        projectId="project-1"
        category={SkillCategory.HOMESPUN}
        selectedSkillName={null}
        onSkillChange={vi.fn()}
        argValues={{}}
        onArgValuesChange={vi.fn()}
      />
    )

    const trigger = await screen.findByRole('combobox', { name: /select skill/i })
    await userEvent.click(trigger)

    const listbox = await screen.findByRole('listbox')
    expect(within(listbox).getByText(/fix-bug/)).toBeInTheDocument()
    expect(within(listbox).queryByText(/openspec-apply-change/)).not.toBeInTheDocument()
  })
})

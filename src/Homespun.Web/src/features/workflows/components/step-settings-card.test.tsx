import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { StepSettingsCard } from './step-settings-card'
import type { WorkflowStep, AgentPrompt } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  AgentPrompts: {
    getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
  },
}))

vi.mock('@/hooks/use-telemetry', () => ({
  useTelemetry: () => ({
    trackEvent: vi.fn(),
    trackPageView: vi.fn(),
    trackException: vi.fn(),
    trackDependency: vi.fn(),
  }),
}))

const mockStep: WorkflowStep = {
  id: 'step-1',
  name: 'Build Step',
  stepType: 'agent',
  prompt: 'Build the project',
  promptId: null,
  sessionMode: 'build',
  onSuccess: { type: 'nextStep' },
  onFailure: { type: 'retry' },
  maxRetries: 3,
  retryDelaySeconds: 30,
  condition: null,
}

const mockSteps: WorkflowStep[] = [
  mockStep,
  { id: 'step-2', name: 'Test Step', stepType: 'agent' },
  { id: 'step-3', name: 'Deploy Step', stepType: 'serverAction' },
]

const mockPrompts: AgentPrompt[] = [
  { id: 'prompt-1', name: 'Code Review', initialMessage: 'Review the code', mode: 'plan' },
  { id: 'prompt-2', name: 'Build Project', initialMessage: 'Build it', mode: 'build' },
]

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('StepSettingsCard', () => {
  const defaultProps = {
    step: mockStep,
    allSteps: mockSteps,
    prompts: mockPrompts,
    projectId: 'proj-1',
    onChange: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders step name input with current value', () => {
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    const nameInput = screen.getByTestId('step-name-input')
    expect(nameInput).toHaveValue('Build Step')
  })

  it('calls onChange when step name is updated', async () => {
    const user = userEvent.setup()
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    const nameInput = screen.getByTestId('step-name-input')
    await user.type(nameInput, 'X')

    expect(defaultProps.onChange).toHaveBeenCalled()
    const lastCall = defaultProps.onChange.mock.calls.at(-1)![0]
    expect(lastCall.name).toBe('Build StepX')
  })

  it('displays step type selector with current type', () => {
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('step-type-agent')).toBeInTheDocument()
    expect(screen.getByTestId('step-type-serverAction')).toBeInTheDocument()
    expect(screen.getByTestId('step-type-gate')).toBeInTheDocument()
  })

  it('calls onChange when step type changes', async () => {
    const user = userEvent.setup()
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    await user.click(screen.getByTestId('step-type-serverAction'))

    expect(defaultProps.onChange).toHaveBeenCalledWith(
      expect.objectContaining({ stepType: 'serverAction' })
    )
  })

  it('shows agent config when step type is agent', () => {
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('agent-config')).toBeInTheDocument()
    expect(screen.getByTestId('session-mode-select')).toBeInTheDocument()
  })

  it('shows gate config when step type is gate', () => {
    const gateStep: WorkflowStep = {
      ...mockStep,
      stepType: 'gate',
      config: { description: 'Approval needed', timeoutSeconds: 600 },
    }
    render(<StepSettingsCard {...defaultProps} step={gateStep} />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByTestId('gate-config')).toBeInTheDocument()
  })

  it('shows server action config when step type is serverAction', () => {
    const saStep: WorkflowStep = {
      ...mockStep,
      stepType: 'serverAction',
      config: { actionType: 'ciMergePoll' },
    }
    render(<StepSettingsCard {...defaultProps} step={saStep} />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByTestId('server-action-config')).toBeInTheDocument()
  })

  it('shows on-success transition dropdown', () => {
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('on-success-select')).toBeInTheDocument()
  })

  it('shows on-failure transition dropdown', () => {
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('on-failure-select')).toBeInTheDocument()
  })

  it('shows retry config when failure transition is retry', () => {
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('max-retries-input')).toBeInTheDocument()
    expect(screen.getByTestId('retry-delay-input')).toBeInTheDocument()
  })

  it('hides retry config when failure transition is not retry', () => {
    const step: WorkflowStep = {
      ...mockStep,
      onFailure: { type: 'exit' },
    }
    render(<StepSettingsCard {...defaultProps} step={step} />, {
      wrapper: createWrapper(),
    })

    expect(screen.queryByTestId('max-retries-input')).not.toBeInTheDocument()
  })

  it('shows go-to-step selector when success transition is goToStep', () => {
    const step: WorkflowStep = {
      ...mockStep,
      onSuccess: { type: 'goToStep', targetStepId: 'step-2' },
    }
    render(<StepSettingsCard {...defaultProps} step={step} />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByTestId('on-success-target-select')).toBeInTheDocument()
  })

  it('shows condition input for skip logic', () => {
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    expect(screen.getByTestId('condition-input')).toBeInTheDocument()
  })

  it('toggles between inline prompt and template mode', async () => {
    const user = userEvent.setup()
    render(<StepSettingsCard {...defaultProps} />, { wrapper: createWrapper() })

    // Initially shows inline prompt (step has prompt, no promptId)
    expect(screen.getByTestId('prompt-textarea')).toBeInTheDocument()

    // Switch to template mode
    await user.click(screen.getByTestId('prompt-mode-template'))

    await waitFor(() => {
      expect(screen.getByTestId('prompt-template-select')).toBeInTheDocument()
    })
    expect(screen.queryByTestId('prompt-textarea')).not.toBeInTheDocument()
  })

  it('shows template dropdown with available prompts', () => {
    const step: WorkflowStep = {
      ...mockStep,
      prompt: null,
      promptId: 'prompt-1',
    }
    render(<StepSettingsCard {...defaultProps} step={step} />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByTestId('prompt-template-select')).toBeInTheDocument()
  })
})

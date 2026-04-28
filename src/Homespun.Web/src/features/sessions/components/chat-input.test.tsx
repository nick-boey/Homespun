import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

import { ChatInput } from './chat-input'

vi.mock('@/features/agents/hooks', () => ({
  useAvailableModels: () => ({
    models: [
      { id: 'opus', displayName: 'Opus', createdAt: '', isDefault: true },
      { id: 'sonnet', displayName: 'Sonnet', createdAt: '' },
      { id: 'haiku', displayName: 'Haiku', createdAt: '' },
    ],
    defaultModel: { id: 'opus', displayName: 'Opus', createdAt: '', isDefault: true },
    isLoading: false,
    isError: false,
    error: null,
  }),
}))

vi.mock('@/features/search', () => ({
  useProjectFiles: () => ({
    files: ['src/index.tsx', 'src/path with spaces.tsx', 'README.md'],
    isLoading: false,
    isSuccess: true,
    isError: false,
    error: null,
    refetch: () => {},
  }),
  useSearchablePrs: () => ({
    prs: [{ number: 42, title: 'Add new feature', branchName: 'feat/new' }],
    isLoading: false,
    isSuccess: true,
    isError: false,
    error: null,
    refetch: () => {},
  }),
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

function renderInput(ui: React.ReactElement) {
  return render(ui, { wrapper: createWrapper() })
}

describe('ChatInput', () => {
  const mockOnSend = vi.fn()
  const mockOnModeChange = vi.fn()
  const mockOnModelChange = vi.fn()

  const defaultProps = {
    onSend: mockOnSend,
    sessionMode: 'build' as const,
    sessionModel: 'opus',
    onModeChange: mockOnModeChange,
    onModelChange: mockOnModelChange,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('rendering', () => {
    it('renders the ComposerPrimitive.Input textarea', () => {
      renderInput(<ChatInput {...defaultProps} />)

      const textarea = screen.getByPlaceholderText(/message/i)
      expect(textarea).toBeInTheDocument()
      expect(textarea.tagName).toBe('TEXTAREA')
    })

    it('renders the send button', () => {
      renderInput(<ChatInput {...defaultProps} />)

      expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument()
    })

    it('renders Plan and Build tab triggers', () => {
      renderInput(<ChatInput {...defaultProps} />)

      const tablist = screen.getByRole('tablist', { name: /session mode/i })
      expect(within(tablist).getByRole('tab', { name: /plan/i })).toBeInTheDocument()
      expect(within(tablist).getByRole('tab', { name: /build/i })).toBeInTheDocument()
    })

    it('renders the model selector trigger', () => {
      renderInput(<ChatInput {...defaultProps} />)

      expect(screen.getByRole('combobox', { name: /model/i })).toBeInTheDocument()
    })
  })

  describe('sending messages', () => {
    it('calls onSend with text + current mode + model when send button is clicked', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} />)

      await user.type(screen.getByPlaceholderText(/message/i), 'Hello Claude')
      await user.click(screen.getByRole('button', { name: /send/i }))

      expect(mockOnSend).toHaveBeenCalledWith('Hello Claude', 'build', 'opus')
    })

    it('calls onSend with current mode/model when Enter is pressed', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} sessionMode="plan" sessionModel="sonnet" />)

      const textarea = screen.getByPlaceholderText(/message/i)
      await user.type(textarea, 'Hello{Enter}')

      expect(mockOnSend).toHaveBeenCalledWith('Hello', 'plan', 'sonnet')
    })

    it('does not send when Shift+Enter is pressed (newline)', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} />)

      const textarea = screen.getByPlaceholderText(/message/i)
      await user.type(textarea, 'Line 1{Shift>}{Enter}{/Shift}Line 2')

      expect(mockOnSend).not.toHaveBeenCalled()
      expect(textarea).toHaveValue('Line 1\nLine 2')
    })

    it('does not send empty messages', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} />)

      // Pressing Enter with an empty composer should not fire onSend (AUI's
      // composer.canSend is false when the input is empty).
      await user.click(screen.getByPlaceholderText(/message/i))
      await user.keyboard('{Enter}')

      // Clicking the send button with empty input also should not fire onSend.
      await user.click(screen.getByRole('button', { name: /send/i }))

      expect(mockOnSend).not.toHaveBeenCalled()
    })
  })

  describe('disabled state', () => {
    it('disables the textarea when disabled', () => {
      renderInput(<ChatInput {...defaultProps} disabled />)

      expect(screen.getByPlaceholderText(/message/i)).toBeDisabled()
    })
  })

  describe('loading state', () => {
    it('shows the loading spinner when isLoading is true', () => {
      renderInput(<ChatInput {...defaultProps} isLoading />)

      expect(screen.getByTestId('send-loading')).toBeInTheDocument()
    })
  })

  describe('mode tabs', () => {
    it('shows Build active when sessionMode=build', () => {
      renderInput(<ChatInput {...defaultProps} sessionMode="build" />)

      const buildTab = screen.getByRole('tab', { name: /build/i })
      expect(buildTab).toHaveAttribute('data-state', 'active')
    })

    it('shows Plan active when sessionMode=plan', () => {
      renderInput(<ChatInput {...defaultProps} sessionMode="plan" />)

      const planTab = screen.getByRole('tab', { name: /plan/i })
      expect(planTab).toHaveAttribute('data-state', 'active')
    })

    it('calls onModeChange("plan") when Plan tab is clicked from Build', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} sessionMode="build" />)

      await user.click(screen.getByRole('tab', { name: /plan/i }))
      expect(mockOnModeChange).toHaveBeenCalledWith('plan')
    })

    it('calls onModeChange("build") when Build tab is clicked from Plan', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} sessionMode="plan" />)

      await user.click(screen.getByRole('tab', { name: /build/i }))
      expect(mockOnModeChange).toHaveBeenCalledWith('build')
    })
  })

  describe('keyboard shortcuts', () => {
    it('toggles from Build to Plan on Shift+Tab in textarea', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} sessionMode="build" />)

      const textarea = screen.getByPlaceholderText(/message/i)
      await user.click(textarea)
      await user.keyboard('{Shift>}{Tab}{/Shift}')

      expect(mockOnModeChange).toHaveBeenCalledWith('plan')
    })

    it('toggles from Plan to Build on Shift+Tab in textarea', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} sessionMode="plan" />)

      const textarea = screen.getByPlaceholderText(/message/i)
      await user.click(textarea)
      await user.keyboard('{Shift>}{Tab}{/Shift}')

      expect(mockOnModeChange).toHaveBeenCalledWith('build')
    })
  })

  describe('model selector', () => {
    it('lists models from useAvailableModels and dispatches onModelChange', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} sessionModel="opus" />)

      await user.click(screen.getByRole('combobox', { name: /model/i }))
      const sonnetOption = await screen.findByRole('option', { name: /sonnet/i })
      await user.click(sonnetOption)

      expect(mockOnModelChange).toHaveBeenCalledWith('sonnet')
    })
  })

  describe('mention popover (`@`)', () => {
    it('opens a popover when `@` is typed and inserts a directive on selection', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} projectId="proj-1" />)

      const textarea = screen.getByPlaceholderText(/message/i)
      await user.click(textarea)
      await user.type(textarea, '@README')

      // AUI's TriggerPopoverItem button renders with role="option".
      const item = await screen.findByRole('option', { name: /README\.md/i })
      await user.click(item)

      // Our DirectiveFormatter serializes file mentions as `@path` (or `@"path"`
      // if the path contains spaces).
      expect((textarea as HTMLTextAreaElement).value).toContain('@README.md')
    })
  })

  describe('slash popover (`/`)', () => {
    it('opens an empty-state popover when `/` is typed', async () => {
      const user = userEvent.setup()
      renderInput(<ChatInput {...defaultProps} />)

      const textarea = screen.getByPlaceholderText(/message/i)
      await user.click(textarea)
      await user.type(textarea, '/')

      const empty = await screen.findByTestId('slash-empty-state')
      expect(empty).toHaveTextContent(/no commands available yet/i)
    })
  })

  describe('legacy expectations', () => {
    it('does not render a <form> element with a custom requestSubmit shell', () => {
      renderInput(<ChatInput {...defaultProps} />)
      // ComposerPrimitive.Root produces its own <form>, but our old custom <form>
      // shell with the gap-2 layout glue should not exist as a separate element.
      // (Sanity check: the AUI form is exactly one form element — the composer root.)
      expect(document.querySelectorAll('form').length).toBe(1)
    })

    it('does not use the legacy DropdownMenu trigger for model', () => {
      renderInput(<ChatInput {...defaultProps} />)
      expect(screen.queryByRole('menuitem')).toBeNull()
    })
  })
})

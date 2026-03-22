import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { GlobalSessionsProvider } from './global-sessions-provider'
import * as signalrProvider from '@/providers/signalr-provider'

// Mock the signalr provider
vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(),
}))

// Mock the sessions hooks
vi.mock('@/features/sessions/hooks/use-sessions', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/features/sessions/hooks/use-sessions')>()
  return {
    ...actual,
    invalidateAllSessionsQueries: vi.fn(),
    invalidateTaskGraphQueries: vi.fn(),
  }
})

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

describe('GlobalSessionsProvider', () => {
  it('renders children correctly', () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: null,
      isConnected: false,
      status: 'disconnected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    const Wrapper = createWrapper()

    render(
      <Wrapper>
        <GlobalSessionsProvider>
          <div data-testid="child">Child content</div>
        </GlobalSessionsProvider>
      </Wrapper>
    )

    expect(screen.getByTestId('child')).toBeInTheDocument()
    expect(screen.getByText('Child content')).toBeInTheDocument()
  })

  it('renders multiple children', () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: null,
      isConnected: false,
      status: 'disconnected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    const Wrapper = createWrapper()

    render(
      <Wrapper>
        <GlobalSessionsProvider>
          <div data-testid="child1">First child</div>
          <div data-testid="child2">Second child</div>
        </GlobalSessionsProvider>
      </Wrapper>
    )

    expect(screen.getByTestId('child1')).toBeInTheDocument()
    expect(screen.getByTestId('child2')).toBeInTheDocument()
  })
})

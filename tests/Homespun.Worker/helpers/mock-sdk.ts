import type {
  SDKMessage,
  Query,
  PermissionResult,
  SDKUserMessage,
} from '@anthropic-ai/claude-agent-sdk';

/**
 * The error message the real `@anthropic-ai/claude-agent-sdk` `ProcessTransport`
 * throws when a write is attempted after `endInput()` has been called. Kept in
 * sync with `sdk.mjs`'s own throw string so tests observe the same failure
 * shape as production.
 */
export const TRANSPORT_NOT_READY_ERROR =
  'ProcessTransport is not ready for writing';

export interface MockQuery extends Query {
  setPermissionMode: ReturnType<typeof vi.fn>;
  _iterator: AsyncGenerator<SDKMessage>;
  _messages: SDKMessage[];
  /**
   * Simulate the real SDK's `endInput()` happening — any subsequent
   * write/control call must throw. Used by tests that want to assert the
   * session-manager never invokes streamInput / setPermissionMode / setModel
   * on a Query whose initial iterable has already completed.
   */
  _simulateInputEnded(): void;
  /**
   * Push a message into the mock's injected input queue. Tests use this
   * instead of `q.streamInput(...)` when they want to exercise the
   * session-manager's persistent-input-queue path.
   */
  _pushInput(msg: SDKUserMessage): void;
}

// Mock V1 Query object that acts as an async generator. Mirrors the real
// sdk.mjs contract: once the input iterable passed to `query({prompt})` is
// exhausted, the transport throws on any further streamInput / setPermissionMode
// / setModel call. This guards against regressions that feed the SDK a finite
// iterable and then call streamInput for follow-ups (the #776 failure mode
// that this OpenSpec change addresses).
export function createMockQuery(messages: SDKMessage[] = []): MockQuery {
  let inputEnded = false;
  const failIfInputEnded = (op: string) => {
    if (inputEnded) {
      throw new Error(`${TRANSPORT_NOT_READY_ERROR}: ${op}`);
    }
  };

  const mockQuery = {
    setPermissionMode: vi.fn(async (..._args: unknown[]) => {
      failIfInputEnded('setPermissionMode');
    }),
    interrupt: vi.fn().mockResolvedValue(undefined),
    rewindFiles: vi.fn().mockResolvedValue(undefined),
    setModel: vi.fn(async (..._args: unknown[]) => {
      failIfInputEnded('setModel');
    }),
    setMaxThinkingTokens: vi.fn().mockResolvedValue(undefined),
    supportedCommands: vi.fn().mockResolvedValue([]),
    supportedModels: vi.fn().mockResolvedValue([]),
    mcpServerStatus: vi.fn().mockResolvedValue([]),
    accountInfo: vi.fn().mockResolvedValue({}),
    streamInput: vi.fn(async (..._args: unknown[]) => {
      failIfInputEnded('streamInput');
    }),
    close: vi.fn(),
    _messages: messages,
    _iterator: null as unknown as AsyncGenerator<SDKMessage>,
    _simulateInputEnded(): void {
      inputEnded = true;
    },
    _pushInput(_msg: SDKUserMessage): void {
      // Default no-op; test may override to capture pushed messages.
    },
  };

  // Make it async iterable
  (mockQuery as unknown as { [Symbol.asyncIterator]: () => AsyncGenerator<SDKMessage> })[Symbol.asyncIterator] =
    function* () {
      for (const msg of this._messages) {
        yield msg;
      }
    } as unknown as () => AsyncGenerator<SDKMessage>;

  return mockQuery as unknown as MockQuery;
}

// Helper to set messages on a mock query
export function setMockQueryMessages(query: MockQuery, messages: SDKMessage[]): void {
  query._messages = messages;
}

/**
 * Creates a mock query that never completes (blocks forever).
 * Useful for tests that need to verify status before the forwarder finishes.
 */
export function createBlockingMockQuery(): MockQuery {
  const mockQuery = createMockQuery();
  (mockQuery as unknown as { [Symbol.asyncIterator]: () => AsyncGenerator<SDKMessage> })[Symbol.asyncIterator] =
    async function* () {
      // Block forever — never yield, never return
      await new Promise(() => {});
    } as unknown as () => AsyncGenerator<SDKMessage>;
  return mockQuery;
}

// For backward compatibility with existing tests
export interface MockSDKSession {
  send: ReturnType<typeof vi.fn>;
  stream: ReturnType<typeof vi.fn>;
  close: ReturnType<typeof vi.fn>;
  query: MockQuery;
}

export function createMockSDKSession(): MockSDKSession {
  return {
    send: vi.fn().mockResolvedValue(undefined),
    stream: vi.fn().mockReturnValue((async function* () {})()),
    close: vi.fn(),
    query: createMockQuery(),
  };
}

export function mockStreamFromMessages(
  session: MockSDKSession,
  messages: SDKMessage[],
): void {
  session.stream.mockReturnValue(
    (async function* () {
      for (const msg of messages) {
        yield msg;
      }
    })(),
  );
}

// Mock canUseTool callback type for testing
export type MockCanUseTool = (
  toolName: string,
  input: Record<string, unknown>,
) => Promise<PermissionResult>;

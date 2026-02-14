import type { SDKMessage, Query, PermissionResult } from '@anthropic-ai/claude-agent-sdk';

export interface MockQuery extends Query {
  setPermissionMode: ReturnType<typeof vi.fn>;
  _iterator: AsyncGenerator<SDKMessage>;
  _messages: SDKMessage[];
}

// Mock V1 Query object that acts as an async generator
export function createMockQuery(messages: SDKMessage[] = []): MockQuery {
  const mockQuery = {
    setPermissionMode: vi.fn().mockResolvedValue(undefined),
    interrupt: vi.fn().mockResolvedValue(undefined),
    rewindFiles: vi.fn().mockResolvedValue(undefined),
    setModel: vi.fn().mockResolvedValue(undefined),
    setMaxThinkingTokens: vi.fn().mockResolvedValue(undefined),
    supportedCommands: vi.fn().mockResolvedValue([]),
    supportedModels: vi.fn().mockResolvedValue([]),
    mcpServerStatus: vi.fn().mockResolvedValue([]),
    accountInfo: vi.fn().mockResolvedValue({}),
    _messages: messages,
    _iterator: null as any,
  };

  // Make it async iterable
  (mockQuery as any)[Symbol.asyncIterator] = function* () {
    for (const msg of this._messages) {
      yield msg;
    }
  };

  return mockQuery as MockQuery;
}

// Helper to set messages on a mock query
export function setMockQueryMessages(query: MockQuery, messages: SDKMessage[]): void {
  query._messages = messages;
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

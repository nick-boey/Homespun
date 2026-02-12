import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';

export interface MockQuery extends AsyncIterable<SDKMessage> {
  setPermissionMode: ReturnType<typeof vi.fn>;
  [Symbol.asyncIterator]: () => AsyncIterator<SDKMessage>;
}

export interface MockSDKSession {
  send: ReturnType<typeof vi.fn>;
  stream: ReturnType<typeof vi.fn>;
  close: ReturnType<typeof vi.fn>;
  query: MockQuery;
}

export function createMockSDKSession(): MockSDKSession {
  const query = {
    setPermissionMode: vi.fn().mockResolvedValue(undefined),
    [Symbol.asyncIterator]: () => ({
      next: async () => ({ done: true, value: undefined as any }),
    }),
  } as MockQuery;

  return {
    send: vi.fn().mockResolvedValue(undefined),
    stream: vi.fn().mockReturnValue((async function* () {})()),
    close: vi.fn(),
    query,
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

export function mockQueryFromMessages(
  query: MockQuery,
  messages: SDKMessage[],
): void {
  (query as any)[Symbol.asyncIterator] = () => {
    let index = 0;
    return {
      async next() {
        if (index < messages.length) {
          return { done: false, value: messages[index++] };
        }
        return { done: true, value: undefined as any };
      },
    };
  };
}

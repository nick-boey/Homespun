import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';

export interface MockSDKSession {
  send: ReturnType<typeof vi.fn>;
  stream: ReturnType<typeof vi.fn>;
  close: ReturnType<typeof vi.fn>;
}

export function createMockSDKSession(): MockSDKSession {
  return {
    send: vi.fn().mockResolvedValue(undefined),
    stream: vi.fn().mockReturnValue((async function* () {})()),
    close: vi.fn(),
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

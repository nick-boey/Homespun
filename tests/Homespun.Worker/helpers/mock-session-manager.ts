import type { SessionManager } from '#src/services/session-manager.js';

export type MockSessionManager = {
  [K in keyof SessionManager]: ReturnType<typeof vi.fn>;
};

export function createMockSessionManager(): MockSessionManager {
  return {
    create: vi.fn(),
    send: vi.fn(),
    stream: vi.fn(),
    close: vi.fn(),
    get: vi.fn(),
    list: vi.fn().mockReturnValue([]),
    closeAll: vi.fn().mockResolvedValue(undefined),
  };
}

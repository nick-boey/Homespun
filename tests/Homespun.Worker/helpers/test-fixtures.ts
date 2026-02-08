import type { SDKMessage } from '@anthropic-ai/claude-agent-sdk';

export function createSystemMessage(overrides: Partial<SDKMessage> = {}): SDKMessage {
  return {
    type: 'system',
    session_id: 'test-conversation-id',
    ...overrides,
  } as SDKMessage;
}

export function createAssistantMessage(overrides: Partial<SDKMessage> = {}): SDKMessage {
  return {
    type: 'assistant',
    session_id: 'test-conversation-id',
    message: { role: 'assistant', content: [{ type: 'text', text: 'Hello' }] },
    ...overrides,
  } as SDKMessage;
}

export function createResultMessage(overrides: Partial<SDKMessage> = {}): SDKMessage {
  return {
    type: 'result',
    session_id: 'test-conversation-id',
    result: 'Task completed.',
    ...overrides,
  } as SDKMessage;
}

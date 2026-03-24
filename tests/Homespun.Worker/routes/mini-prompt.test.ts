import { vi, beforeEach, type Mock } from 'vitest';
import { createMiniPromptRoute } from '#src/routes/mini-prompt.js';

// Mock the Claude Agent SDK query function
const mockQuery = vi.fn();
vi.mock('@anthropic-ai/claude-agent-sdk', () => ({
  query: (...args: any[]) => mockQuery(...args),
}));

// Capture logger calls
const mockInfo = vi.fn();
const mockError = vi.fn();
vi.mock('#src/utils/logger.js', () => ({
  info: (...args: any[]) => mockInfo(...args),
  error: (...args: any[]) => mockError(...args),
}));

describe('POST /mini-prompt', () => {
  let miniPromptRoute: ReturnType<typeof createMiniPromptRoute>;

  beforeEach(() => {
    vi.clearAllMocks();
    miniPromptRoute = createMiniPromptRoute();
  });

  function setupMockQuery(events: any[]) {
    mockQuery.mockReturnValue({
      [Symbol.asyncIterator]: () => {
        let i = 0;
        return {
          async next() {
            if (i < events.length) {
              return { value: events[i++], done: false };
            }
            return { value: undefined, done: true };
          },
        };
      },
    });
  }

  it('returns resolvedModel in the response when system init event is received', async () => {
    setupMockQuery([
      { type: 'system', subtype: 'init', model: 'claude-haiku-4-5-20251001' },
      {
        type: 'assistant',
        message: { content: [{ type: 'text', text: 'test-response' }] },
      },
      { type: 'result', total_cost_usd: 0.0001 },
    ]);

    const res = await miniPromptRoute.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: 'Generate a branch ID', model: 'haiku' }),
    });

    const data = await res.json();
    expect(res.status).toBe(200);
    expect(data.success).toBe(true);
    expect(data.response).toBe('test-response');
    expect(data.resolvedModel).toBe('claude-haiku-4-5-20251001');
  });

  it('returns undefined resolvedModel when no system init event', async () => {
    setupMockQuery([
      {
        type: 'assistant',
        message: { content: [{ type: 'text', text: 'test-response' }] },
      },
      { type: 'result', total_cost_usd: 0.0001 },
    ]);

    const res = await miniPromptRoute.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: 'Generate a branch ID' }),
    });

    const data = await res.json();
    expect(res.status).toBe(200);
    expect(data.success).toBe(true);
    expect(data.resolvedModel).toBeUndefined();
  });

  it('logs prompt content at info level', async () => {
    setupMockQuery([
      { type: 'result', total_cost_usd: 0 },
    ]);

    await miniPromptRoute.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: 'Generate a branch ID for testing' }),
    });

    // Verify prompt content is logged
    const promptLogCall = mockInfo.mock.calls.find(
      (call: any[]) => typeof call[0] === 'string' && call[0].includes('prompt: Generate a branch ID for testing')
    );
    expect(promptLogCall).toBeDefined();
  });

  it('logs requestedModel and resolvedModel on completion', async () => {
    setupMockQuery([
      { type: 'system', subtype: 'init', model: 'claude-haiku-4-5-20251001' },
      {
        type: 'assistant',
        message: { content: [{ type: 'text', text: 'response' }] },
      },
      { type: 'result', total_cost_usd: 0.001 },
    ]);

    await miniPromptRoute.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: 'test prompt', model: 'haiku' }),
    });

    // Verify completion log includes model info
    const completionLogCall = mockInfo.mock.calls.find(
      (call: any[]) =>
        typeof call[0] === 'string' &&
        call[0].includes('requestedModel=haiku') &&
        call[0].includes('resolvedModel=claude-haiku-4-5-20251001')
    );
    expect(completionLogCall).toBeDefined();
  });

  it('truncates long prompts in log to 500 chars', async () => {
    const longPrompt = 'x'.repeat(600);
    setupMockQuery([
      { type: 'result', total_cost_usd: 0 },
    ]);

    await miniPromptRoute.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: longPrompt }),
    });

    const promptLogCall = mockInfo.mock.calls.find(
      (call: any[]) => typeof call[0] === 'string' && call[0].includes('prompt: ')
    );
    expect(promptLogCall).toBeDefined();
    // Should be truncated with '...'
    expect(promptLogCall![0]).toContain('...');
    // Should not contain the full 600-char prompt
    expect(promptLogCall![0].length).toBeLessThan(600);
  });

  it('returns 400 for empty prompt', async () => {
    const res = await miniPromptRoute.request('/', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: '' }),
    });

    expect(res.status).toBe(400);
    const data = await res.json();
    expect(data.success).toBe(false);
    expect(data.error).toBe('Prompt is required');
  });
});

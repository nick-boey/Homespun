import { Hono } from 'hono';
import { query } from '@anthropic-ai/claude-agent-sdk';
import { info, error } from '../utils/logger.js';

/**
 * Request body for mini prompts.
 */
interface MiniPromptRequest {
  prompt: string;
  model?: string;
}

/**
 * Response body for mini prompts.
 */
interface MiniPromptResponse {
  success: boolean;
  response?: string;
  error?: string;
  costUsd?: number;
  durationMs?: number;
}

/**
 * Creates a mini-prompt route handler.
 * Mini prompts are lightweight, tool-free AI completions for simple tasks
 * like branch ID generation and title suggestions.
 */
export function createMiniPromptRoute() {
  const miniPrompt = new Hono();

  miniPrompt.post('/', async (c) => {
    const startTime = Date.now();
    const body = await c.req.json<MiniPromptRequest>();

    if (!body.prompt || body.prompt.trim().length === 0) {
      return c.json<MiniPromptResponse>({
        success: false,
        error: 'Prompt is required',
        durationMs: Date.now() - startTime,
      }, 400);
    }

    const model = body.model || 'haiku';
    info(`POST /mini-prompt - model=${model}, promptLength=${body.prompt.length}`);

    try {
      // Create a single-message async generator for the prompt
      async function* createPromptStream() {
        yield {
          type: 'user' as const,
          session_id: '',
          message: {
            role: 'user' as const,
            content: [{ type: 'text' as const, text: body.prompt }],
          },
          parent_tool_use_id: null,
        };
      }

      // Run query with no tools and single turn
      const q = query({
        prompt: createPromptStream(),
        options: {
          model,
          cwd: process.cwd(),
          permissionMode: 'bypassPermissions',
          allowDangerouslySkipPermissions: true,
          allowedTools: [], // No tools for mini prompts
          maxTurns: 1, // Single turn only
          includePartialMessages: false,
        },
      });

      // Collect the response
      let responseText = '';
      let totalCostUsd = 0;

      for await (const event of q) {
        if (event.type === 'assistant') {
          // Extract text from assistant message content
          const content = event.message?.content;
          if (Array.isArray(content)) {
            for (const block of content) {
              if (block.type === 'text' && typeof block.text === 'string') {
                responseText += block.text;
              }
            }
          }
        } else if (event.type === 'result') {
          // Extract cost from result
          if (typeof event.total_cost_usd === 'number') {
            totalCostUsd = event.total_cost_usd;
          }
        }
      }

      const durationMs = Date.now() - startTime;
      info(`Mini-prompt completed in ${durationMs}ms, cost: $${totalCostUsd.toFixed(6)}`);

      return c.json<MiniPromptResponse>({
        success: responseText.length > 0,
        response: responseText || undefined,
        error: responseText.length === 0 ? 'Empty response from AI' : undefined,
        costUsd: totalCostUsd > 0 ? totalCostUsd : undefined,
        durationMs,
      });
    } catch (err) {
      const durationMs = Date.now() - startTime;
      const message = err instanceof Error ? err.message : String(err);
      error(`Mini-prompt failed after ${durationMs}ms: ${message}`);

      return c.json<MiniPromptResponse>({
        success: false,
        error: message,
        durationMs,
      }, 500);
    }
  });

  return miniPrompt;
}

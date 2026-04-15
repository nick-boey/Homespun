/**
 * spike-idle-tolerance.ts — OpenSpec `simplify-worker-session-manager`, tasks 1.1–1.3
 *
 * Verifies that one long-lived `query()` from `@anthropic-ai/claude-agent-sdk`
 * does not self-terminate during long idle periods in streaming-input mode.
 *
 * Flow: start query -> drain until first `result` -> idle IDLE_SECONDS seconds
 * (default 600) -> push a follow-up via `q.streamInput(asyncIterableOfOne(msg))`
 * -> drain until a second `result`. Prints FOLLOW-UP SUCCESS or FAILURE.
 *
 * Run from `src/Homespun.Worker` (requires ANTHROPIC_API_KEY):
 *   ANTHROPIC_API_KEY=sk-ant-... IDLE_SECONDS=600 npx tsx scripts/spike-idle-tolerance.ts
 * If `claude: not found`, run inside the worker container so the CLI resolves.
 *
 * Pass criteria: "FIRST RESULT" arrives; no mid-idle errors; after idle the
 * follow-up echoes assistant text then "FOLLOW-UP SUCCESS".
 *
 * If it fails (streamInput throws or stream ends with no 2nd result):
 *   - add a keep-alive task to tasks.md (synthetic no-op under the observed
 *     timeout) BEFORE landing the refactor; or
 *   - fall back to "selective resume": close-and-resume only when idle crosses
 *     the observed threshold.
 * Record the observed tolerance in design.md under Open Questions.
 */

import {
  query,
  type PermissionResult,
  type SDKUserMessage,
} from "@anthropic-ai/claude-agent-sdk";

const IDLE_SECONDS = Number.parseInt(process.env.IDLE_SECONDS ?? "600", 10);
const MODEL = "claude-haiku-4-5-20251001";

const allowAll = async (
  toolName: string,
  input: Record<string, unknown>,
): Promise<PermissionResult> => ({
  behavior: "allow",
  updatedInput: input,
});

function userMessage(text: string): SDKUserMessage {
  return {
    type: "user",
    session_id: "",
    parent_tool_use_id: null,
    message: { role: "user", content: [{ type: "text", text }] },
  };
}

async function* once(msg: SDKUserMessage): AsyncGenerator<SDKUserMessage> {
  yield msg;
}

async function drainUntilResult(
  q: AsyncIterable<unknown>,
  label: string,
): Promise<void> {
  for await (const msg of q) {
    const m = msg as { type?: string; subtype?: string };
    console.log(`[${label}] msg type=${m.type}${m.subtype ? ` subtype=${m.subtype}` : ""}`);
    if (m.type === "result") {
      console.log(`[${label}] got result — returning`);
      return;
    }
  }
  throw new Error(`[${label}] stream ended before a result message arrived`);
}

async function main() {
  console.log(`spike-idle-tolerance: IDLE_SECONDS=${IDLE_SECONDS}, model=${MODEL}`);

  const q = query({
    prompt: once(userMessage("Say hi in one short sentence.")),
    options: {
      model: MODEL,
      permissionMode: "default",
      canUseTool: allowAll,
      includePartialMessages: false,
    },
  });

  console.log("waiting for FIRST RESULT...");
  await drainUntilResult(q, "turn1");
  console.log("FIRST RESULT received");

  const idleMs = IDLE_SECONDS * 1000;
  const startedAt = Date.now();
  console.log(`idling for ${IDLE_SECONDS}s...`);
  await new Promise<void>((resolve) => setTimeout(resolve, idleMs));
  console.log(`idle complete after ${Math.round((Date.now() - startedAt) / 1000)}s`);

  try {
    await q.streamInput(once(userMessage("Respond with the single word 'alive'.")));
    console.log("streamInput accepted; waiting for SECOND RESULT...");
    await drainUntilResult(q, "turn2");
    console.log("FOLLOW-UP SUCCESS");
  } catch (err) {
    const msg = err instanceof Error ? `${err.name}: ${err.message}` : String(err);
    console.error(`FOLLOW-UP FAILURE: ${msg}`);
    if (err instanceof Error && err.stack) console.error(err.stack);
    process.exitCode = 1;
  } finally {
    try {
      await (q as unknown as { return?: () => Promise<unknown> }).return?.();
    } catch {
      /* ignore cleanup errors */
    }
  }
}

main().catch((err) => {
  console.error("spike crashed:", err);
  process.exit(1);
});

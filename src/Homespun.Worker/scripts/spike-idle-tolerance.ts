/**
 * spike-idle-tolerance.ts — OpenSpec `fix-worker-streaminput-multi-turn`, tasks 1.1–1.3
 *
 * Regression probe for the SDK `streamInput` + `endInput` behavior that broke
 * multi-turn sessions after PR #776 (`simplify-worker-session-manager`).
 *
 * ## Finding
 *
 * `@anthropic-ai/claude-agent-sdk@0.2.81` (`sdk.mjs`) always calls
 * `transport.endInput()` at the tail of every `streamInput` invocation:
 *
 *     for await (const msg of stream) { transport.write(msg) }
 *     if (count > 0 && hasBidirectionalNeeds()) await waitForFirstResult()
 *     transport.endInput()   // closes stdin to CLI — CLI exits
 *
 * The initial `AsyncIterable` passed to `query({ prompt })` is consumed via the
 * same `streamInput` routine internally, so even a single-shot initial prompt
 * (e.g. `onceIterator(initialPrompt)`) triggers `endInput()` once it returns.
 * Any subsequent `streamInput`, `setPermissionMode`, or `setModel` on the
 * `Query` then throws `ProcessTransport is not ready for writing`.
 *
 * ## Fix shape (INPUT_MODE=queue)
 *
 * A persistent async input queue whose iterator never returns until session
 * close is passed as the `prompt`. The initial message is `push(...)`-ed into
 * the queue; follow-ups are `push(...)`-ed as well. The SDK's internal
 * `streamInput` stays suspended on `await` between pushes and never reaches
 * `endInput()`, so the CLI process survives for the session's lifetime.
 *
 * ## Flow
 *
 * start query -> drain until first `result` -> idle IDLE_SECONDS seconds
 * (default 600) -> deliver a follow-up (via `inputQueue.push(msg)` when
 * `INPUT_MODE=queue`, or via `q.streamInput(once(msg))` when
 * `INPUT_MODE=stream`) -> drain until a second `result`. Prints
 * `FOLLOW-UP SUCCESS` on the queue path and `FOLLOW-UP FAILURE` on the stream
 * path (the historical failure mode that motivated this change).
 *
 * ## Envs
 *
 * - `INPUT_MODE=queue|stream` (default `stream` to mirror the pre-fix failure)
 * - `IDLE_SECONDS` (default 600)
 * - `MODEL` (default `claude-haiku-4-5-20251001`)
 * - `PERMISSION_MODE` (default `default`) — set to `bypassPermissions` to
 *   reproduce Build mode
 * - `DANGEROUS_SKIP=true` — sets `allowDangerouslySkipPermissions`
 * - `SET_MODE_ON_TURN2=true` — calls `setPermissionMode` before the follow-up
 *   to exercise the full production send() path
 *
 * ## Run
 *
 *   CLAUDE_CODE_OAUTH_TOKEN=... INPUT_MODE=queue IDLE_SECONDS=3 \
 *     npx tsx scripts/spike-idle-tolerance.ts
 *
 * If `claude: not found`, run inside the worker container so the CLI resolves.
 *
 * Retain this script as a regression probe: if a future SDK upgrade alters the
 * `streamInput`/`endInput` behavior, running this script in both modes is the
 * shortest-path reproduction.
 */

import {
  query,
  type PermissionResult,
  type SDKUserMessage,
  type PermissionMode,
} from "@anthropic-ai/claude-agent-sdk";

const IDLE_SECONDS = Number.parseInt(process.env.IDLE_SECONDS ?? "600", 10);
const MODEL = process.env.MODEL ?? "claude-haiku-4-5-20251001";
const PERMISSION_MODE = (process.env.PERMISSION_MODE ?? "default") as PermissionMode;
const DANGEROUS_SKIP = process.env.DANGEROUS_SKIP === "true";
// When SET_MODE_ON_TURN2=true, exercises the real send() path that
// calls setPermissionMode before streamInput on the follow-up turn.
const SET_MODE_ON_TURN2 = process.env.SET_MODE_ON_TURN2 === "true";
// INPUT_MODE=queue uses a persistent async-queue as the initial prompt and
// pushes follow-ups through it — avoiding the SDK's `endInput()` that fires
// at the tail of every streamInput() call. Default "stream" matches the
// current session-manager.ts behavior (onceIterator + q.streamInput).
const INPUT_MODE = (process.env.INPUT_MODE ?? "stream") as "stream" | "queue";

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

/**
 * Persistent async input queue — the iterator NEVER returns on its own, so
 * the SDK's internal `streamInput` never calls `endInput()` (which would
 * close stdin and terminate the CLI). Follow-up messages are pushed in via
 * `push(...)` rather than a fresh `q.streamInput(...)` call.
 */
class InputQueue implements AsyncIterable<SDKUserMessage> {
  private buffer: SDKUserMessage[] = [];
  private resolve: ((msg: IteratorResult<SDKUserMessage>) => void) | null = null;
  private closed = false;

  push(msg: SDKUserMessage): void {
    if (this.closed) return;
    if (this.resolve) {
      const r = this.resolve;
      this.resolve = null;
      r({ value: msg, done: false });
    } else {
      this.buffer.push(msg);
    }
  }

  close(): void {
    this.closed = true;
    if (this.resolve) {
      const r = this.resolve;
      this.resolve = null;
      r({ value: undefined as unknown as SDKUserMessage, done: true });
    }
  }

  async *[Symbol.asyncIterator](): AsyncGenerator<SDKUserMessage> {
    while (true) {
      if (this.buffer.length > 0) {
        yield this.buffer.shift()!;
        continue;
      }
      if (this.closed) return;
      const next = await new Promise<IteratorResult<SDKUserMessage>>((res) => {
        this.resolve = res;
      });
      if (next.done) return;
      yield next.value;
    }
  }
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
  console.log(
    `spike-idle-tolerance: IDLE_SECONDS=${IDLE_SECONDS}, model=${MODEL}, ` +
      `permissionMode=${PERMISSION_MODE}, dangerousSkip=${DANGEROUS_SKIP}, ` +
      `setModeOnTurn2=${SET_MODE_ON_TURN2}`,
  );

  const options: Record<string, unknown> = {
    model: MODEL,
    permissionMode: PERMISSION_MODE,
    canUseTool: allowAll,
    includePartialMessages: false,
  };
  if (DANGEROUS_SKIP) {
    options.allowDangerouslySkipPermissions = true;
  }

  const inputQueue = INPUT_MODE === "queue" ? new InputQueue() : null;
  let prompt: AsyncIterable<SDKUserMessage>;
  const initialMsg = userMessage("Say hi in one short sentence.");
  if (inputQueue) {
    inputQueue.push(initialMsg);
    prompt = inputQueue;
  } else {
    prompt = once(initialMsg);
  }

  const q = query({
    prompt,
    options: options as Parameters<typeof query>[0]["options"],
  });

  try {
    // Single continuous drain across both turns. Exiting the for-await
    // between turns would call iterator.return() which marks the SDK's
    // inner inputStream as done — making a second for-await yield nothing.
    let resultsSeen = 0;
    let followupScheduled = false;
    for await (const msg of q) {
      const m = msg as { type?: string; subtype?: string };
      const label = resultsSeen === 0 ? "turn1" : "turn2";
      console.log(`[${label}] msg type=${m.type}${m.subtype ? ` subtype=${m.subtype}` : ""}`);
      if (m.type === "result") {
        resultsSeen++;
        if (resultsSeen === 1) {
          console.log("FIRST RESULT received");
          const idleMs = IDLE_SECONDS * 1000;
          const startedAt = Date.now();
          console.log(`idling for ${IDLE_SECONDS}s...`);
          await new Promise<void>((resolve) => setTimeout(resolve, idleMs));
          console.log(`idle complete after ${Math.round((Date.now() - startedAt) / 1000)}s`);

          if (SET_MODE_ON_TURN2) {
            console.log(`calling setPermissionMode('${PERMISSION_MODE}')...`);
            await q.setPermissionMode(PERMISSION_MODE);
            console.log("setPermissionMode accepted");
          }
          const followupMsg = userMessage("Respond with the single word 'alive'.");
          if (inputQueue) {
            console.log("pushing follow-up through persistent input queue...");
            inputQueue.push(followupMsg);
          } else {
            console.log("calling q.streamInput(once(followup))...");
            // Fire-and-forget so we keep draining; streamInput awaits
            // waitForFirstResult internally which would deadlock here.
            q.streamInput(once(followupMsg)).catch((e) => {
              console.error(`streamInput rejected: ${e instanceof Error ? e.message : e}`);
            });
          }
          followupScheduled = true;
          continue;
        }
        if (resultsSeen === 2) {
          console.log("FOLLOW-UP SUCCESS");
          break;
        }
      }
    }
    if (resultsSeen < 2) {
      throw new Error(
        `drain ended before second result (seen=${resultsSeen}, followupScheduled=${followupScheduled})`,
      );
    }
  } catch (err) {
    const msg = err instanceof Error ? `${err.name}: ${err.message}` : String(err);
    console.error(`FOLLOW-UP FAILURE: ${msg}`);
    if (err instanceof Error && err.stack) console.error(err.stack);
    process.exitCode = 1;
  } finally {
    inputQueue?.close();
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

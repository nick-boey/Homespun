## Why

The Claude Agent SDK supports **streaming input mode**: the `query()` function accepts an `AsyncIterable<SDKUserMessage>` as its `prompt`, and the returned `Query` object exposes `streamInput(stream)`, `interrupt()`, `setPermissionMode(mode)`, and `setModel(model?)` — all "only available in streaming input mode" per the published docs (code.claude.com/docs/en/agent-sdk/typescript).

Homespun's worker already uses streaming input mode: `SessionManager` constructs a persistent `InputQueue` (line 645 of `session-manager.ts`), passes it as the `prompt` to `query()` (line 662), and appends follow-up user messages to it (line 1125) on each `SendMessage` from the server. A previous OpenSpec change (`fix-worker-streaminput-multi-turn`) explicitly established this contract.

The server's `ClaudeCodeHub.SendMessage` → `SessionService.SendMessageAsync` → `DockerAgentExecutionService.SendMessageAsync` path has no mid-run guard: a message sent while a previous turn is in flight is forwarded to the worker, which enqueues it.

**The only thing blocking mid-stream messages is a single client-side expression:**

```tsx
// src/Homespun.Web/src/routes/sessions.$sessionId.tsx:144
const isProcessing =
  session?.status === 'running' ||
  session?.status === 'runningHooks' ||
  session?.status === 'starting'

// line 412
<ChatInput ... disabled={isProcessing || !isConnected || !isJoined} />
```

Removing `isProcessing` from the `disabled` expression enables mid-stream messaging end-to-end. The feature is SDK-supported, worker-supported, server-supported, and trivially unblockable on the client.

This change ships that unblock with appropriate UX polish.

## What Changes

- **Client composer accepts input while `session.status` is `running`, `runningHooks`, or `starting`.**
  - The `isProcessing` term is removed from the `disabled` expression at `src/Homespun.Web/src/routes/sessions.$sessionId.tsx:412`.
  - The composer remains `disabled` for `!isConnected`, `!isJoined`, and — during the handoff between the pre- and post-`questions-plans-as-tools` world — potentially for `pendingPlan` / `pendingQuestion` (see Pending-interaction behaviour below).
- **Visual cue that a message is queued rather than immediately processed.**
  - When the user submits a message while the agent is running, the submitted message appears in the message list immediately (already happens — server echoes as `user.message` custom event) with a subtle "queued" annotation until the next `RUN_STARTED` arrives.
  - If the user submits multiple messages mid-run, all appear in the list in submission order, with queued annotations until each is consumed.
- **`Stop` button (optional, scoped if simple).**
  - Wire a new hub method `InterruptSession(sessionId)` that calls `q.interrupt()` on the worker's current `Query`. Add a `Stop` button visible only while `isProcessing`. Submitting a message still queues by default; `Stop` is a distinct "please stop what you're doing" action.
  - If the worker-side `interrupt()` plumbing requires non-trivial work (e.g. finding the right SDK call), descope to a follow-up. Mid-stream submit is the primary goal.
- **Pending-interaction behaviour.**
  - If `questions-plans-as-tools` has NOT merged: the composer remains disabled when a `pendingPlan` exists, because submitting a free-text message while a plan is pending has special "reject with feedback" semantics via `ApprovePlan(false, _, feedback)`. For `pendingQuestion`, the current panel handles answer submission; the composer does not take its place.
  - If `questions-plans-as-tools` has merged: plan and question interactions are tool calls; the composer can accept input during them the same as any other mid-run state. The user's message enters the queue as a normal follow-up; the pending tool call remains pending until its component commits via `addResult`.
  - The proposal explicitly handles both landing orders via the `disabled` expression's fallback terms.
- **Mode / model selection remain locked mid-run for now.**
  - The SDK supports `setPermissionMode` / `setModel` mid-run, but exposing this would add confusing UX ("your next message will use the new model") and is orthogonal to the main win. Out of scope; the composer's mode/model selectors stay hidden or locked while processing.
- **Worker side: verify-only.**
  - No code change. Add one worker test asserting that `InputQueue.push` invoked while the forwarder loop is mid-iteration delivers the message to the SDK on the next loop turn (probably already covered by `fix-worker-streaminput-multi-turn`'s test suite — verify and cite).

## Capabilities

### New Capabilities
- `mid-stream-messaging`: The client composer accepts and submits messages while the agent is running; submitted messages queue on the worker's `InputQueue` and are consumed by the SDK on the next user-turn boundary.

### Modified Capabilities
- None. This is an additive client-facing feature that relies on existing server + worker contracts unchanged.

## Impact

- **Frontend**: `src/Homespun.Web/`
  - `routes/sessions.$sessionId.tsx`: drop `isProcessing` from the composer `disabled` expression; add the queued-message annotation rendering.
  - `features/sessions/components/chat-input.tsx`: no behavioural change; optional visual polish for the "queued" state while `isProcessing` (e.g. a faint clock icon next to the Send button until `RUN_STARTED` fires for the new message).
  - Storybook: a new `ChatSurface — typing during assistant run` story (extends the fixture harness from `chat-assistant-ui`).
  - Tests: route-level test that the composer is enabled when `session.status === 'running'`.
- **Backend / Worker / Shared**: unchanged, modulo the optional `InterruptSession` hub method + its worker HTTP path.
- **Risk**: Low. End-to-end plumbing already exists and is tested independently via `fix-worker-streaminput-multi-turn`. Worst-case regression is a user sending two messages in quick succession and seeing the second arrive slightly out of intuitive order (it doesn't — FIFO queue ensures order), or confusion about whether their message was received (mitigated by the optimistic render already emitted by the server's echo event).
- **Dependencies**: Independent. Can ship before, after, or alongside `chat-assistant-ui` and `questions-plans-as-tools`. If it ships before `chat-assistant-ui`, the change lands against the current chat UI; if after, it lands against the Assistant-UI composer. The `disabled` expression edit is trivial in both worlds.

## Migration & roll-back

- **Roll-back:** re-add `isProcessing` to the `disabled` expression; one-line revert.
- **Feature flag:** not needed.
- **Observability:** existing session-event telemetry already reports every user message and RUN_STARTED / RUN_FINISHED transition. A mid-run submit will appear as a user message emitted during an isRunning=true window, traced end-to-end.

## Follow-ups (explicitly out of scope)

- Mid-run mode / model switching. SDK-supported (`setPermissionMode`, `setModel`) but UX is complicated. Separate proposal if wanted.
- True interrupt-and-redirect UX (send-message-that-also-interrupts). Today's approach is "message queues, current turn finishes, queued message processed next". If a "stop and do this instead" affordance is desired, design a distinct button and contract.
- Showing the worker's input-queue depth to the user during long runs. Mostly diagnostic; defer.

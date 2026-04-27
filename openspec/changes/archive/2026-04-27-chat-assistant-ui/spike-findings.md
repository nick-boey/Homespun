# Spike findings — `chat-assistant-ui` and follow-ups

**Date:** 2026-04-22
**Spike scope:**
1. Re-verify `@assistant-ui/react-ag-ui` maturity and architectural fit (Spike A).
2. Confirm `Toolkit` API composes cleanly with `useExternalStoreRuntime` (Spike B).
3. Confirm Claude Agent SDK's mid-stream input support and locate the actual block on mid-stream UX today (Spike C).

**Artefacts inspected:**
- `@assistant-ui/react-ag-ui@0.0.26` (latest, published 2026-04-13)
- `@assistant-ui/react@0.12.25`
- `@assistant-ui/core@0.1.14` (dependency of the above)
- `@ag-ui/client@0.0.52`

Installed into `/tmp/spike/` for type + implementation inspection.

---

## Spike A — `@assistant-ui/react-ag-ui` maturity

### Version / release cadence

- Current version: **0.0.26**. Latest publish: 2026-04-13 (9 days before this spike).
- 26 releases since 2025-11-19 — actively maintained, weekly-ish cadence.
- Still pre-1.0. Type declarations still carry `@experimental This API is still under active development and might change without notice.`
- No deprecation notice.

### Architectural fit (the load-bearing question)

**The fit is WORSE than the previous D1 analysis identified.** The previous analysis focused on eventId dedup and experimental-API risk. A deeper concern is the **run-lifecycle assumption**, which is fundamental.

How `useAgUiRuntime` works:

1. `const runtime = useAgUiRuntime({ agent: new HttpAgent({url}) })`.
2. User types a message; `runtime.append(message)` is called.
3. Runtime calls `this.agent.runAgent(input, subscriber)` — ONE HTTP+SSE request per run.
4. Subscriber dispatches AG-UI events to a **per-run** `RunAggregator`, which collects "a single assistant message worth of parts" (source: `adapter/run-aggregator.d.ts`).
5. When `agent.runAgent` resolves, the run is over. Next user input creates a new aggregator.

How Homespun sessions work:

- Worker runs **autonomously**. No per-user-message request/response boundary.
- When the user reloads a session page, the agent may be mid-run (or mid-thought, or between tool calls) with **no client-initiated `runAgent` call to anchor the event stream**.
- When the user answers a question or approves a plan, the worker resumes the **same** run — it does NOT kick off a new one.

This means `useAgUiRuntime` cannot naturally observe our sessions. Possible workarounds, all problematic:

| Workaround | Problem |
|---|---|
| Custom `IAgent` whose `runAgent` subscribes to SSE and resolves on `RUN_FINISHED` | Requires bypassing `addResult`'s auto-resume behavior (line 164 of `AgUiThreadRuntimeCore.js` calls `startRun` again when pending tool results satisfy all pending calls). Also: a page-load mid-run has no `runAgent` call to anchor. |
| Use `applyExternalMessages(messages)` publicly to import snapshot + ignore runtime's run machinery | Effectively bypasses the runtime. We'd maintain our own subscription and reducer-equivalent, defeating the purpose. |
| Make worker request/response so it fits the runtime model | Fundamental architecture change. Worker autonomy is a feature, not an incidental. Out of scope. |

### MESSAGES_SNAPSHOT handling (the good news)

`AgUiThreadRuntimeCore.handleEvent()` (line 371 of the js) does have first-class handling for `MESSAGES_SNAPSHOT`:

```js
case "MESSAGES_SNAPSHOT": {
  this.importMessagesSnapshot(event.messages);
  return;
}
```

`importMessagesSnapshot` calls `applyExternalMessages(messages)` — which REPLACES the entire message list. So if we deliver snapshot-on-connect + deltas, the runtime does the right thing for state replacement.

However, this only helps us if events are routed through the runtime's `handleEvent` path, which requires the `agent.runAgent` lifecycle (see above).

### Verdict

**`@assistant-ui/react-ag-ui`'s run-lifecycle assumption is structurally incompatible with Homespun's autonomous-worker session model.**

Implications:

- The `ag-ui-runtime-swap` proposal as written overstates feasibility.
- Amendments needed (see "Proposal amendments" below).
- The `chat-assistant-ui` D1 decision (use `useExternalStoreRuntime`, keep the reducer) stands, **strengthened** by this finding.

---

## Spike B — `Toolkit` + `useExternalStoreRuntime` compatibility

### Type-level confirmation

`AssistantRuntimeProvider` accepts `{ runtime, aui? }` as independent props (`legacy-runtime/AssistantRuntimeProvider.d.ts`):

```ts
type Props = PropsWithChildren<{
  runtime: AssistantRuntime;
  aui?: AssistantClient;
}>;
```

`runtime` is any object implementing `AssistantRuntime` — `useExternalStoreRuntime`, `useChatRuntime`, `useAgUiRuntime`, and `useLocalRuntime` all produce compatible runtimes. They are orthogonal to `aui`.

### Implementation-level confirmation

`Tools({ toolkit })` (core's `client/Tools.js`) registers tool renders via `aui.tools().setToolUI(toolName, render)`. These renders land in a `tools` state map on the aui client.

When a tool-call message part is rendered, `primitives/message/MessageParts.js:98` does:

```js
const Render = s.tools.tools[props.toolName] ?? Fallback;
```

This lookup is **runtime-agnostic**. The runtime supplies messages; the message-parts primitive looks up tool renders from the aui client's state. Any runtime that produces messages with tool-call content parts — including `useExternalStoreRuntime` — will work.

### Additional finding: `makeAssistantToolUI` and `Toolkit` share the same store

`model-context/useAssistantToolUI.js:8` calls `aui.tools().setToolUI(tool.toolName, tool.render)` — **the same underlying mechanism** that `Tools({ toolkit })` uses. The two APIs are not distinct generations; they are two surfaces over one store.

This changes the D3 rationale slightly:

- It is **not** the case that "`makeAssistantToolUI` won't plug into Tool UI components". Both go to the same store; the Tool UI registry components would render either way.
- It **is** the case that:
  - `Toolkit` is more ergonomic (one object, data-driven).
  - `Toolkit` carries `type: "backend" | "frontend" | "human"` and (for frontend) `parameters` (zod schema) for model-context registration. `makeAssistantToolUI` only takes `render`.
  - Tool UI's skill + examples are written against `Toolkit`.
  - Frontend-tool schemas — which `questions-plans-as-tools` needs for `ask_user_question` and `propose_plan` — are idiomatic under `Toolkit` only.

So D3 is still correct: **use `Toolkit`**. The rationale should emphasise ergonomics + frontend-tool schema support, not API generation.

### Built-in fallback

`MessageParts.js:98`: `?? Fallback`. AUI ships a built-in Fallback renderer for tool calls. Our spec and tasks currently talk about registering a fallback explicitly — we may not need to.

### Verdict

**Spike B is green.** `Toolkit` + `useExternalStoreRuntime` compose cleanly under one `AssistantRuntimeProvider`.

---

---

## Spike C — mid-stream user input (Claude Agent SDK)

### SDK capability (verified against the published TypeScript docs)

`query()` supports two input modes:

1. **Single-shot** — `prompt: string`. Not useful here.
2. **Streaming input** — `prompt: AsyncIterable<SDKUserMessage>`. Returns a `Query` object with mid-flight controls:
   - `streamInput(stream)` — push new user messages into the conversation.
   - `interrupt()` — stop the current assistant turn.
   - `setPermissionMode(mode)` — swap Plan/Build mid-run.
   - `setModel(model?)` — swap model mid-run.
   - `close()` — terminate.

All four controls are explicitly "only available in streaming input mode".

### Homespun worker already uses streaming input mode

Verified by reading `src/Homespun.Worker/src/services/session-manager.ts`:

- Line 645: `const inputQueue = new InputQueue();` — persistent queue supplied as the `prompt` argument to `query()` (line 662).
- Line 1125: `ws.inputQueue.push(userMessage)` — follow-up messages are appended to the same queue.
- Lines 742–745 (comment): *"Follow-up user messages are delivered by pushing into the session's persistent `InputQueue`... never via `q.streamInput(...)` — see the `fix-worker-streaminput-multi-turn` OpenSpec change for the SDK contract that motivates this design."*

There is also an `OpenSpec` change, `fix-worker-streaminput-multi-turn`, that formalised this pattern. The worker is explicitly designed for mid-stream sends.

### Server path has no mid-run guard

`ClaudeCodeHub.SendMessage(...)` (line 102) calls `SessionService.SendMessageAsync(sessionId, message, mode)` which calls through to `DockerAgentExecutionService.SendMessageAsync` (line 404). None of these paths check `isRunning` or equivalent state.

### The block is a single client-side line

`src/Homespun.Web/src/routes/sessions.$sessionId.tsx:144`:

```tsx
const isProcessing =
  session?.status === 'running' ||
  session?.status === 'runningHooks' ||
  session?.status === 'starting'
```

Applied to the composer at line 412 as `disabled={isProcessing || !isConnected || !isJoined}`.

Removing `isProcessing` from this expression unblocks mid-stream messaging end-to-end. No architectural change required.

### Interaction with the runtime-swap analysis

`useAgUiRuntime`'s aggregator is per-run. While `agent.runAgent(input, subscriber)` is in flight, the runtime has no API for pushing additional user input — no `streamInput`-equivalent. Adopting it would **re-block** mid-stream input at the runtime level, trading a one-line UI gate for a structural constraint inherited from upstream.

This is the second (independent) reason to keep `useExternalStoreRuntime` + reducer. Combined with the run-lifecycle finding from Spike A, the case against runtime-swap is now decisive.

### Verdict

**Spike C: mid-stream input is SDK-supported, worker-supported, server-supported, and blocked only in client UI.** A small dedicated change (`composer-accepts-mid-stream`) will flip the one line. Keeping `useExternalStoreRuntime` is what preserves the feature.

---

## Proposal dispositions

### `chat-assistant-ui`

**Low-risk amendments (all done in this session):**
- D3 rationale refined: "ergonomics + schema support + ecosystem alignment", not "two generations, pick one".
- D8 recorded: Spike A verdict. D1 stands.
- Phase 1 tasks 1.0 / 1.0b marked done against this memo.
- Spec simplified to use AUI's built-in tool-call fallback.

### `questions-plans-as-tools`

**No amendments needed.** The change is independent of runtime choice — translator change + Toolkit frontend-tool entries. Spike B green.

### `composer-accepts-mid-stream` (new)

Created alongside these findings. Scope: flip the one-line UI gate, verify worker `InputQueue` FIFO semantics under mid-run sends, add Storybook coverage. Small change.

### `ag-ui-runtime-swap` — DROPPED

The change was proposed, amended to BLOCKED status after Spike A, and ultimately dropped entirely based on Spike C evidence: adopting `useAgUiRuntime` would actively re-block mid-stream input that we intend to ship. The change's directory has been deleted. This spike memo is the written-down record of the analysis; if the need re-surfaces later (upstream ships a long-lived observation mode AND drops the one-run-at-a-time input constraint), start a fresh proposal rather than reviving this one.

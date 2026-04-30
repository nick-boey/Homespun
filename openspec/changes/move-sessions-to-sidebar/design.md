## Context

The web sidebar (`src/Homespun.Web/src/components/layout/sidebar.tsx`) currently renders a flat project list from `useProjects()` with a separate "Sessions" link in a Global section. Discovering active work requires either watching aggregate dots in the top bar (`ActiveAgentsIndicator`) or going to `/sessions`.

The data and live-update infrastructure to do better already exists:

- `SessionSummary` DTO carries `id`, `projectId`, `status` (`ClaudeSessionStatus`), `createdAt`, `lastActivityAt`.
- `useGlobalSessionsSignalR` is mounted in `GlobalSessionsProvider` at app root and invalidates session queries via `invalidateAllSessionsQueries(queryClient)` on every lifecycle event (`SessionStarted`, `SessionStopped`, `SessionStatusChanged`, `SessionError`, `SessionResultReceived`, `SessionModeModelChanged`).
- `ActiveAgentsIndicator` already encodes the canonical status → colour map.
- shadcn `Collapsible` is the standard primitive for expand/collapse interactions.
- TanStack Router has typed `Link` to `/sessions/$sessionId`.

This change is purely frontend, additive, and reuses every existing primitive — the design effort is mostly about composition and persistence semantics, not new infrastructure.

## Goals / Non-Goals

**Goals:**
- Make running sessions discoverable from the primary navigation, grouped by project.
- Sort sessions oldest-first within each project so long-running work surfaces visibly.
- Keep the sidebar visually quiet: just a colour dot and truncated title per session.
- Persist per-project collapse state across reloads.
- Keep the new list live without page refresh, reusing the existing SignalR invalidation path.
- Single source of truth for the status → colour map (sidebar + top-bar indicator share it).

**Non-Goals:**
- Replace or modify the top-bar `ActiveAgentsIndicator`.
- Modify the `/sessions` page sort, filter, or layout.
- Show `STOPPED` sessions anywhere new (history view stays on `/sessions`).
- Show aggregate dots on the project row itself.
- Add per-session metadata (mode, age, cost) to the sidebar row.
- Animate the collapse/expand transition.
- Any backend, worker, SignalR contract, or DTO changes.

## Decisions

### D1. Single `useAllSessions()` query, not N per-project queries

**Choice:** A single TanStack Query that fetches the full session list once; the sidebar groups by `projectId` client-side. Filter `STOPPED` and sort each group by `createdAt` ascending in a memoized selector.

**Alternatives considered:**
- Reuse `useProjectSessions(projectId)` per project row → N queries, N invalidations on every lifecycle event, more cache keys, harder to reason about.
- Keep `useAllSessionsCount()` and add a parallel `useAllSessions()` that returns the full list → chosen, since the count hook implies the same backend endpoint already exists or is trivial to expose.

**Rationale:** One query is simpler to invalidate, less cache thrash, single fetch on connect, and the data shape (`SessionSummary` carrying `projectId`) makes client-side grouping trivial. A typical user has tens of sessions across a handful of projects — N+1 queries are unjustified.

### D2. Live updates via the existing `invalidateAllSessionsQueries` helper

**Choice:** Register the new `useAllSessions()` query key under the same namespace that `invalidateAllSessionsQueries` invalidates. No new SignalR handlers, no new provider, no new event registrations.

**Alternatives considered:**
- Subscribe to SignalR directly from the sidebar component → duplicates the global subscription, adds another consumer, adds re-render churn.
- Optimistically update the cache from SignalR event payloads → unnecessary complexity; query invalidation is fast enough and is what the rest of the app does.

**Rationale:** The global subscription already does the right thing for every consumer of the session-query namespace. Make the new query a member of that namespace.

**Acceptance:** Triggering each lifecycle event in a test harness causes the new query to refetch and the sidebar to re-render with the new state, with no manual user action.

### D3. shadcn `Collapsible` for per-project expand/collapse

**Choice:** Wrap each project row's children in shadcn `Collapsible` (`CollapsibleTrigger` + `CollapsibleContent`). Chevron icon (lucide `ChevronRight` / `ChevronDown`) sits in the trigger.

**Alternatives considered:**
- Custom expand/collapse with `useState` + conditional rendering → reinvents the wheel; misses the a11y wiring shadcn provides.
- shadcn `Accordion` → enforces accordion semantics (one open at a time by default, more chrome) which we don't want.

**Rationale:** `Collapsible` is the right primitive: minimal chrome, accessible (`aria-expanded`), composable. The project's CLAUDE.md explicitly prohibits custom components when a shadcn equivalent exists.

**Animation:** Disabled (`data-state` transitions reset to instant). Matches the rest of the sidebar's interaction feel and avoids one more thing to maintain.

### D4. Persistence: `localStorage`, per-project, default expanded

**Choice:** Each project's collapse state is keyed `homespun.sidebar.project-expanded.<projectId>` in `localStorage`. Default value when the key is absent: `true` (expanded).

**Implementation:** A small `useLocalStorageBoolean(key, defaultValue)` hook (lazy-init from `localStorage`, write on toggle, swallow `localStorage` errors in incognito / quota-exceeded scenarios).

**Alternatives considered:**
- Zustand store → adds global state for what is local, per-row UI state.
- `sessionStorage` → loses state across reloads, which is the opposite of what we want.
- Server-persisted preference → backend change; out of scope and overkill.
- One single key holding a `Record<projectId, boolean>` → atomic write but per-project read/write is simpler and avoids race conditions across tabs.

**Rationale:** Per-project keys are simple, robust to projects being added/removed, and survive across reloads. Default-expanded matches the user's mental model: when you open the app, you want to see what's running.

### D5. Statuses shown: filter `STOPPED` only

**Choice:** Show all of `STARTING`, `RUNNING_HOOKS`, `RUNNING`, `WAITING_FOR_INPUT`, `WAITING_FOR_QUESTION_ANSWER`, `WAITING_FOR_PLAN_EXECUTION`, `ERROR`. Hide `STOPPED`.

**Rationale:** The sidebar surfaces *active work and active failures*. `ERROR` is included because it demands attention. `STOPPED` is terminal history and belongs on `/sessions`.

### D6. Sort: `createdAt` ascending

**Choice:** Within each project group, sort by `createdAt` ascending (oldest first).

**Rationale:** A long-running session that has been around a while is the one most likely to need attention or to be forgotten about. Newest-first is already the `/sessions` page's sort (by `lastActivityAt` desc) — these two views complement each other rather than duplicate.

### D7. Status → colour map: extract to shared util

**Choice:** Move the canonical status → colour map currently encoded in `ActiveAgentsIndicator` to `src/Homespun.Web/src/features/sessions/utils/session-status-color.ts`. Both the indicator and the new sidebar row consume from it.

**Rationale:** Drift between the two surfaces would confuse users. One source of truth.

**Mapping (re-stated for the spec):**

```
RUNNING / RUNNING_HOOKS / STARTING        → green   (bg-green-500)
WAITING_FOR_INPUT                         → yellow  (bg-yellow-500)
WAITING_FOR_QUESTION_ANSWER               → purple  (bg-purple-500)
WAITING_FOR_PLAN_EXECUTION                → orange  (bg-orange-500)
ERROR                                     → red     (bg-red-500)
STOPPED                                   → (filtered out, never rendered)
```

The exact Tailwind class names follow whatever `ActiveAgentsIndicator` uses today — the extraction is a copy, not a redesign.

### D8. Click target: typed TanStack Router `Link`

**Choice:** Each session row is a `<Link to="/sessions/$sessionId" params={{ sessionId }}>` matching the existing `SessionCard` pattern.

**Rationale:** Type safety, consistent with the rest of the app. No imperative navigation.

### D9. Empty-state: row renders unchanged

**Choice:** A project with zero non-`STOPPED` sessions renders the existing project row exactly as today — no chevron, no expand affordance, no children. The feature is purely additive.

**Rationale:** Avoids visually misleading UI (an empty disclosure that toggles nothing). Keeps existing project navigation intact for projects with no active work.

## Risks / Trade-offs

- **[Risk] `useAllSessions()` payload grows large for users with many sessions.**
  → Mitigation: This is bounded by what the user has actually created. If it ever becomes a concern, server-side filter by status is a small follow-up. For typical use it's tens of rows of small DTOs.

- **[Risk] Sidebar height grows with many running sessions, pushing the Global section off-screen.**
  → Mitigation: Per-project collapse is the user's lever; defaults are expanded but they can collapse projects they don't care about. If real users hit this, a dedicated scroll region inside the sidebar is a targeted follow-up.

- **[Risk] `localStorage` writes can throw in incognito / Safari private / quota-exceeded.**
  → Mitigation: Wrap reads/writes in try/catch; on failure, treat as "no persisted preference" and use the default. Don't surface errors to the user.

- **[Risk] Drift between `ActiveAgentsIndicator` colours and sidebar colours.**
  → Mitigation: D7 extracts the map to one util consumed by both. A unit test asserts the map covers every `ClaudeSessionStatus` enum value (excluding `STOPPED`).

- **[Risk] SignalR invalidation namespace mismatch — new query key not picked up by existing helper.**
  → Mitigation: Either co-locate the new query under the same key prefix the helper already invalidates, or extend `invalidateAllSessionsQueries` to invalidate the new key explicitly. A unit test fires each lifecycle event and asserts the new query refetches.

- **[Trade-off] Single `useAllSessions()` vs. per-project queries.**
  → We pay slightly larger payloads in exchange for simpler invalidation and fewer cache keys. Acceptable for the current scale.

- **[Trade-off] `localStorage` per-project keys vs. single keyed object.**
  → Per-key writes can race across tabs; a single keyed object would be atomic. We accept the race because the only loss on a race is one toggle being overwritten by another tab's last write — never a correctness issue.

## Open Questions

None blocking. The proposal answered the user-facing questions; remaining decisions are implementation-level and codified above.

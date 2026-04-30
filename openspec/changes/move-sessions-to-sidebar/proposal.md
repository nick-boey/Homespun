## Why

Today, discovering which sessions are active across projects requires either watching the aggregate status dots in the top bar or navigating to the global `/sessions` page. Neither surfaces *which project* a session belongs to in the primary navigation, which is the natural place to scan ongoing work. Surfacing live sessions inline in the project sidebar reduces context-switching and makes it obvious at a glance which projects have running work and what state each session is in.

## What Changes

- Each project row in the sidebar (`src/Homespun.Web/src/components/layout/sidebar.tsx`) becomes a **collapsible** group via shadcn `Collapsible`. The chevron toggles visibility of that project's running sessions.
- Under each expanded project, list its **running sessions** (statuses `STARTING`, `RUNNING_HOOKS`, `RUNNING`, `WAITING_FOR_INPUT`, `WAITING_FOR_QUESTION_ANSWER`, `WAITING_FOR_PLAN_EXECUTION`, `ERROR`). Hide `STOPPED`.
- Each session row renders a **status colour dot** + **truncated title**. No mode badge, no age, no extras. Hover tooltip carries the full title.
- Sessions are sorted **oldest-first** by `createdAt` ascending within each project group.
- Clicking a session row navigates via TanStack Router typed `Link` to `/sessions/$sessionId`.
- Default expansion state is **expanded**. User toggle is **persisted in `localStorage`** keyed `homespun.sidebar.project-expanded.<projectId>`.
- Empty projects (zero running sessions) render as today — no chevron, no children. The feature is purely additive.
- New `useAllSessions()` query fetches all sessions once and groups them client-side. Live-updated via the existing `useGlobalSessionsSignalR` handler — the new query key is registered with `invalidateAllSessionsQueries` so SignalR session lifecycle events keep the sidebar live without manual refresh.
- The existing top-bar `ActiveAgentsIndicator` is untouched; it remains a global aggregate view.
- The status → colour map is extracted to a shared utility shared by both the new sidebar rows and the existing top-bar indicator (single source of truth).

## Capabilities

### New Capabilities
- `sidebar-session-list`: how the project sidebar lists running sessions grouped by project, what statuses are shown, sort order, collapse/expand behaviour, persistence, click navigation, and live-update guarantees.

### Modified Capabilities
<!-- None. The session DTO, SignalR contract, /sessions page, and top-bar indicator are unchanged. -->

## Impact

**Affected code (frontend only — no backend changes):**
- `src/Homespun.Web/src/components/layout/sidebar.tsx` — restructure each project row to use shadcn `Collapsible` with a session-list child.
- New: `src/Homespun.Web/src/features/sessions/hooks/use-all-sessions.ts` — single TanStack Query for the full session list.
- New: `src/Homespun.Web/src/features/sessions/components/sidebar-session-list.tsx` and `sidebar-session-row.tsx` — the per-project list and row components.
- `src/Homespun.Web/src/features/sessions/hooks/use-sessions.ts` — extend `invalidateAllSessionsQueries` to invalidate the new `useAllSessions()` query key (or co-locate the new query under the same key namespace).
- New shared util: `src/Homespun.Web/src/features/sessions/utils/session-status-color.ts` — extracted from `ActiveAgentsIndicator`; consumed by both the indicator and the new sidebar rows.
- shadcn `Collapsible` — install via `npx shadcn@latest add collapsible` if not already present (with co-located `collapsible.stories.tsx` per the project's Storybook conventions).
- New tests: unit tests for sort/filter/persistence/live-update; Storybook story for `SidebarSessionList`.

**No impact on:**
- Backend (`Homespun.Server`, `Homespun.Worker`, `Homespun.Shared`) — purely frontend.
- SignalR hub contract or `SessionSummary` DTO.
- `/sessions` page rendering or sort behaviour.
- Top-bar `ActiveAgentsIndicator` behaviour.
- Worker, Aspire wiring, OTel pipeline, Fleece, OpenSpec.

**Linked work:**
- Fleece issue `cZH7Ip` ("Move sessions to side bar and sort by age"), tagged `openspec=move-sessions-to-sidebar`.

## 1. Shared utility — status colour map

- [x] 1.1 Create `src/Homespun.Web/src/features/sessions/utils/session-status-color.ts` exporting a `getSessionStatusColor(status: ClaudeSessionStatus): string | null` (returns the Tailwind background class, or `null` for `STOPPED` / unmapped).
- [x] 1.2 Refactor `ActiveAgentsIndicator` (`src/features/agents/components/active-agents-indicator.tsx`) to consume from the new utility — no behavioural change to the indicator.
- [x] 1.3 Add unit test asserting the map covers every non-`STOPPED` `ClaudeSessionStatus` enum value and returns `null` for `STOPPED`.

## 2. Data — `useAllSessions()` query

- [x] 2.1 Add `useAllSessions()` hook at `src/Homespun.Web/src/features/sessions/hooks/use-all-sessions.ts`. Use TanStack Query, single call to the existing all-sessions endpoint (look at `useAllSessionsCount()` and `useEnrichedSessions()` for the right backend route / generated client method).
- [x] 2.2 Pick a query key under the namespace already invalidated by `invalidateAllSessionsQueries` (in `src/features/sessions/hooks/use-sessions.ts`). If the namespace doesn't already cover the new key, extend `invalidateAllSessionsQueries` to invalidate it explicitly.
- [x] 2.3 Add a memoized selector helper (co-located in the same file or in a small `utils/group-sessions-by-project.ts`) that takes the raw `SessionSummary[]`, filters out `STOPPED`, groups by `projectId`, and sorts each group by `createdAt` ascending. Return shape: `Map<string, SessionSummary[]>` keyed by `projectId`.
- [x] 2.4 Unit-test the selector for: (a) STOPPED filter, (b) ERROR included, (c) sort order ascending by `createdAt`, (d) sessions with unknown `projectId` excluded.

## 3. Persistence — `useLocalStorageBoolean`

- [x] 3.1 Add `src/Homespun.Web/src/hooks/use-local-storage-boolean.ts`: `useLocalStorageBoolean(key: string, defaultValue: boolean): [boolean, (next: boolean) => void]`. Lazy-init from `localStorage`, write on change, swallow errors silently.
- [x] 3.2 Unit-test for: (a) returns `defaultValue` when key absent, (b) returns persisted value when key present, (c) writes on update, (d) write failure (mock `localStorage.setItem` to throw) does not crash and updates in-memory state.

## 4. shadcn `Collapsible` primitive

- [x] 4.1 Run `npx shadcn@latest add collapsible` in `src/Homespun.Web` (skip if already installed). Verify the file lands at `src/components/ui/collapsible.tsx`.
- [x] 4.2 Add `src/components/ui/collapsible.stories.tsx` with at minimum a Default story per the project's Storybook conventions.

## 5. Components — `SidebarSessionRow` and `SidebarSessionList`

- [x] 5.1 Add `src/Homespun.Web/src/features/sessions/components/sidebar-session-row.tsx` rendering a single session row: a `<Link to="/sessions/$sessionId" params={{ sessionId }}>` containing the status colour dot + truncated title. Add a hover tooltip carrying the full title (use existing tooltip primitive).
- [x] 5.2 Add `src/Homespun.Web/src/features/sessions/components/sidebar-session-list.tsx` taking a `projectId` and rendering the sorted, filtered session children for that project. Returns `null` when the group is empty.
- [x] 5.3 Add Storybook story for `SidebarSessionRow` covering each colour state (green / yellow / purple / orange / red).
- [x] 5.4 Add Storybook story for `SidebarSessionList` with a multi-session fixture demonstrating sort order and the colour spread.

## 6. Sidebar wiring

- [x] 6.1 In `src/Homespun.Web/src/components/layout/sidebar.tsx`, replace the existing flat project `NavItem` with a structure that wraps the project trigger + child list in shadcn `Collapsible`, gated on whether that project has any non-`STOPPED` sessions.
- [x] 6.2 Drive `Collapsible.open` from `useLocalStorageBoolean('homespun.sidebar.project-expanded.' + projectId, true)`.
- [x] 6.3 For projects with zero non-`STOPPED` sessions, render the existing `NavItem` unchanged (no chevron, no Collapsible wrapping).
- [x] 6.4 Ensure the chevron has accessible affordances: visible icon + `aria-expanded` on the trigger (shadcn `Collapsible` provides this — verify it is preserved).
- [x] 6.5 Disable any default Collapsible animation (e.g. via class overrides) so the transition is instant.

## 7. Live-update integration test

- [x] 7.1 Component test: mount the sidebar with a fixed `useProjects()` result and a fixed `useAllSessions()` initial result. Programmatically fire each of the six SignalR lifecycle events through the existing event pump (or directly invoke `invalidateAllSessionsQueries`) and assert the rendered DOM updates accordingly:
  - `SessionStarted` → new row appears
  - `SessionStatusChanged` (RUNNING → WAITING_FOR_INPUT) → dot colour changes from green to yellow
  - `SessionStopped` → row disappears
  - `SessionError` / `SessionResultReceived` / `SessionModeModelChanged` → query is re-invalidated (cache invalidation count increments)

## 8. Pre-PR checks

- [ ] 8.1 Run `dotnet test` (no backend changes are expected to break, but the project's pre-PR checklist requires it).
- [ ] 8.2 In `src/Homespun.Web`: `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test`, `npm run build-storybook`.
- [ ] 8.3 Optional but recommended: launch `dev-mock` and visually verify the sidebar with the seeded mock projects and sessions. Toggle a project, reload, confirm state survives. Confirm the dot colours match the top-bar indicator.

## 9. Linking and review

- [ ] 9.1 Tag fleece issue `cZH7Ip` with `openspec=move-sessions-to-sidebar` (`fleece edit cZH7Ip --tags "openspec=move-sessions-to-sidebar"`).
- [ ] 9.2 Update issue status to `progress` when starting (`fleece edit cZH7Ip -s progress`) and to `review` with `--linked-pr <number>` when opening the PR.
- [ ] 9.3 Commit `.fleece/` changes alongside code changes in the same PR.

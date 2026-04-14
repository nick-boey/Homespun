# Follow-up Issues — prompts

These are stubs for the gaps identified during the brownfield migration (see
`spec.md` → *Identified Gaps*). They are committed to Fleece under a parent
`verify` issue so the group can be closed out once every child is `complete`.

The canonical record is whatever `fleece show <id>` returns; this file is a
human-readable snapshot at migration time.

## Parent

| FI | Fleece ID | Type | Title |
|---|---|---|---|
| FI-0 | `M6cHa1` | verify | Close out Prompts migration gaps |

## Children

| FI | Fleece ID | Type | Priority | Title | Why it matters | Acceptance |
|---|---|---|---|---|---|---|
| FI-1 | `cisMRL` | task | 1 | Add server-side tests for `AgentPromptService` and `AgentPromptsController` | Zero direct server coverage on the largest shipped CRUD surface in the app. Constitution §I expects TDD; Constitution §V expects ≥80% coverage on changed lines and module coverage on track for 60%/2026-06-30 and 80%/2026-09-30. This slice currently contributes 0%. GP-1. | New tests under `tests/Homespun.Tests/Features/ClaudeCode/` and `tests/Homespun.Api.Tests/Features/AgentPromptsApiTests.cs` covering: (a) `GetAvailableForProjectAsync` merge + dedupe + `IsOverride`; (b) Create/Update/Delete happy + 404 + 409 + `Category`-silent-drop; (c) `CreateOverrideAsync` missing-global, duplicate, inheritance; (d) `EnsureDefaults` idempotence, `RestoreDefaults` overwrite semantics, `DeleteAllProjectPrompts` project isolation; (e) `RenderTemplate` each placeholder + `{{#if}}` removal + case-insensitive lookup. Delta ≥80% on the slice. |
| FI-2 | `OG4lUm` | chore | 2 | Move prompt request DTOs to `Homespun.Shared/Requests/` | `CreateAgentPromptRequest`, `UpdateAgentPromptRequest`, and `CreateOverrideRequest` live inside `AgentPromptsController.cs`. Constitution §III requires cross-process DTOs to originate in `Homespun.Shared`. OpenAPI generation currently masks the issue for the web, but any C# consumer (tests, future services) would have to reference `Homespun.Server`. GP-2. | DTOs extracted into `src/Homespun.Shared/Requests/AgentPromptRequests.cs`; controller imports them; OpenAPI client regenerates with no shape change; `npm run generate:api:fetch` + typecheck + tests pass; no removed types leak out. |
| FI-3 | `PYFYqP` | bug | 2 | `AgentPromptService.UpdateAsync` silently drops `Category` changes | The request shape accepts `Category` but the service ignores it on update. A user who re-saves a prompt after changing its category sees no error but no effect — silent data loss. GP-3. | Either (a) persist the `Category` change (preferred) OR (b) reject with `400` + message "Category is immutable after create". Decision documented in the PR. Regression test: update a prompt with a different `Category`, assert the persisted row matches the decision. |
| FI-4 | `IOVi6S` | chore | 4 | Document the "no sanitisation on `InitialMessage`" decision | Not a security issue today because the template body is sent as a Claude prompt and never rendered as HTML in the browser. But the decision is not documented and a future change (e.g. adding a rich-text preview) could expose it. GP-4. | Code comment on `AgentPromptService.CreateAsync` + `UpdateAsync` noting the template is intentionally stored verbatim and referencing this issue. `CLAUDE.md` or the slice's `index.ts` notes where the rendering boundary is. No code change beyond comments + docs. |
| FI-5 | `E2EvIw` | feature | 3 | Broadcast prompt catalogue mutations over SignalR | Editing a prompt in browser A does not invalidate the query in browser B until TanStack Query's focus-refetch kicks in. In a multi-tab or multi-user setup this leads to stale catalogues. GP-5. | New `INotificationHubClient.AgentPromptChanged(promptName, projectId?, changeType)` method; controller broadcasts after successful mutations (Create / Update / Delete / Override / RemoveOverride / RestoreDefaults / DeleteAllProjectPrompts); web adds a hub subscription that invalidates the relevant `['prompts', ...]` keys. Regression test asserts invalidation fires in a second client after a mutation in the first. |
| FI-6 | `BSG5Of` | feature | 4 | Soft-delete / audit trail for destructive prompt operations | `DELETE /project/{id}/all` removes every project prompt permanently in one call. `restore-defaults` only covers globals. No undo, no audit of who deleted what. GP-6. | Introduce a soft-delete flag on `AgentPrompt` OR a separate `AgentPromptDeletion` history table/file; expose `GET /api/agent-prompts/trash?projectId=&limit=` and `POST /api/agent-prompts/trash/{id}/restore`. Existing endpoints now mark-as-deleted instead of hard-removing; hard-deletion left as a separate explicit endpoint or a retention job. Tests cover the restore happy path + retention policy. |
| FI-7 | `EUvQTv` | task | 3 | Fill test-coverage gaps: route components and `use-remove-override` | `routes/prompts.tsx` and `routes/projects.$projectId.prompts.tsx` have no component tests; `use-remove-override.ts` has no co-located test. Constitution §I expects test-first discipline and co-located tests are the established convention. GP-7. | New `routes/prompts.test.tsx` and `routes/projects.$projectId.prompts.test.tsx` asserting the correct `<PromptsList>` props; new `hooks/use-remove-override.test.tsx` asserting the endpoint call + query invalidation + error handling. All pass in CI. |

## Status

Created via `/speckit-brownfield-migrate` follow-up. Inspect with:

```bash
fleece show M6cHa1 --json
fleece list --tree | rg -A 9 M6cHa1
```

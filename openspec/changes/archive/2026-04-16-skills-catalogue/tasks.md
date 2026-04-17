## 1. Backend: delete prompt system

- [x] 1.1 Delete `AgentPromptService.cs`, `IAgentPromptService.cs` from `Features/ClaudeCode/Services/`
- [x] 1.2 Delete `AgentPromptsController.cs` from `Features/ClaudeCode/Controllers/`
- [x] 1.3 Delete `DefaultPromptDefinition.cs`, `DefaultPromptsInitializationService.cs` from `Features/ClaudeCode/Services/`
- [x] 1.4 Delete `default-prompts.json` from `Features/ClaudeCode/Resources/`
- [x] 1.5 Remove `AgentPrompt`, `PromptContext` models from `Homespun.Shared/Models/Sessions/`
- [x] 1.6 Remove `Category` enum (`PromptCategory`) from `Homespun.Shared`. **Kept**: `SessionMode` (used by session creation beyond prompts) and `SessionType` (used by IssuesAgentController/SessionsController as a session tag).
- [x] 1.7 Remove prompt service registrations from DI in `Program.cs`
- [x] 1.8 Regenerate OpenAPI client (`npm run generate:api:fetch`) to drop prompt endpoints

## 2. Backend: skill discovery service

- [x] 2.1 Create `ISkillDiscoveryService` interface with `DiscoverSkillsAsync(projectPath)` returning categorised skill descriptors
- [x] 2.2 Implement `SkillDiscoveryService` — scan `.claude/skills/**/SKILL.md`, parse frontmatter, categorise into OpenSpec (hard-coded names), Homespun (`homespun: true`), and general
- [x] 2.3 Define `SkillDescriptor` model: `{name, description, category, mode?, args[]?, skillBody}`
- [x] 2.4 Parse `homespun-mode` and `homespun-args` from frontmatter for Homespun skills
- [x] 2.5 Register service in DI
- [x] 2.6 Write unit tests for discovery: OpenSpec skill matching, Homespun frontmatter parsing, missing SKILL.md handling, malformed frontmatter skipping

## 3. Backend: skill-based dispatch

- [x] 3.1 Modify `IssuesAgentController` dispatch flow: read skill body from SKILL.md, append composed args, send as `session.initialMessage`
- [x] 3.2 Support dispatch with no skill (Task Agent: empty or custom free-text message)
- [x] 3.3 Support schema override injection into system prompt for OpenSpec skills on non-default schemas
- [x] 3.4 Write integration test: dispatch with Homespun skill composes correct initialMessage
- [x] 3.5 Write integration test: dispatch with OpenSpec skill includes skill body + change name
- [x] 3.6 Write integration test: dispatch with no skill sends empty or user-provided message

## 4. Backend: expose skills API

- [x] 4.1 Create skills endpoint returning available skills for a project (or add to existing session/agent endpoints) — `GET /api/skills/project/{projectId}` returning `DiscoveredSkills { OpenSpec, Homespun, General }` (skill bodies stripped for wire transfer)
- [x] 4.2 Regenerate OpenAPI client (deferred to follow-up frontend session)

## 5. Frontend: delete prompts slice

- [x] 5.1 Delete `features/prompts/` directory (7 components, 13 hooks, ~4,000 LOC)
- [x] 5.2 Remove prompt-related tests (14 files) — also deleted prompt-template util, useIssueContext, useAgentPrompts, useIssueAgentAvailablePrompts and related tests
- [x] 5.3 Remove any prompt references from route definitions and navigation — `/prompts`, `/projects/$projectId/prompts` routes removed; "Prompts" removed from sidebar and project tabs

## 6. Frontend: skill picker in Task Agent tab

- [x] 6.1 Add skill-picker component that fetches available Homespun skills from the API
- [x] 6.2 Render `homespun-args` as appropriate input controls per `kind` enum (issue picker, change picker, phase-list multi-select, free-text input) — text-input placeholders per kind; richer pickers tracked as follow-up
- [x] 6.3 Compose args from UI inputs and pass to dispatch
- [x] 6.4 Support "no skill" dispatch (existing free-text message behaviour)
- [x] 6.5 Write component tests for skill picker rendering and arg composition

## 7. Frontend: skills in session chat window

- [x] 7.1 Surface all discovered skills (general category) in the session chat window — new Skills tab in SessionInfoPanel
- [x] 7.2 Display skill name and description for Claude's auto-invocation context

## 8. Verification

- [x] 8.1 Run full backend test suite: `dotnet test` — 1972 + 203 + 5 passed (0 failed; 7 environment-dependent skipped)
- [x] 8.2 Run frontend checks: `npm run lint:fix`, `npm run format:check`, `npm run typecheck`, `npm test` — typecheck / format / tests (169 files, 1944 passed, 1 skipped) all green. Lint has 1 pre-existing error in `components/error-boundary.tsx` (unrelated to this change, present on main) and 18 pre-existing warnings
- [x] 8.3 Verify Task Agent tab dispatches with selected skill, with no skill, and with custom message — verified via Playwright MCP: skill picker renders `fix-bug (build)` from the project's `.claude/skills/`, selecting it renders Issue ID input, Start Agent POSTs `{projectId, mode:"build", skillName:"fix-bug", skillArgs:{"issue-id":"ABC123"}}` to `/api/issues/{id}/run`. Mode synced from skill's declared `homespun-mode: build`.
- [x] 8.4 Verify OpenSpec skills appear in the OpenSpec tab (depends on `openspec-integration` but skill discovery should work independently) — backend discovery verified at API level (`openspec-apply-change` correctly categorized under `openSpec`). OpenSpec tab UI lives in the separate `openspec-integration` change.
- [x] 8.5 Verify Issues Agent tab remains skill-less and functions as before — verified via Playwright MCP: Issues Agent tab has only mode/model/textarea/start button, no skill or prompt picker.

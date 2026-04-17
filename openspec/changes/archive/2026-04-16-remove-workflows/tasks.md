## 1. Backend deletion

- [x] 1.1 Delete `src/Homespun.Server/Features/Workflows/` directory (controllers, services, hubs, templates, storage — ~15 files)
- [x] 1.2 Remove `WorkflowHub` registration and interface from `Homespun.Shared`
- [x] 1.3 Remove workflow-related DTOs and enums from `Homespun.Shared/Models/`
- [x] 1.4 Remove workflow service registrations from DI in `Program.cs`
- [x] 1.5 Remove `WorkflowAgentStatus` from `Features/ClaudeCode/Data/`
- [x] 1.6 Remove `PullRequestWorkflowService` from `Features/PullRequests/` (verify if this is workflow-dependent or independent)

## 2. Frontend deletion

- [x] 2.1 Remove the Workflow tab component from the run-agent panel
- [x] 2.2 Remove any workflow-related hooks, API clients, and types from the web slice
- [x] 2.3 Regenerate the OpenAPI client (`npm run generate:api:fetch`) to drop workflow endpoints

## 3. Test cleanup

- [x] 3.1 Remove any workflow-related backend tests from `tests/Homespun.Tests/` and `tests/Homespun.Api.Tests/`
- [x] 3.2 Remove any workflow-related frontend tests
- [x] 3.3 Run full test suite to confirm no regressions: `dotnet test`, `npm test`, `npm run typecheck`, `npm run lint:fix`

## 4. Verification

- [x] 4.1 Confirm no remaining references to workflow types or services in the codebase (`grep -r "Workflow" src/`)
- [x] 4.2 Confirm the application starts and the run-agent panel renders without the Workflow tab
- [x] 4.3 Confirm agent dispatch on issues still works (unrelated to workflows)

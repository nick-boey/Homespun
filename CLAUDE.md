# Development Instructions

This file contains instructions for Claude Code when working on this project.

## Overview

Homespun is a web application for managing development features and AI agents. It provides:

- Project and feature management with hierarchical tree visualization
- Git clone integration for isolated feature development
- GitHub PR synchronization
- Claude Code agent orchestration

The frontend is built with React + Vite + TypeScript + TailwindCSS, with an ASP.NET server backend.

## Development Practices

### Test Driven Development (TDD)

**TDD is mandatory for all planning and implementation.** When developing new features or fixing bugs:

1. **Write tests first** - Before implementing any code, write failing tests that define the expected behavior
2. **Red-Green-Refactor** - Follow the TDD cycle for every change:
   - Red: Write a failing test
   - Green: Write minimal code to make the test pass
   - Refactor: Clean up the code while keeping tests green
3. **Plan with tests in mind** - When planning features, identify test cases as part of the design
4. **Test naming** - Use descriptive test names that explain the scenario and expected outcome (e.g., `it('returns error when project not found')`)
5. **Test coverage** - Aim for comprehensive coverage of business logic and component behavior

### Pre-PR Checklist

**Before creating a pull request, always run the following checks:**

```bash
dotnet test

cd src/Homespun.Web

# Run linting (must pass with no errors)
npm run lint:fix

# Format code
npm run format:check

# Generate API client from OpenAPI spec
npm run generate:api:fetch

# Run type checking (must pass with no errors)
npm run typecheck

# Run tests
npm test

# Run e2e tests
npm test:e2e
```

These checks are run in CI and will cause the PR to fail if not passing. Always verify locally first.

### Project Structure (Vertical Slice Architecture)

The project follows Vertical Slice Architecture where code is organized by feature rather than technical layer. Features are organized at the top level, each containing all related components, hooks, stores, and API calls.

```
src/
├── Homespun.Server/             # ASP.NET backend (API, SignalR hubs, services)
│   ├── Features/                # Feature slices (all business logic)
│   │   ├── AgentOrchestration/  # Agent lifecycle management
│   │   ├── ClaudeCode/          # Claude Code SDK session management
│   │   ├── Commands/            # Shell command execution
│   │   ├── Fleece/              # Fleece issue tracking integration
│   │   ├── Git/                 # Git clone operations
│   │   ├── GitHub/              # GitHub API integration (Octokit)
│   │   ├── Notifications/       # Toast notifications via SignalR
│   │   ├── Projects/            # Project management
│   │   ├── PullRequests/        # PR workflow and data entities
│   │   └── SignalR/             # SignalR hub implementations
│   └── Program.cs               # Application entry point
├── Homespun.Web/                # React frontend
│   ├── src/
│   │   ├── features/            # Feature slices (top-level organization)
│   │   │   ├── projects/        # Project management
│   │   │   │   ├── components/  # Feature-specific components
│   │   │   │   ├── hooks/       # Feature-specific hooks
│   │   │   │   └── index.ts     # Public exports
│   │   │   ├── issues/          # Issue tracking
│   │   │   ├── agents/          # Agent sessions and chat
│   │   │   └── branches/        # Branch and PR management
│   │   ├── components/          # Shared components
│   │   │   └── ui/              # shadcn/ui components
│   │   ├── api/                 # OpenAPI generated client
│   │   ├── hooks/               # Shared hooks
│   │   ├── lib/                 # Utility functions
│   │   ├── stores/              # Zustand stores
│   │   ├── routes/              # TanStack Router routes
│   │   └── test/                # Test setup and utilities
│   └── package.json
├── Homespun.Shared/             # Shared library (DTOs, contracts, hub interfaces)
├── Homespun.ClaudeAgentSdk/     # Claude Code SDK C# wrapper
└── Homespun.Worker/             # TypeScript agent worker (Hono + Claude Agent SDK)

tests/
├── Homespun.Tests/              # Backend unit tests (NUnit + Moq)
├── Homespun.Api.Tests/          # API integration tests (WebApplicationFactory)
```

### Feature Slices

- **Fleece**: Integration with Fleece issue tracking - JSONL-based storage in `.fleece/` directory
  - **Version Sync Required**: When updating the `Fleece.Core` NuGet package version in `Homespun.Server.csproj` and `Homespun.Shared.csproj`, you must also update the `Fleece.Cli` version in `Dockerfile.base` to match
- **ClaudeCode**: Claude Code SDK session management — supports Plan (read-only) and Build (full access) modes. The worker is the only component that speaks the native SDK format; the server ingests A2A events from the worker via `ISessionEventIngestor`, appends each event to the per-session JSONL log through `A2AEventStore`, translates once via `A2AToAGUITranslator`, and broadcasts a single `SessionEventEnvelope` per AG-UI event over SignalR's `ReceiveSessionEvent`. Refresh replays the same envelopes through `GET /api/sessions/{id}/events?since=N&mode=incremental|full` so live and refresh produce identical streams. The default replay mode is configurable via `SessionEvents:ReplayMode`; `Full` is the kill switch if incremental replay ever produces a gap.
- **Commands**: Shell command execution abstraction
- **Git**: Git clone creation, management, and rebase operations
- **GitHub**: GitHub PR synchronization and API operations using Octokit
- **Projects**: Project CRUD operations
- **PullRequests**: PR workflow, feature management, and core data entities

### Dev prerequisites

- .NET 10 SDK with the Aspire workload
- Node 20+ (npm on PATH — required by the AppHost's Vite wiring)
- Docker Desktop (for PLG containers, and for Docker-mode agent execution)
- One-time secret bootstrap: run `scripts/set-user-secrets.sh` (macOS/Linux) or
  `scripts/set-user-secrets.ps1` (Windows) after copying `.env.example` → `.env`.
  The script migrates `GITHUB_TOKEN` and `CLAUDE_CODE_OAUTH_TOKEN` into the
  `Homespun.AppHost` user-secrets store so Aspire can inject them at runtime.

### Running the Application

**Do not under any circumstances stop a container called `homespun` or `homespun-prod`.**

Local dev runs through the Aspire AppHost. Pick a launch profile:

| Profile | Use case |
|---|---|
| `dev-mock` | Fastest inner loop. Server (`AddProject`) + Vite + PLG with the mock service graph. No worker, agents return canned A2A events. |
| `dev-live` | Same stack plus real Claude SDK sessions. Docker-mode agent execution spawns a sibling worker container per session via DooD. |
| `dev-windows` | Same stack plus a single pre-running worker container. Agent execution routes every session to that worker (SingleContainer mode) — also usable on Apple Silicon now that the worker image is built locally instead of pulled from GHCR. |
| `dev-container` | Prod-parity check: server/web/worker built from their Dockerfiles. Not a daily driver — inner loop is rebuild-per-change. |

```bash
dotnet run --project src/Homespun.AppHost --launch-profile dev-mock
dotnet run --project src/Homespun.AppHost --launch-profile dev-live
dotnet run --project src/Homespun.AppHost --launch-profile dev-windows
dotnet run --project src/Homespun.AppHost --launch-profile dev-container
```

Use `dotnet run` for these — `aspire run` does not accept `--launch-profile`
and silently falls back to the first profile in `launchSettings.json`.

In every dev profile that needs a worker (`dev-live`, `dev-windows`,
`dev-container`), the AppHost builds the image from
`src/Homespun.Worker/Dockerfile` via `AddDockerfile` and tags it `worker:dev`.
First boot takes an extra 30–60s for the Docker build; subsequent boots hit
the layer cache. No dev profile pulls from GHCR — GHCR is reserved for the
prod deploy path (`docker-compose.yml` + Komodo).

Server is reachable at `http://localhost:5101` (port pinned for Playwright + existing tests).
Grafana lands on `3000` and Loki on `3100`. The Aspire dashboard prints its own URL at startup.

### Accessing Application Logs

Server logs flow through two sinks in parallel:

1. **Aspire dashboard** — `AddServiceDefaults()` wires the OTLP log exporter;
   `aspire otel logs server` surfaces entries for the current session. The
   server's `Program.cs` calls `ClearProviders()` *before* `AddServiceDefaults`
   so the OTLP provider survives alongside the JSON console formatter.
2. **Grafana/Loki via Promtail** — every Aspire-managed container the dev
   stack runs (worker, plus server/web in `dev-container`) carries the
   `logging=promtail` label so Promtail's `docker_sd_configs` discovers it.
   Promtail streams those containers' logs through the host Docker socket
   alone (no `/var/lib/docker/containers` bind mount, which doesn't exist on
   macOS Docker Desktop).

Workers can query application logs from Loki at `http://homespun-loki:3100`.

**Verify connectivity:**
```bash
curl -s 'http://homespun-loki:3100/ready'
```

For detailed log analysis with LogQL queries, use the `/logs` skill.

## React Frontend Development

### Technology Stack

The React frontend uses:

- **React 19** with TypeScript
- **Vite** for build tooling
- **TailwindCSS v4** for styling
- **TanStack Router** for file-based routing
- **TanStack Query** for server state management
- **Zustand** for client state management
- **shadcn/ui** with Base UI for components (New York style, Zinc base color)
- **prompt-kit** for AI chat interface components

### OpenAPI Client

The API client is auto-generated from the server's OpenAPI spec. The generated code is in `src/api/generated/`.

Regenerate after server API changes:

```bash
npm run generate:api:fetch
```

Use the typed API client:

```typescript
import { getProjects, createProject } from '@/api'
```

## Testing

### Backend Tests (.NET)

```bash
# Run all backend tests
dotnet test

# Run specific test project
dotnet test tests/Homespun.Tests
dotnet test tests/Homespun.Api.Tests

# Run with verbose output
dotnet test --verbosity normal
```

### Frontend Tests (Vitest)

The React frontend uses **Vitest** with **React Testing Library** for testing.

```bash
cd src/Homespun.Web

# Run tests once
npm test

# Run tests in watch mode
npm run test:watch

# Run tests with coverage
npm run test:coverage
```

#### Test Structure

Tests are co-located with source files using the `.test.ts` or `.test.tsx` extension:

```
src/
├── components/ui/
│   ├── button.tsx
│   └── button.test.tsx
├── lib/
│   ├── utils.ts
│   └── utils.test.ts
└── features/projects/
    ├── ProjectCard.tsx
    └── ProjectCard.test.tsx
```

#### Writing Tests

Refer to existing tests and use a similar style.

## Testing Infrastructure

The project uses a comprehensive three-tier testing strategy:

### Unit Tests (Homespun.Tests)

**Framework:** NUnit + Moq

Unit tests cover service logic in isolation using Moq for dependency mocking.

### API Integration Tests (Homespun.Api.Tests)

**Framework:** NUnit + WebApplicationFactory

API tests verify HTTP endpoints using an in-memory test server with `HomespunWebApplicationFactory`.

### React Frontend E2E Tests (Homespun.Web/e2e)

**Framework:** Playwright Test

E2E tests for the React frontend are located in `src/Homespun.Web/e2e/`. These tests mirror the C# E2E tests but use the JavaScript Playwright API.

**E2E Test Configuration:**

- Tests automatically start the mock server via `webServer` config

### Inspection with Playwright MCP and Mock Mode

To start a server running in Mock Mode to investigate with the Playwright MCP:

```bash
dotnet run --project src/Homespun.AppHost --launch-profile dev-mock
```

Server, Vite, and the PLG stack are started by the AppHost. Server logs, traces,
and metrics stream to the Aspire dashboard via OTLP (URL printed at startup).
Worker / container-resource logs are also visible in Loki via Grafana at
`http://localhost:3000`. The server is reachable at `http://localhost:5101`
(pinned port), and the Vite dev server at `http://localhost:5173`. Use the
Playwright MCP tools against those URLs.

### Critical Shell Management Rules

**NEVER use `KillShell` on a shell running `dotnet run --project src/Homespun.AppHost`
or any long-lived `dotnet` process.** Killing these shells can terminate your
entire session and crash the agent.

When cleaning up after UI testing:

- Close the browser using `mcp__playwright__browser_close` - this is safe
- Leave the AppHost process running - it will be cleaned up when the session ends
- Do NOT attempt to kill background shells that are running server processes

If you need to restart the AppHost:

1. Use `pkill -f "Homespun.AppHost"` to stop the dotnet process directly
2. Start a new AppHost run with
   `dotnet run --project src/Homespun.AppHost --launch-profile dev-mock &`

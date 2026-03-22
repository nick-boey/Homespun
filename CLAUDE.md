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
- **ClaudeCode**: Claude Code SDK session management - supports Plan (read-only) and Build (full access) modes
- **Commands**: Shell command execution abstraction
- **Git**: Git clone creation, management, and rebase operations
- **GitHub**: GitHub PR synchronization and API operations using Octokit
- **Projects**: Project CRUD operations
- **PullRequests**: PR workflow, feature management, and core data entities

### Running the Application

**Do not under any circumstances stop a container called `homespun` or `homespun-prod`.**

### Accessing Application Logs

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
# Start mock backend server
./scripts/mock.sh       # Linux/Mac
./scripts/mock.ps1      # Windows

cd src/Homespun.Web

# Start development server
npm run dev
```

Review the output to find the ports that the development backend and frontend applications are running on, then use the Playwright MCP tools to investigate.

### Critical Shell Management Rules

**NEVER use `KillShell` on a shell running `mock.sh` or any `dotnet` process.** Killing these shells can terminate your entire session and crash the agent.

When cleaning up after UI testing:

- Close the browser using `mcp__playwright__browser_close` - this is safe
- Leave the mock.sh process running - it will be cleaned up when the session ends
- Do NOT attempt to kill background shells that are running server processes

If you need to restart the mock server:

1. Use `pkill -f "dotnet.*mock"` to stop the dotnet process directly
2. Start a new mock server with `./scripts/mock.sh &`

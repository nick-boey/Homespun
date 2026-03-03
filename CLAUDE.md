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
└── Homespun.E2E.Tests/          # End-to-end tests (Playwright)
```

### Feature Slices

- **Fleece**: Integration with Fleece issue tracking - JSONL-based storage in `.fleece/` directory
- **ClaudeCode**: Claude Code SDK session management - supports Plan (read-only) and Build (full access) modes
- **Commands**: Shell command execution abstraction
- **Git**: Git clone creation, management, and rebase operations
- **GitHub**: GitHub PR synchronization and API operations using Octokit
- **Projects**: Project CRUD operations
- **PullRequests**: PR workflow, feature management, and core data entities

### Running the Application

**Do not under any circumstances stop a container called `homespun` or `homespun-prod`.**

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

### UI Components

#### shadcn/ui

The project uses shadcn/ui for base components. Components are installed into `src/components/ui/`. Use the default shadcn/ui theme (which will be styled later).

Add new components:
```bash
cd src/Homespun.Web
npx shadcn@latest add button
npx shadcn@latest add input
# etc.
```

**DO NOT create custom components if a shadcn/ui component already exists.**

Configuration is in `components.json`:
- Style: new-york
- Base color: zinc
- CSS Variables: enabled
- Icon library: lucide

#### prompt-kit (Chat Components)

For AI chat interface components, use the prompt-kit library. These components are built on shadcn/ui and Tailwind.

Add prompt-kit components:
```bash
npx shadcn@latest add "https://prompt-kit.com/c/prompt-input.json"
npx shadcn@latest add "https://prompt-kit.com/c/message.json"
npx shadcn@latest add "https://prompt-kit.com/c/markdown.json"
```

Available prompt-kit components:
- **prompt-input** - Chat input with file attachments
- **message** - Chat message display
- **markdown** - Markdown rendering with syntax highlighting
- **code-block** - Code display with copy button
- **thinking-bar** - AI thinking indicator
- **loader** - Loading animations

**Use prompt-kit components for all chat and AI-related UI.** Do not create custom chat components.

### Development Commands

```bash
cd src/Homespun.Web

# Start development server
npm run dev

# Build for production
npm run build

# Lint code
npm run lint
npm run lint:fix

# Format code
npm run format
npm run format:check

# Type checking
npm run typecheck

# Generate API client from OpenAPI spec
npm run generate:api:fetch
```

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
dotnet test tests/Homespun.E2E.Tests

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

Use React Testing Library with Vitest:

```typescript
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Button } from './button'

describe('Button', () => {
  it('renders button text', () => {
    render(<Button>Click me</Button>)
    expect(screen.getByRole('button', { name: 'Click me' })).toBeInTheDocument()
  })

  it('calls onClick when clicked', async () => {
    const user = userEvent.setup()
    let clicked = false
    render(<Button onClick={() => (clicked = true)}>Click</Button>)

    await user.click(screen.getByRole('button'))
    expect(clicked).toBe(true)
  })
})
```

#### Test Utilities

- `@testing-library/react` - Component rendering and queries
- `@testing-library/user-event` - User interaction simulation
- `@testing-library/jest-dom` - DOM matchers (toBeInTheDocument, etc.)
- `vitest` - Test runner with globals enabled

## Testing Infrastructure

The project uses a comprehensive three-tier testing strategy:

### Unit Tests (Homespun.Tests)

**Framework:** NUnit + Moq

Unit tests cover service logic in isolation using Moq for dependency mocking.

### API Integration Tests (Homespun.Api.Tests)

**Framework:** NUnit + WebApplicationFactory

API tests verify HTTP endpoints using an in-memory test server with `HomespunWebApplicationFactory`.

### End-to-End Tests (Homespun.E2E.Tests)

**Framework:** NUnit + Playwright

E2E tests run against the full application stack using Playwright for browser automation.

**E2E Configuration:**
- `HomespunFixture` automatically starts the application for tests
- Set `E2E_BASE_URL` to test against an external server
- Set `E2E_CONFIGURATION` to specify build configuration (Release/Debug)

### Inspection with Playwright MCP and Mock Mode

The mock mode provides a development environment with:
- Pre-seeded demo data (projects, features, issues)
- No external dependencies (GitHub, Claude API)
- Isolated from production data

To start a server running in Mock Mode:
```bash
./scripts/mock.sh       # Linux/Mac
./scripts/mock.ps1      # Windows
```

The script runs `dotnet run` with the mock launch profile. When running inside a container, it uses `localhost` for the URL. Review the script output to find the URL to access (typically `http://localhost:5095`).

### Using Mock Mode with Playwright

When testing the UI with Playwright MCP tools inside the agent container:

1. **Start the mock server as a background process:**
   ```bash
   ./scripts/mock.sh &
   ```
   Wait for the server to start (look for "Now listening on: http://localhost:5095" in the output).

2. **Access the mock server at localhost:**
   - Navigate: `browser_navigate` to `http://localhost:5095/projects`
   - Screenshot: `browser_take_screenshot`

3. **After testing, the mock process will continue running.** To stop it:
   - Find the process: `ps aux | grep dotnet`
   - Stop it: `kill <pid>` or use Ctrl+C if running in foreground

### Critical Shell Management Rules

**NEVER use `KillShell` on a shell running `mock.sh` or any `dotnet` process.** Killing these shells can terminate your entire session and crash the agent.

When cleaning up after UI testing:
- Close the browser using `mcp__playwright__browser_close` - this is safe
- Leave the mock.sh process running - it will be cleaned up when the session ends
- Do NOT attempt to kill background shells that are running server processes

If you need to restart the mock server:
1. Use `pkill -f "dotnet.*mock"` to stop the dotnet process directly
2. Start a new mock server with `./scripts/mock.sh &`

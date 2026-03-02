# Development Instructions

This file contains instructions for Claude Code when working on this project.

## Overview

Homespun is a Blazor web application for managing development features and AI agents. It provides:
- Project and feature management with hierarchical tree visualization
- Git clone integration for isolated feature development
- GitHub PR synchronization
- Claude Code agent orchestration

## Development Practices

### Test Driven Development (TDD)

**TDD is mandatory for all planning and implementation.** When developing new features or fixing bugs:

1. **Write tests first** - Before implementing any code, write failing tests that define the expected behavior
2. **Red-Green-Refactor** - Follow the TDD cycle for every change:
   - Red: Write a failing test
   - Green: Write minimal code to make the test pass
   - Refactor: Clean up the code while keeping tests green
3. **Plan with tests in mind** - When planning features, identify test cases as part of the design
4. **Test naming** - Use descriptive test names that explain the scenario and expected outcome (e.g., `GetProjectById_ReturnsNotFound_WhenNotExists`)
5. **Test coverage** - Aim for comprehensive coverage of business logic in services

### Project Structure (Vertical Slice Architecture)

The project follows Vertical Slice Architecture with a Blazor WebAssembly (WASM) frontend and ASP.NET server backend. Code is organized by feature rather than technical layer.

```
src/
├── Homespun.Server/             # ASP.NET backend (API, SignalR hubs, services)
│   ├── Features/                # Feature slices (all business logic)
│   │   ├── AgentOrchestration/  # Agent lifecycle management
│   │   ├── ClaudeCode/          # Claude Code SDK session management
│   │   ├── Commands/            # Shell command execution
│   │   ├── Design/              # Design system component registry
│   │   ├── Fleece/              # Fleece issue tracking integration
│   │   ├── Git/                 # Git clone operations
│   │   ├── GitHub/              # GitHub API integration (Octokit)
│   │   ├── Navigation/          # Navigation services
│   │   ├── Notifications/       # Toast notifications via SignalR
│   │   ├── Projects/            # Project management
│   │   ├── PullRequests/        # PR workflow and data entities
│   │   └── SignalR/             # SignalR hub implementations
│   └── Program.cs               # Application entry point
├── Homespun.Client/             # Blazor WASM frontend
│   ├── Components/              # Shared Blazor components
│   ├── Layout/                  # Layout components
│   ├── Pages/                   # Page components
│   ├── Services/                # Client-side HTTP services
│   └── Program.cs               # WASM entry point
├── Homespun.Shared/             # Shared library (DTOs, contracts, hub interfaces)
│   ├── Models/                  # Shared data models
│   ├── Hubs/                    # SignalR hub interfaces
│   └── Requests/                # API request/response types
├── Homespun.ClaudeAgentSdk/     # Claude Code SDK C# wrapper
└── Homespun.Worker/             # TypeScript agent worker (Hono + Claude Agent SDK)

tests/
├── Homespun.Tests/              # Unit tests (NUnit + bUnit + Moq)
│   ├── Features/                # Tests organized by feature (mirrors src structure)
│   │   ├── ClaudeCode/          # ClaudeCode service and hub tests
│   │   ├── Git/                 # Git clone tests
│   │   ├── GitHub/              # GitHub service tests
│   │   └── PullRequests/        # PR model and workflow tests
│   ├── Components/              # bUnit tests for Blazor components
│   └── Helpers/                 # Shared test utilities and fixtures
├── Homespun.Api.Tests/          # API integration tests (WebApplicationFactory)
└── Homespun.E2E.Tests/          # End-to-end tests (Playwright)
```

### Feature Slices

- **Fleece**: Integration with Fleece issue tracking - JSONL-based storage in `.fleece/` directory, uses Fleece.Core types directly.
- **ClaudeCode**: Claude Code SDK session management using ClaudeAgentSdk NuGet package - supports Plan (read-only) and Build (full access) modes
- **Commands**: Shell command execution abstraction
- **Git**: Git clone creation, management, and rebase operations
- **GitHub**: GitHub PR synchronization and API operations using Octokit
- **Projects**: Project CRUD operations
- **PullRequests**: PR workflow, feature management, and core data entities (Feature, Project, PullRequest)

### Running the Application

**Do not under any circumstances stop a container called `homespun` or `homespun-prod`.**

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Homespun.Tests
dotnet test tests/Homespun.Api.Tests
dotnet test tests/Homespun.E2E.Tests

# Run with verbose output
dotnet test --verbosity normal
```

## Testing Infrastructure

The project uses a comprehensive three-tier testing strategy:

### Unit Tests (Homespun.Tests)

**Framework:** NUnit + bUnit + Moq

Unit tests cover service logic and Blazor components in isolation.

**Service tests** use Moq for dependency mocking.
**Component tests** use bUnit with a shared `BunitTestContext` base class.

### API Integration Tests (Homespun.Api.Tests)

**Framework:** NUnit + WebApplicationFactory

API tests verify HTTP endpoints using an in-memory test server with `HomespunWebApplicationFactory`.

### End-to-End Tests (Homespun.E2E.Tests)

**Framework:** NUnit + Playwright

E2E tests run against the full application stack using Playwright for browser automation:

**E2E Configuration:**
- `HomespunFixture` automatically starts the application for tests
- Set `E2E_BASE_URL` to test against an external server
- Set `E2E_CONFIGURATION` to specify build configuration (Release/Debug)

## UI 

The user interface is primarily developed using Blazor Blueprint. Use the MCP tools to gather documentation about components, or if this is not available simplified documentation is available at https://blazorblueprintui.com/llms/index.txt.

DO NOT create components if a similar Blazor Blueprint component already exists.

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

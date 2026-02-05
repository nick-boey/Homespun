# Development Instructions

This file contains instructions for Claude Code when working on this project.

## Overview

Homespun is a Blazor web application for managing development features and AI agents. It provides:
- Project and feature management with hierarchical tree visualization
- Git worktree integration for isolated feature development
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

The project follows Vertical Slice Architecture, organizing code by feature rather than technical layer. Each feature contains its own services, data models, and components.

```
src/Homespun/
├── Features/                    # Feature slices (all business logic)
│   ├── Fleece/                  # Fleece issue tracking integration
│   ├── ClaudeCode/              # Claude Code SDK session management
│   ├── Commands/                # Shell command execution
│   ├── Git/                     # Git worktree operations
│   ├── GitHub/                  # GitHub API integration (Octokit)
│   ├── Projects/                # Project management
│   ├── PullRequests/            # PR workflow and data entities
│   └── Notifications/           # Toast notifications via SignalR
├── Components/                  # Shared Blazor components
│   ├── Layout/                  # Layout components
│   ├── Pages/                   # Page components
│   └── Shared/                  # Reusable components
├── HealthChecks/                # Health check implementations
└── Program.cs                   # Application entry point

tests/
├── Homespun.Tests/              # Unit tests (NUnit + bUnit + Moq)
│   ├── Features/                # Tests organized by feature (mirrors src structure)
│   │   ├── ClaudeCode/          # ClaudeCode service and hub tests
│   │   ├── Git/                 # Git worktree tests
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
- **Git**: Git worktree creation, management, and rebase operations
- **GitHub**: GitHub PR synchronization and API operations using Octokit
- **Projects**: Project CRUD operations
- **PullRequests**: PR workflow, feature management, and core data entities (Feature, Project, PullRequest)

### Running the Application

The application is containerised and should always be run in a container. Helper Bash and PowerShell scripts are provided to build and run containers.

```bash
# Linux
./scripts/run.sh                # Production: Runs Watchtower and the latest container on GHCR
./scripts/run.sh --local        # Development: Builds a container from source and runs it
./scripts/mock.sh               # Testing: Builds a container from source using mock services for local UI testing

# Similar PowerShell scripts exist for windows
```

There are other various configuration variables that can be passed into the scripts as required. Environment variables are used for auth tokens, review the scripts if required to understand these.

**Do not under any circumstances stop a container called `homespun` or `homespun-prod`.** You may only stop containers named `homespun-mock-{hash}`.

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

### Data Storage

- JSON and JSONL file storage
- Data file: `homespun-data.json` (stored in `.homespun` directory)

## UI Development with Mock Mode

### Overview

The mock mode provides a development environment with:
- Pre-seeded demo data (projects, features, issues)
- No external dependencies (GitHub, Claude API)
- Isolated from production data

### Starting Mock Mode

```bash
./scripts/mock.sh       # Linux/Mac
./scripts/mock.ps1      # Windows
```

The scripts find an available port between 15000 and 16000 when running in this mode. Review the script output to find the URL to access.
Use the HTTP URL when accessing the mock container with Playwright.

When using Playwright MCP tools in such environments, use the HTTP URL shown in the console output rather than the HTTPS URL from the launch profile.

### Playwright MCP Tools

Key tools for UI inspection:
- `browser_navigate` - Navigate to URLs
- `browser_take_screenshot` - Capture visual state
- `browser_snapshot` - Get accessibility tree
- `browser_click` / `browser_type` - Interact with elements
- `browser_console_messages` - Check for JS errors

## Container Playwright MCP Usage

Most of the development of Homespun comes from agents running within the application container itself. Consider this when testing a mock container using the Playwright MCP tools. There are important networking considerations when starting and accessing mock containers within the application container.

### Browser Installation

Playwright browsers are pre-installed at `/opt/playwright-browsers`. The `PLAYWRIGHT_BROWSERS_PATH` environment variable is automatically configured. No additional setup is required.

### Container Networking

**Important:** From inside a Docker container, `localhost` refers to the container itself, not the host machine or sibling containers.

#### Accessing Sibling Containers (DooD)

Docker-outside-of-Docker is used to spawn mock containers:

1. **Start a mock container:**
   ```bash
   ./scripts/mock.sh
   ```
   Note the container name and port from the output (e.g., `homespun-mock-ba1185a8` on port `15633`).

2. **Find the container's Docker network IP:**
   ```bash
   docker inspect -f '{{range.NetworkSettings.Networks}}{{.IPAddress}}{{end}}' homespun-mock-ba1185a8
   # Returns: 172.17.0.3 (example)
   ```

3. **Use the IP address with Playwright MCP tools:**
   - Navigate: `browser_navigate` to `http://172.17.0.3:8080/projects`
   - Screenshot: `browser_take_screenshot`

**Note:** The port inside the container is always `8080`, regardless of the host-mapped port.

#### Accessing the Host Machine

- **Linux hosts**: Use the Docker bridge IP, typically `172.17.0.1`
- **Docker Desktop (Mac/Windows)**: Use `host.docker.internal`

**Note: Do not edit any files on the host machine or stop/modify any containers that are not a mock container that you created.**

### Troubleshooting

| Issue | Solution |
|-------|----------|
| "Browser not installed" error | Verify: `ls /opt/playwright-browsers/` |
| Connection refused to localhost | Use Docker network IP instead of localhost |
| Permission denied on browser | Check `PLAYWRIGHT_BROWSERS_PATH` is set to `/opt/playwright-browsers` |

## Styling with Tailwind CSS

When styling components, always use Tailwind CSS utility classes. Avoid inline styles and prefer using:
- Tailwind utility classes directly in markup
- Component classes defined in `wwwroot/css/tailwind.css` under `@layer components`
- CSS variables defined in `wwwroot/css/variables.css` when custom values are needed

Build Tailwind CSS after making changes to the CSS files:
```bash
cd src/Homespun && npm run css:build
```

## Design System and Component Showcases

The design system at `/design` provides a catalog of all UI components with mock data for visual testing. This is only available in mock mode.

A further description on how to use the design system is at `./src/Homespun/Components/CLAUDE.md`. Always create and update the showcase when creating and modifying components.

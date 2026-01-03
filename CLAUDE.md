# Development Instructions

This file contains instructions for Claude Code when working on this project.

## Overview

TreeAgent is a Blazor web application for managing development features and AI agents. It provides:
- Project and feature management with hierarchical tree visualization
- Git worktree integration for isolated feature development
- GitHub PR synchronization
- Claude Code agent orchestration with real-time message streaming
- Customizable system prompts with template variables

## Development Practices

### Test Driven Development (TDD)

Use Test Driven Development practices where possible:

1. **Write tests first** - Before implementing a feature, write failing tests that define the expected behavior
2. **Red-Green-Refactor** - Follow the TDD cycle:
   - Red: Write a failing test
   - Green: Write minimal code to make the test pass
   - Refactor: Clean up the code while keeping tests green
3. **Test naming** - Use descriptive test names that explain the scenario and expected outcome
4. **Test coverage** - Aim for comprehensive coverage of business logic in services

### Project Structure (Vertical Slice Architecture)

The project follows Vertical Slice Architecture, organizing code by feature rather than technical layer.

```
src/TreeAgent.Web/
├── Features/                    # Feature slices
│   ├── Agents/                  # Claude Code agent management
│   │   ├── Components/Pages/    # Agent UI pages
│   │   ├── Data/                # Agent-related entities
│   │   ├── Hubs/                # SignalR hub for agents
│   │   └── Services/            # Agent services
│   └── PullRequests/            # GitHub PR management
│       └── Services/            # GitHub services
├── Components/                  # Shared Blazor components
│   ├── Layout/                  # Layout components
│   ├── Pages/                   # Shared page components
│   └── Shared/                  # Shared/reusable components
├── Data/
│   └── Entities/                # Shared EF Core entities
├── HealthChecks/                # Health check implementations
├── Migrations/                  # EF Core migrations
├── Services/                    # Shared services
└── Program.cs                   # Application entry point

tests/TreeAgent.Web.Tests/
├── Features/                    # Tests organized by feature
│   ├── Agents/
│   │   ├── Services/            # Agent service unit tests
│   │   └── Integration/         # Agent integration tests
│   └── PullRequests/
│       └── Services/            # GitHub service tests
├── Integration/
│   └── Fixtures/                # Test fixtures
└── Services/                    # Shared service tests
```

### Feature Slices

- **Agents**: Claude Code agent orchestration, process management, message streaming
- **PullRequests**: GitHub PR synchronization using Octokit

### Running the Application

```bash
cd src/TreeAgent.Web
dotnet run
```

The application will be available at `https://localhost:5001` (or the configured port).

### Running Tests

```bash
dotnet test
```

### Database

- SQLite database with EF Core
- Migrations applied automatically on startup
- Database file: `treeagent.db` (gitignored)

### Creating Migrations

```bash
cd src/TreeAgent.Web
dotnet ef migrations add <MigrationName>
```

## Key Services

### Shared Services (in Services/)
- **ProjectService**: CRUD operations for projects
- **FeatureService**: Feature management with tree structure and worktree integration
- **GitWorktreeService**: Git worktree operations
- **SystemPromptService**: Template processing for agent system prompts

### Agents Feature (in Features/Agents/)
- **AgentService**: Agent lifecycle management with Claude Code process orchestration
- **ClaudeCodeProcessManager**: Process pool management for Claude Code instances
- **ClaudeCodePathResolver**: Platform-aware Claude Code executable discovery
- **MessageParser**: JSON message parser for Claude Code output
- **AgentHub**: SignalR hub for real-time agent updates

### PullRequests Feature (in Features/PullRequests/)
- **GitHubService**: GitHub PR synchronization using Octokit
- **GitHubClientWrapper**: Octokit abstraction for testability

## Configuration

Environment variables:
- `TREEAGENT_DB_PATH`: Path to SQLite database (default: `treeagent.db`)
- `GITHUB_TOKEN`: GitHub personal access token for PR operations

## Health Checks

The application exposes a health check endpoint at `/health` that monitors:
- Database connectivity
- Process manager status

## Real-time Updates

SignalR is used for real-time updates:
- Agent message streaming
- Agent status changes
- Connect to `/hubs/agent` for agent-related updates

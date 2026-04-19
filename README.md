# Homespun

Homespun is a web application for managing development features and AI agents. It provides:
- Project and feature management with hierarchical tree visualization
- Git clone integration for isolated feature development
- GitHub PR synchronization
- Claude Code agent orchestration

## Overview

Homespun provides a visual interface for planning and executing software development through AI agents. It manages:

- **Feature Tree**: Visualize past, present, and future pull requests as a tree structure
- **Multiple Projects**: Work on multiple repositories simultaneously
- **Git Clones**: Automatically manage clones for each feature branch
- **Claude Code Agents**: Spawn and manage Claude Code SDK sessions
- **Issue Tracking**: Integration with Fleece issue tracking
- **Real-time Updates**: Live updates via SignalR

## Technology Stack

### Frontend
- **React 19** with TypeScript
- **Vite** for build tooling
- **TailwindCSS v4** for styling
- **TanStack Router** for file-based routing
- **TanStack Query** for server state management
- **Zustand** for client state management
- **shadcn/ui** for UI components (New York style, Zinc base color)
- **prompt-kit** for AI chat interface components

### Backend
- **ASP.NET Core** (.NET 8)
- **SignalR** for real-time communication
- **Fleece** for issue tracking (JSONL-based)
- **Claude Code SDK** for AI agent integration
- **Octokit** for GitHub API integration

## Features

- Tree-based visualization of feature development
- Sync with GitHub pull requests (open, closed, merged)
- Plan future features before development begins
- Drill down into agent message streams
- Real-time updates via SignalR
- Custom system prompts with template variables
- Color-coded feature status:
  - Blue: Merged
  - Red: Cancelled/Closed
  - Yellow: In development
  - Green: Ready for review
  - Orange: Has review comments
  - Cyan: Approved
  - Grey: Future/planned
- Automated review polling from GitHub
- Agent status panel with real-time updates
- Fleece integration for local issue tracking

## Getting Started

### Prerequisites

- .NET 8 SDK or later
- Node.js 18+ and npm
- Git
- Claude Code CLI installed and configured
- GitHub personal access token (for PR synchronization)

### Installation

```bash
git clone https://github.com/your-org/Homespun.git
cd Homespun

# Backend
dotnet restore
dotnet build

# Frontend
cd src/Homespun.Web
npm install
```

### Configuration

Set the following environment variables before running:

| Variable | Description | Default |
|----------|-------------|---------|
| `HOMESPUN_DATA_PATH` | Path to data file | `~/.homespun/homespun-data.json` |
| `GITHUB_TOKEN` | GitHub personal access token for PR operations | (required for GitHub sync) |
| `HOMESPUN_CLONE_ROOT` | Base directory for clones | (uses project path) |

Example:
```bash
export GITHUB_TOKEN="ghp_your_token_here"
export HOMESPUN_DATA_PATH="/data/.homespun/homespun-data.json"
```

### Running

#### Development Mode

Local dev runs through the Aspire AppHost. Prerequisites: .NET 10 SDK + Aspire
workload, Node 20+, Docker Desktop. One-time secret bootstrap:

```bash
# macOS/Linux
./scripts/set-user-secrets.sh
# Windows
./scripts/set-user-secrets.ps1
```

Then launch the full dev stack via one of the AppHost profiles. Use
`dotnet run` — the `aspire` CLI's `run` command does not support
`--launch-profile` and will silently fall back to the first profile.

```bash
# Mock data + mock agents (fastest inner loop)
dotnet run --project src/Homespun.AppHost --launch-profile dev-mock

# Mock data + real Claude SDK sessions, Docker-spawned workers (DooD)
dotnet run --project src/Homespun.AppHost --launch-profile dev-live

# Mock data + pre-run worker container (for Windows hosts)
dotnet run --project src/Homespun.AppHost --launch-profile dev-windows

# Container parity check (server/web/worker all from Dockerfiles)
dotnet run --project src/Homespun.AppHost --launch-profile dev-container
```

Server: `http://localhost:5101`. Vite: `http://localhost:5173`. Grafana: `:3000`.
The Aspire dashboard URL is printed at startup.

## Development

### Project Structure

The project follows Vertical Slice Architecture where code is organized by feature rather than technical layer:

```
src/
├── Homespun.Server/             # ASP.NET backend
│   ├── Features/                # Feature slices
│   │   ├── AgentOrchestration/  # Agent lifecycle management
│   │   ├── ClaudeCode/          # Claude Code SDK session management
│   │   ├── Commands/            # Shell command execution
│   │   ├── Fleece/              # Fleece issue tracking integration
│   │   ├── Git/                 # Git clone operations
│   │   ├── GitHub/              # GitHub API integration (Octokit)
│   │   ├── Notifications/       # Toast notifications via SignalR
│   │   ├── Projects/            # Project management
│   │   └── PullRequests/        # PR workflow and data entities
├── Homespun.Web/                # React frontend
│   ├── src/
│   │   ├── features/            # Feature slices
│   │   ├── components/ui/       # shadcn/ui components
│   │   ├── api/                 # OpenAPI generated client
│   │   └── routes/              # TanStack Router routes
├── Homespun.Shared/             # Shared DTOs and contracts
├── Homespun.ClaudeAgentSdk/     # Claude Code SDK C# wrapper
└── Homespun.Worker/             # TypeScript agent worker
```

### Frontend Development

```bash
cd src/Homespun.Web

# Development server with hot reload
npm run dev

# Build for production
npm run build

# Linting and formatting
npm run lint
npm run lint:fix
npm run format
npm run format:check

# Type checking
npm run typecheck

# Generate API client from OpenAPI spec
npm run generate:api:fetch
```

### Testing

#### Frontend Tests

```bash
cd src/Homespun.Web

# Run tests once
npm test

# Run tests in watch mode
npm run test:watch

# Run tests with coverage
npm run test:coverage

# Run E2E tests
npm run playwright:install  # First time only
npm run test:e2e
npm run test:e2e:ui        # Interactive mode
```

#### Backend Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Homespun.Tests
dotnet test tests/Homespun.Api.Tests
dotnet test tests/Homespun.E2E.Tests
```

### Pre-PR Checklist

Before creating a pull request:

```bash
cd src/Homespun.Web

# Must pass with no errors
npm run lint
npm run typecheck
npm test
```

## Usage Guide

### Managing Projects

1. **Create a Project**: Navigate to the Projects page and click "Create Project"
2. **Configure Repository**: Enter the local Git repository path and optionally configure GitHub integration (owner/repo)
3. **Set Default Branch**: Specify the main branch name (e.g., `main` or `master`)

### Working with Features

1. **View Feature Tree**: Click on a project to see its feature tree
2. **Create a Feature**: Click "Add Feature" to create a new planned feature
   - Enter title, description, and branch name
   - Optionally set a parent feature to create hierarchical relationships
3. **Sync with GitHub**: Use the "Sync" button to import existing pull requests
4. **Start Development**: Click "Start" on a feature to:
   - Create a Git clone automatically
   - Spawn a Claude Code agent
   - Begin development in isolation

### Managing Issues with Fleece

Homespun integrates with Fleece for local issue tracking:

1. **View Issues**: Issues are displayed in the sidebar
2. **Create Issues**: Use the Fleece CLI or UI to create new issues
3. **Update Status**: Progress issues through workflow states:
   ```
   open → progress → review → complete
                           ↘ archived
                           ↘ closed
   ```
4. **Link to PRs**: Issues are automatically linked to pull requests

### Managing Agents

1. **View Agents**: Navigate to the Agents dashboard to see all active agents
2. **Agent Modes**:
   - **Plan Mode**: Read-only access for planning
   - **Build Mode**: Full access for implementation
3. **Monitor Messages**: Click on an agent to view its message stream in real-time
4. **Stop Agents**: Use the "Stop" button to gracefully terminate an agent

### System Prompts

Homespun supports customizable system prompts with template variables:

| Variable | Description |
|----------|-------------|
| `{{ProjectName}}` | Name of the current project |
| `{{FeatureTitle}}` | Title of the feature being worked on |
| `{{FeatureDescription}}` | Description of the feature |
| `{{BranchName}}` | Git branch name |
| `{{ClonePath}}` | Path to the clone directory |

## API Endpoints

The application exposes an OpenAPI-documented REST API. Key endpoints include:

- `/api/projects` - Project management
- `/api/features` - Feature/PR management
- `/api/agents` - Agent orchestration
- `/api/fleece` - Issue tracking
- `/api/github` - GitHub sync operations

SignalR hubs:
- `/hubs/agent` - Agent message streaming and status updates
- `/hubs/notifications` - Toast notifications

## Deployment

Homespun can be deployed using Docker containers.

### Docker Deployment

```bash
# Build the image
docker build -t homespun:local .

# Run with GitHub token
docker run -e GITHUB_TOKEN="ghp_..." -p 8080:8080 homespun:local
```

### Pre-built Images

Pre-built images are available from GitHub Container Registry:

```bash
docker pull ghcr.io/nick-boey/homespun:latest
```

### Data Persistence

Data is stored in `~/.homespun-container/data/` including:
- JSON data file
- Fleece issue tracking data
- Data protection keys

## Documentation

- [CLAUDE.md](CLAUDE.md) - Development instructions for Claude Code
- [Installation Guide](docs/installation.md) - Deployment options
- [SPECIFICATION.md](SPECIFICATION.md) - Technical specification
- [ROADMAP.md](ROADMAP.md) - Development roadmap

## Known Issues

### Agent UI Shows Wrong Branch

When running Claude Code in a Git clone, the UI may display the default branch instead of the actual clone branch. This is a known issue with the agent's VCS module. Use `git branch` in the terminal to verify the actual branch.

## License

MIT
# TreeAgent

TreeAgent is a .NET application for managing software development workflows using Git, GitHub, and agentic AI tools like Claude Code. It models development as a tree where each node represents a feature or fix aligned with GitHub pull requests.

## Overview

TreeAgent provides a visual interface for planning and executing software development through AI agents. It manages:

- **Feature Tree**: Visualize past, present, and future pull requests as a tree structure
- **Multiple Projects**: Work on multiple repositories simultaneously
- **Git Worktrees**: Automatically manage worktrees for each feature branch
- **Claude Code Agents**: Spawn and manage headless Claude Code instances
- **Message Persistence**: Store all agent communications in SQLite

## Features

- Tree-based visualization of feature development
- Sync with GitHub pull requests (open, closed, merged)
- Plan future features before development begins
- Drill down into agent message streams
- Color-coded feature status:
  - Blue: Merged
  - Red: Cancelled
  - Yellow: In development
  - Green: Ready for review
  - Grey: Future/planned

## Technology Stack

- **.NET 8+**: Core runtime
- **Blazor Server**: Web frontend
- **SQLite + EF Core**: Persistence
- **Claude Code CLI**: AI agent
- **Tailscale**: Optional secure remote access

## Getting Started

### Prerequisites

- .NET 8 SDK or later
- Git
- Claude Code CLI

### Installation

```bash
git clone https://github.com/your-org/TreeAgent.git
cd TreeAgent
dotnet restore
dotnet build
```

### Running

```bash
dotnet run --project src/TreeAgent.Web
```

## Documentation

- [SPECIFICATION.md](SPECIFICATION.md) - Technical specification
- [ROADMAP.md](ROADMAP.md) - Development roadmap

## License

MIT

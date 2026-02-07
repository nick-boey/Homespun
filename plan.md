# Plan: Check Agent and Main Dependencies (Un2Nkx)

## Analysis Summary

After thorough review, **the agent container (AgentWorker) Dockerfile runtime dependencies already closely match the main container's Dockerfile runtime dependencies**. Fleece CLI is already installed in the agent container. The two Dockerfiles are well-aligned, with only intentional differences related to their different purposes.

## Detailed Dependency Comparison

### Docker Runtime Stage Dependencies (system-level tools)

| Dependency | Main Container | Agent Container | Status |
|---|---|---|---|
| Base image: `dotnet/sdk:10.0` | ✅ | ✅ | Match |
| git | ✅ | ✅ | Match |
| curl | ✅ | ✅ | Match |
| ca-certificates | ✅ | ✅ | Match |
| gnupg | ✅ | ✅ | Match |
| Node.js (LTS) | ✅ | ✅ | Match |
| GitHub CLI (gh) | ✅ | ✅ | Match |
| build-essential (temp) | ✅ install+remove | ✅ install+remove | Match |
| python3-setuptools (temp) | ✅ install+remove | ✅ install+remove | Match |
| Claude Code CLI (npm) | ✅ | ✅ | Match |
| Playwright MCP + Chromium | ✅ | ✅ | Match |
| **Fleece CLI (dotnet tool)** | ✅ | ✅ | **Match** |
| Docker CLI (docker-ce-cli) | ✅ | ✅ | Match |
| Tailscale | ✅ | ❌ | Intentional diff |

### .NET NuGet Dependencies (application-level)

| Package | Main (Homespun.csproj) | Agent (AgentWorker.csproj) | Notes |
|---|---|---|---|
| Swashbuckle.AspNetCore 10.1.0 | ✅ | ✅ | Match |
| Homespun.ClaudeAgentSdk (project ref) | ✅ | ✅ | Match |
| Azure.Identity 1.13.2 | ✅ | ❌ | Main-only (Azure auth) |
| Fleece.Core 1.0.0 | ✅ | ❌ | Main-only (issue tracking UI) |
| Markdig 0.44.0 | ✅ | ❌ | Main-only (markdown rendering) |
| Microsoft.AspNetCore.SignalR.Client 10.0.1 | ✅ | ❌ | Main-only (real-time UI) |
| Microsoft.Data.Sqlite 10.0.1 | ✅ | ❌ | Main-only (data storage) |
| Octokit 14.0.0 | ✅ | ❌ | Main-only (GitHub API) |

### Container Setup & Configuration

| Configuration | Main | Agent | Notes |
|---|---|---|---|
| Non-root user (`homespun`) | ✅ | ✅ | Match |
| `/data` directory | ✅ | ✅ | Match |
| `.claude` directory structure | ✅ | ✅ | Match |
| `git config safe.directory '*'` | ✅ | ✅ | Match |
| `PLAYWRIGHT_BROWSERS_PATH` | ✅ | ✅ | Match |
| `DOTNET_PRINT_TELEMETRY_MESSAGE` | ✅ | ✅ | Match |
| Port 8080 | ✅ | ✅ | Match |
| Health check | ✅ | ❌ | Main-only |
| Tailscale setup | ✅ | ❌ | Main-only |

## Findings

### ✅ Fleece is installed in the agent container
The Fleece CLI is already installed in the AgentWorker Dockerfile at lines 68-73:
```dockerfile
RUN dotnet tool install Fleece.Cli -g \
    && cp /root/.dotnet/tools/fleece /usr/local/bin/fleece \
    && chmod 755 /usr/local/bin/fleece
```

### ✅ All critical runtime dependencies match
Both containers have identical installations of: git, Node.js, GitHub CLI, Claude Code CLI, Playwright MCP + Chromium, Fleece CLI, and Docker CLI.

### ✅ Intentional differences are appropriate
- **Tailscale**: Only in main container — the agent doesn't need VPN access since it's managed by the orchestrator
- **NuGet packages differ**: The AgentWorker is a minimal API service that delegates to Claude Code CLI. It doesn't need Blazor UI packages (Markdig, SignalR), data storage (SQLite), or direct GitHub/Azure APIs (Octokit, Azure.Identity). The Fleece NuGet package (`Fleece.Core`) is only needed in the main app for the issue tracking UI, while the agent uses the `fleece` CLI tool directly
- **Health check**: Only in main container Dockerfile (agent health is managed by the session pool/Docker orchestration)

### Minor inconsistency (non-blocking)
The main Dockerfile installs Node.js and system dependencies in **separate** `RUN` commands (lines 67-72 for git/curl/ca-certs/gnupg, then lines 83-85 for Node.js), while the agent Dockerfile combines them into a **single** `RUN` command (lines 31-38). This is purely stylistic and has no functional impact.

## Recommendation

**No changes are needed.** The agent container dependencies already match the main container dependencies where appropriate. Fleece CLI is installed in both containers. The differences that exist (Tailscale, NuGet packages) are intentional and correct for each container's purpose.

### Steps

1. **No code changes required** — Dependencies are already aligned
2. Optionally, consider adding a brief comment to the AgentWorker Dockerfile referencing the main Dockerfile to help future maintainers keep them in sync (e.g., `# NOTE: Keep runtime dependencies in sync with root Dockerfile`)

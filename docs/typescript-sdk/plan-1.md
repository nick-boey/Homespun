# Phase 1: Create Hono Worker (`src/Homespun.Worker/`) ✅ COMPLETE

## Context

The current agent worker (`Homespun.AgentWorker`) is an ASP.NET Core app wrapping `Homespun.ClaudeAgentSdk` (a C# port of the Python Claude Agent SDK). This phase replaces it with a lightweight Hono (TypeScript) server using the official `@anthropic-ai/claude-agent-sdk` V2 preview. The Hono server is a thin pass-through that exposes SDK messages over REST/SSE.

## 1.1 Project Setup

Create a new TypeScript project at `src/Homespun.Worker/`:

```
src/Homespun.Worker/
  package.json              # hono, @anthropic-ai/claude-agent-sdk, @hono/node-server
  tsconfig.json
  src/
    index.ts                # Hono app entry + server start
    routes/
      health.ts             # GET /health
      info.ts               # GET /info (issue/project metadata)
      sessions.ts           # Session CRUD + SSE streaming
      files.ts              # POST /files/read
    services/
      session-manager.ts    # Manages V2 SDK Session objects
      sse-writer.ts         # Converts SDKMessage stream to SSE format
      session-discovery.ts  # Scans .claude/ for existing sessions
    types/
      index.ts              # Request/response DTOs
  Dockerfile                # Standalone Node.js-based image
  start.sh                  # Git config, env setup, then node dist/index.js
```

## 1.2 Hono Endpoints

| Method | Path | Description | Response |
|--------|------|-------------|----------|
| `GET` | `/health` | Health check | `{ status: "ok" }` |
| `GET` | `/info` | Container metadata | `{ issueId, projectId, projectName, status }` |
| `GET` | `/sessions` | List sessions (active + discoverable from `.claude/`) | `{ sessions: [...], activeSessionId? }` |
| `POST` | `/sessions` | Start or resume session | SSE stream of `SDKMessage` |
| `POST` | `/sessions/:id/message` | Send message to active session | SSE stream of `SDKMessage` |
| `POST` | `/sessions/:id/interrupt` | Interrupt current turn | `{ ok: true }` |
| `DELETE` | `/sessions/:id` | Close session | `{ ok: true }` |
| `POST` | `/files/read` | Read file from container filesystem | `{ filePath, content }` |

## 1.3 SDK Integration (V2 Preview)

Use `unstable_v2_createSession()` and `unstable_v2_resumeSession()` from `@anthropic-ai/claude-agent-sdk`:

```typescript
// session-manager.ts
class SessionManager {
  private sessions = new Map<string, Session>();

  async create(opts: { prompt: string; model: string; mode: string; systemPrompt?: string }): Promise<string>;
  async resume(sessionId: string, opts: { model: string }): Promise<string>;
  async send(sessionId: string, message: string): Promise<void>;
  stream(sessionId: string): AsyncGenerator<SDKMessage>;
  async interrupt(sessionId: string): Promise<void>;
  async close(sessionId: string): Promise<void>;
  list(): SessionInfo[];
}
```

Session options configured per mode:
- **Plan mode**: `permissionMode: 'plan'`, `allowedTools: ['Read', 'Glob', 'Grep', 'WebFetch', 'WebSearch', 'Task', 'AskUserQuestion', 'ExitPlanMode']`
- **Build mode**: `permissionMode: 'bypassPermissions'`, `allowDangerouslySkipPermissions: true`
- Both modes: `includePartialMessages: true`, `settingSources: ['user', 'project']`, `systemPrompt: { type: 'preset', preset: 'claude_code', append: customSystemPrompt }`, Playwright MCP server configured

## 1.4 SSE Streaming Format

Pass `SDKMessage` objects directly as SSE events, keyed by their `type` field:

```
event: system
data: {"type":"system","subtype":"init","uuid":"...","session_id":"...","tools":[...],...}

event: assistant
data: {"type":"assistant","uuid":"...","session_id":"...","message":{...},"parent_tool_use_id":null}

event: stream_event
data: {"type":"stream_event","event":{...},"parent_tool_use_id":null,"uuid":"...","session_id":"..."}

event: result
data: {"type":"result","subtype":"success","uuid":"...","total_cost_usd":0.05,...}
```

Custom events for lifecycle:
```
event: session_started
data: {"sessionId":"abc-123"}

event: error
data: {"message":"...","code":"...","isRecoverable":true}
```

## 1.5 Environment Variables

Read from environment at startup:
- `ISSUE_ID`, `PROJECT_ID`, `PROJECT_NAME` - Issue context
- `CLAUDE_CODE_OAUTH_TOKEN` / `ANTHROPIC_API_KEY` - Auth
- `GITHUB_TOKEN` - GitHub access
- `GIT_AUTHOR_NAME`, `GIT_AUTHOR_EMAIL`, `GIT_COMMITTER_NAME`, `GIT_COMMITTER_EMAIL` - Git identity
- `PORT` (default `8080`) - Server port
- `WORKING_DIRECTORY` - The repo clone path (defaults to `/workdir`)

## 1.6 Standalone Dockerfile

```dockerfile
FROM node:22-bookworm-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src/ src/
RUN npm run build

FROM node:22-bookworm-slim
WORKDIR /app

# Install git, curl, gh CLI
RUN apt-get update && apt-get install -y --no-install-recommends \
    git curl ca-certificates gnupg sudo \
    && rm -rf /var/lib/apt/lists/*

# GitHub CLI
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
      | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
      > /etc/apt/sources.list.d/github-cli.list \
    && apt-get update && apt-get install -y gh && rm -rf /var/lib/apt/lists/*

# Claude Code CLI + Playwright MCP + Chromium
RUN npm install -g @anthropic-ai/claude-code @playwright/mcp@latest
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/playwright-browsers
RUN npx playwright install chromium --with-deps \
    && chmod -R 777 /opt/playwright-browsers

# Copy built app
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./
COPY start.sh ./
RUN chmod +x start.sh

# Non-root user
RUN useradd --create-home --shell /bin/bash homespun \
    && mkdir -p /home/homespun/.claude /workdir \
    && chown -R homespun:homespun /home/homespun /workdir

USER homespun
ENV HOME=/home/homespun PORT=8080
EXPOSE 8080
ENTRYPOINT ["./start.sh"]
```

## Reference Files (Current Implementation)
- `src/Homespun.AgentWorker/Services/WorkerSessionService.cs` - Session management logic to reimplement
- `src/Homespun.AgentWorker/Controllers/SessionsController.cs` - Endpoint contracts to match
- `src/Homespun.AgentWorker/Models/AgentModels.cs` - Current DTOs (for reference)
- `src/Homespun.AgentWorker/Controllers/FilesController.cs` - File reading endpoint to match

## Verification

1. `npm run build` compiles without errors
2. `npm test` passes (mock SDK tests for session lifecycle)
3. `docker build -t homespun-worker:local src/Homespun.Worker/` succeeds
4. Container starts and `GET /health` returns 200
5. `POST /sessions` with a test prompt returns SSE stream with `system`, `assistant`, and `result` events
6. `POST /sessions/:id/message` sends a follow-up and streams response
7. `DELETE /sessions/:id` closes the session cleanly

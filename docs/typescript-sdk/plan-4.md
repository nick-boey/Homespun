# Phase 4: Docker Execution Service (Per-Issue Containers)

## Context

The current `DockerAgentExecutionService` creates a new container per session (ephemeral, `--rm`). This phase refactors it to create containers per Fleece issue. Containers are reused across sessions for the same issue and persist until explicitly stopped. This enables natural session resumption and ties agent work to specific issues.

**Dependencies:** Phases 1 (Hono worker), 2 (SdkMessage types), 3 (IssueWorkspaceService)

## 4.1 Refactor `DockerAgentExecutionService`

### Container Naming
`homespun-issue-{issueId}` (deterministic, enables reuse detection)

### Container Tracking
```csharp
private record IssueContainer(
    string IssueId,
    string ContainerId,
    string ContainerName,
    string WorkerUrl,
    DateTime CreatedAt
);

// key: issueId
private readonly ConcurrentDictionary<string, IssueContainer> _containers = new();
// sessionId -> issueId (for routing messages to the right container)
private readonly ConcurrentDictionary<string, string> _sessionToIssue = new();
```

### `StartSessionAsync` Flow
1. Extract `issueId` from `AgentStartRequest`
2. Check if container `homespun-issue-{issueId}` exists in `_containers`
3. If not running, call `StartContainerAsync` with per-issue mounts
4. Send `POST /sessions` to the worker with prompt, mode, model, systemPrompt
5. Stream SSE events back as `SdkMessage` types, mapping session ID
6. Store `sessionId -> issueId` mapping for subsequent messages

### Mount Changes (in `StartContainerAsync`)
```
docker run -d \
  --name homespun-issue-{issueId} \
  --memory 4GB --cpus 2.0 \
  --user {uid}:{gid} \
  -v {hostPath}/projects/{name}/issues/{id}/.claude:/home/homespun/.claude \
  -v {hostPath}/projects/{name}/issues/{id}/src:/workdir \
  -e ISSUE_ID={issueId} \
  -e PROJECT_ID={projectId} \
  -e PROJECT_NAME={projectName} \
  -e WORKING_DIRECTORY=/workdir \
  -e CLAUDE_CODE_OAUTH_TOKEN=... \
  -e GITHUB_TOKEN=... \
  -e GIT_AUTHOR_NAME=... \
  -e GIT_AUTHOR_EMAIL=... \
  -e GIT_COMMITTER_NAME=... \
  -e GIT_COMMITTER_EMAIL=... \
  --network bridge \
  homespun-worker:latest
```

Key differences from current:
- No `--rm` flag (containers persist)
- Specific per-issue mounts instead of entire `/data` volume
- Issue/project env vars added
- `WORKING_DIRECTORY` env var for the worker to know its cwd

## 4.2 Container Reuse

When sending a follow-up message or starting a new session on the same issue:
1. Look up existing container by `issueId` in `_containers`
2. Verify container is healthy (`GET /health`)
3. If healthy, use existing container URL for the session request
4. If unhealthy (container died), remove from tracking and start a new one

## 4.3 Container Lifecycle

| Action | Behavior |
|--------|----------|
| Stop agent session | `DELETE /sessions/:id` on worker - container stays running (idle) |
| Stop container | `docker stop homespun-issue-{issueId}` - explicit user action or cleanup |
| App shutdown | Stop all managed containers via `DisposeAsync` |
| Issue completed | Stop and remove the container |

## 4.4 SSE Parsing Update

Replace the current custom event parsing:

**Current events:** `SessionStarted`, `ContentBlockReceived`, `MessageReceived`, `ResultReceived`, `QuestionReceived`, `SessionEnded`, `Error`

**New events:** `system`, `assistant`, `user`, `result`, `stream_event`, `session_started`, `error`

Use the `SdkMessageParser` (from Phase 2) to deserialize SSE data into `SdkMessage` types. The SSE reading loop in `SendSseRequestAsync` changes from:

```csharp
// Old: eventType switch to specific AgentEvent types
"SessionStarted" => ParseJson<AgentSessionStartedEvent>(data),
"ContentBlockReceived" => ParseContentBlockEvent(data, sessionId),
```

To:

```csharp
// New: eventType maps directly to SdkMessage types
"assistant" => ParseJson<SdkAssistantMessage>(data),
"result" => ParseJson<SdkResultMessage>(data),
"stream_event" => ParseJson<SdkStreamEvent>(data),
"session_started" => /* extract sessionId, create tracking entry */,
"error" => /* handle error */,
```

## Critical Files to Modify
- `src/Homespun/Features/ClaudeCode/Services/DockerAgentExecutionService.cs` - Major refactor (~802 lines)

## Tests
- Container naming: verify `homespun-issue-{issueId}` format
- Container reuse: verify same container is used for multiple sessions on same issue
- Mount paths: verify correct host-to-container path mapping
- SSE parsing: verify new SDK message event format is parsed correctly
- Lifecycle: verify containers are not removed on session stop, only on explicit container stop

## Verification
1. `dotnet test` passes
2. Start agent for an issue -> verify container named `homespun-issue-{issueId}` is created
3. `docker inspect` shows correct volume mounts (`.claude` and `src`)
4. Send messages -> verify SSE streaming works with new format
5. Stop session -> verify container is still running
6. Start new session on same issue -> verify same container is reused
7. Stop container explicitly -> verify container is removed
8. Session resumption: stop and restart agent, verify previous session is discoverable via `GET /sessions`

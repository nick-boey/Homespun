# Test Session Data

This directory contains JSONL session files used for mock mode testing.

## Directory Structure

```
sessions/
├── {project-id}/
│   ├── {session-id}.jsonl      # Message data (one JSON object per line)
│   └── {session-id}.meta.json  # Session metadata
└── README.md
```

## File Formats

### JSONL Messages (`{session-id}.jsonl`)

Each line is a JSON object representing a `ClaudeMessage`:

```json
{
  "id": "message-uuid",
  "sessionId": "session-uuid",
  "role": 0,
  "content": [...],
  "createdAt": "2026-02-01T11:36:29.769433Z",
  "isStreaming": false
}
```

- `role`: 0 = User, 1 = Assistant
- `content`: Array of `ClaudeMessageContent` blocks

### Metadata (`{session-id}.meta.json`)

```json
{
  "sessionId": "session-uuid",
  "entityId": "pr-logging",
  "projectId": "demo-project",
  "messageCount": 26,
  "createdAt": "2026-02-01T11:35:42.510258Z",
  "lastMessageAt": "2026-02-01T11:45:11.647477Z",
  "mode": 1,
  "model": "opus",
  "status": 3,
  "planContent": "# Plan content here...",
  "planFilePath": "PLAN.md"
}
```

- `mode`: 0 = Plan, 1 = Build
- `entityId`: Links to mock PR/issue (e.g., `pr-logging` matches `MockDataSeederService` PR)
- `status` (optional): Session status enum value:
  - 0 = Starting
  - 1 = RunningHooks
  - 2 = Running
  - 3 = WaitingForInput (default)
  - 4 = WaitingForQuestionAnswer
  - 5 = WaitingForPlanExecution
  - 6 = Stopped
  - 7 = Error
- `planContent` (optional): The plan content to display when status is WaitingForPlanExecution
- `planFilePath` (optional): Path to the plan file

## Adding New Sessions

1. Create a subdirectory matching the project ID from `MockDataSeederService`
2. Add `.jsonl` and `.meta.json` files with a unique session ID (UUID)
3. Ensure `entityId` matches an existing mock PR or issue
4. Rebuild the Docker image

## Loading

`MockDataSeederService` automatically loads sessions from `/data/sessions` when:
- `HOMESPUN_MOCK_MODE=true`
- The directory exists and contains valid session files

Falls back to hardcoded demo data if no JSONL files are found.

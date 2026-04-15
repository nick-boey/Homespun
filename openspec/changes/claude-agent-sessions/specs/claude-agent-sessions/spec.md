## ADDED Requirements

### Requirement: Session lifecycle with finite state machine

The system SHALL model sessions with states: Starting, RunningHooks, Running, WaitingForInput, WaitingForQuestionAnswer, WaitingForPlanExecution, Stopped, Error.

#### Scenario: Session creation transitions through startup states
- **WHEN** a session is created with an initial message
- **THEN** a container SHALL spin up
- **AND** the session SHALL transition `Starting → RunningHooks → Running`
- **AND** streamed tokens SHALL be broadcast to all clients joined to that session

#### Scenario: Session completes a turn
- **WHEN** the SDK emits `RunFinished`
- **THEN** the session SHALL transition to `WaitingForInput`
- **AND** the final message SHALL be persisted to the JSONL cache

#### Scenario: Non-existent session operations return errors
- **WHEN** an operation targets a non-existent or terminated session
- **THEN** the system SHALL return a deterministic error

### Requirement: AG-UI event streaming via SignalR

The system SHALL stream SDK output to clients as AG-UI events via the `ClaudeCodeHub` SignalR hub.

#### Scenario: Tool call events are broadcast
- **WHEN** Claude invokes a tool (e.g. Bash, Read, Grep)
- **THEN** a `ToolCallStart` event SHALL be broadcast before execution
- **AND** `ToolCallEnd` + tool result blocks SHALL arrive after

#### Scenario: Multiple clients receive the same stream
- **WHEN** multiple browser tabs join the same session
- **THEN** all SHALL receive the same AG-UI event stream

### Requirement: Plan mode with explicit approval

The system SHALL pause at `WaitingForPlanExecution` when `ExitPlanMode` is called, requiring user approval before writes.

#### Scenario: ExitPlanMode pauses session
- **WHEN** a Plan-mode session's agent calls `ExitPlanMode`
- **THEN** the session status SHALL become `WaitingForPlanExecution`
- **AND** the plan content SHALL be available to the client

#### Scenario: Approve plan with context preservation
- **WHEN** the user approves with `keepContext=true`
- **THEN** the session SHALL resume in Build mode with conversation history preserved

#### Scenario: Approve plan without context
- **WHEN** the user approves with `keepContext=false`
- **THEN** the session SHALL resume in Build mode with cleared context

#### Scenario: Reject plan with feedback
- **WHEN** the user rejects with feedback
- **THEN** the feedback SHALL be injected as a user message
- **AND** the session SHALL return to Running in Plan mode

### Requirement: Structured question answering

The system SHALL pause at `WaitingForQuestionAnswer` when `AskUserQuestion` is called.

#### Scenario: Question pauses session
- **WHEN** Claude calls `AskUserQuestion` with options
- **THEN** the session status SHALL become `WaitingForQuestionAnswer`
- **AND** `PendingQuestion` SHALL be populated and broadcast

#### Scenario: Answer resumes session
- **WHEN** the user submits an answer
- **THEN** the session SHALL resume with the answer included in the next SDK turn

### Requirement: Session resume from JSONL cache

The system SHALL discover and resume prior sessions from on-disk JSONL files and metadata sidecars.

#### Scenario: Resumable sessions are listed
- **WHEN** a client queries resumable sessions for an entity
- **THEN** stopped sessions with JSONL caches SHALL be listed with mode, model, and last-activity timestamp

#### Scenario: Resume restores conversation context
- **WHEN** a user resumes a prior session
- **THEN** the session SHALL return to Running with prior conversation available

### Requirement: Mid-session mode and model switching

Users SHALL be able to change mode and model mid-session without restarting the container.

#### Scenario: Switch from Plan to Build
- **WHEN** `SetSessionMode(Build)` is invoked on a running Plan-mode session
- **THEN** subsequent tool calls SHALL have full write access

#### Scenario: Switch model
- **WHEN** `SetSessionModel(B)` is invoked
- **THEN** the next turn SHALL execute against model B

### Requirement: Session control operations

Users SHALL be able to clear context, interrupt a running turn, or stop a session.

#### Scenario: Clear context preserves cached messages
- **WHEN** the user clears context
- **THEN** subsequent turns SHALL start with no conversation history
- **AND** cached messages SHALL remain readable

#### Scenario: Interrupt returns to WaitingForInput
- **WHEN** the user interrupts a running turn
- **THEN** the current turn SHALL be aborted
- **AND** the session SHALL return to `WaitingForInput` without stopping

#### Scenario: Stop tears down container
- **WHEN** the user stops a session
- **THEN** the container SHALL be torn down
- **AND** the session SHALL transition to `Stopped`

### Requirement: Container reconciliation on startup

The system SHALL reconcile orphan containers at server startup.

#### Scenario: Orphan containers are recovered or cleaned up
- **WHEN** the server starts
- **THEN** `ContainerRecoveryHostedService` SHALL discover orphan containers
- **AND** SHALL reattach or clean up their sessions

## ADDED Requirements

### Requirement: Composer accepts user input while the agent is running

The web client's session-detail composer SHALL be interactable (enabled) when the session's server-reported status is `running`, `runningHooks`, or `starting`, provided the transport is connected and the user has joined the session.

#### Scenario: Composer is enabled during `running` status
- **WHEN** the client observes `session.status === 'running'`
- **AND** SignalR is connected and the user has joined
- **THEN** the composer input field SHALL be editable
- **AND** the Send button SHALL be enabled when the draft is non-empty

#### Scenario: Composer is enabled during `runningHooks` status
- **WHEN** the client observes `session.status === 'runningHooks'`
- **AND** SignalR is connected and the user has joined
- **THEN** the composer input field SHALL be editable
- **AND** the Send button SHALL be enabled when the draft is non-empty

#### Scenario: Composer remains disabled when transport is down
- **WHEN** the client observes `!isConnected || !isJoined`
- **THEN** the composer SHALL be disabled regardless of `session.status`

### Requirement: Submitting a mid-run message enqueues on the worker

When the user submits a message while the agent is running, the server SHALL forward the message to the worker without a `session.status` precondition check. The worker SHALL append the message to its persistent `InputQueue` so the Claude Agent SDK consumes it on the next user-turn boundary.

#### Scenario: Server does not gate SendMessage on isRunning
- **WHEN** the client invokes `SendMessage(sessionId, …)` over SignalR while `session.status === 'running'`
- **THEN** the server's hub handler SHALL forward to `SessionService.SendMessageAsync` without returning an error based on run state
- **AND** the worker SHALL receive the message via its existing HTTP send path

#### Scenario: Worker enqueues the message without blocking
- **WHEN** the worker receives a follow-up message while its `runQueryForwarder` loop is actively iterating SDK messages
- **THEN** the worker SHALL push the message to the session's `InputQueue`
- **AND** the push SHALL not block on the current assistant turn completing

### Requirement: Queued user messages appear in the thread immediately

Submitted mid-run messages SHALL render in the message list as soon as the server echoes them as `user.message` events (the existing path), at the natural end of the current message list, preserving FIFO submission order.

#### Scenario: Single mid-run message appears immediately
- **WHEN** the user submits a message while the agent is running
- **AND** the server emits its `user.message` custom event for the submitted message
- **THEN** the message SHALL appear at the end of the rendered message list in the same position as any other user message

#### Scenario: Multiple mid-run messages preserve FIFO order
- **WHEN** the user submits messages M1 and M2 in rapid succession mid-run
- **THEN** M1 SHALL appear in the rendered message list before M2
- **AND** both SHALL be present before the next `RUN_STARTED` arrives

### Requirement: Queued-state visual indication

The client SHALL provide a subtle visual indication for user messages submitted mid-run that have not yet triggered a new assistant run (i.e. the agent has not yet reached them in its input queue).

#### Scenario: Queued indicator appears until next RUN_STARTED
- **WHEN** a user message is rendered and `session.status` is still `running` at the moment of that message's arrival
- **THEN** the rendered message SHALL carry a subtle queued indicator (e.g. a small clock icon or faded styling)

#### Scenario: Queued indicator clears on RUN_STARTED
- **WHEN** the next `RUN_STARTED` envelope arrives after the user message
- **THEN** the queued indicator on that message SHALL be cleared
- **AND** the message SHALL render in its normal user-message style

### Requirement: Pending plan reject-with-feedback flow is not broken by mid-run input

Pre-`questions-plans-as-tools`, when a plan is pending, submitting a free-text message via the composer has special semantics — it's the plan-rejection-with-feedback path (`ApprovePlan(approved=false, feedback=text)`). This change SHALL preserve those semantics while `pendingPlan` is non-null.

#### Scenario: Pending plan still routes composer text to reject-with-feedback
- **WHEN** `pendingPlan` is non-null and the user submits text via the composer
- **THEN** the client SHALL invoke `ApprovePlan(sessionId, false, keepContext, text)` (preserving the current behaviour)
- **AND** the client SHALL NOT invoke `SendMessage` with the same text

#### Scenario: No pending plan → mid-run submit routes to SendMessage
- **WHEN** `pendingPlan` is null and the user submits text mid-run
- **THEN** the client SHALL invoke `SendMessage(sessionId, text, sessionMode)` as normal

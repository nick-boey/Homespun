# Feature Specification: [FEATURE NAME]

**Feature Branch**: `[###-feature-name]`  
**Created**: [DATE]  
**Status**: Draft  
**Input**: User description: "$ARGUMENTS"

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - [Brief Title] (Priority: P1)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently - e.g., "Can be fully tested by [specific action] and delivers [specific value]"]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]
2. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 2 - [Brief Title] (Priority: P2)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

### User Story 3 - [Brief Title] (Priority: P3)

[Describe this user journey in plain language]

**Why this priority**: [Explain the value and why it has this priority level]

**Independent Test**: [Describe how this can be tested independently]

**Acceptance Scenarios**:

1. **Given** [initial state], **When** [action], **Then** [expected outcome]

---

[Add more user stories as needed, each with an assigned priority]

### Edge Cases

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right edge cases.
-->

- What happens when [boundary condition]?
- How does system handle [error scenario]?

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
- **FR-002**: System MUST [specific capability, e.g., "validate email addresses"]  
- **FR-003**: Users MUST be able to [key interaction, e.g., "reset their password"]
- **FR-004**: System MUST [data requirement, e.g., "persist user preferences"]
- **FR-005**: System MUST [behavior, e.g., "log all security events"]

*Example of marking unclear requirements:*

- **FR-006**: System MUST authenticate users via [NEEDS CLARIFICATION: auth method not specified - email/password, SSO, OAuth?]
- **FR-007**: System MUST retain user data for [NEEDS CLARIFICATION: retention period not specified]

### Key Entities *(include if feature involves data)*

- **[Entity 1]**: [What it represents, key attributes without implementation]
- **[Entity 2]**: [What it represents, relationships to other entities]

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: [Measurable metric, e.g., "Users can complete account creation in under 2 minutes"]
- **SC-002**: [Measurable metric, e.g., "System handles 1000 concurrent users without degradation"]
- **SC-003**: [User satisfaction metric, e.g., "90% of users successfully complete primary task on first attempt"]
- **SC-004**: [Business metric, e.g., "Reduce support tickets related to [X] by 50%"]

## Assumptions

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right assumptions based on reasonable defaults
  chosen when the feature description did not specify certain details.
-->

- [Assumption about target users, e.g., "Users have stable internet connectivity"]
- [Assumption about scope boundaries, e.g., "Mobile support is out of scope for v1"]
- [Assumption about data/environment, e.g., "Existing authentication system will be reused"]
- [Dependency on existing system/service, e.g., "Requires access to the existing user profile API"]

## Affected Slices *(mandatory)*

<!--
  Homespun uses Vertical Slice Architecture. Identify EVERY slice this feature
  touches, on both sides of the wire, so the plan and tasks can be scoped
  correctly. If a slice doesn't yet exist, propose its name here.
-->

| Side | Slice path | New / Existing | Why this slice is touched |
|------|------------|----------------|---------------------------|
| Server | `src/Homespun.Server/Features/<Slice>/` | Existing / New | [why] |
| Web    | `src/Homespun.Web/src/features/<slice>/` | Existing / New | [why] |
| Worker | `src/Homespun.Worker/src/<area>/`        | Existing / New / N/A | [why] |
| Shared | `src/Homespun.Shared/`                   | Yes / No | [DTOs or hub contracts added/changed] |

## API & Contract Impact *(mandatory)*

- [ ] **Server API changes?** If yes, list endpoints / DTOs added or changed.
- [ ] **OpenAPI regeneration required?** If yes, the implementation MUST run
      `npm run generate:api:fetch` and commit the regenerated client under
      `src/Homespun.Web/src/api/generated/` (never hand-edited).
- [ ] **Shared DTO / hub interface changes?** If yes, they MUST originate in
      `src/Homespun.Shared` — no duplicated hand-written contracts.
- [ ] **Breaking change for existing clients?** If yes, capture the migration
      story below.

[Notes / details / migration story]

## Realtime Impact *(SignalR / AG-UI)*

- [ ] **SignalR hub events added or changed?** Hub: `[name]`, events: `[list]`.
- [ ] **AG-UI events broadcast?** Describe the event payload and trigger.
- [ ] **Frontend subscriptions?** Note the slice that consumes the events and
      any Zustand store updates triggered.
- [ ] N/A — feature has no realtime surface.

## Persistence Impact *(SQLite + Fleece)*

- [ ] **SQLite schema changes?** Tables / columns added / changed; migration
      strategy.
- [ ] **Fleece JSONL changes?** New issue types, status meanings, or
      `.fleece/` schema fields.
- [ ] **External state (GitHub, Loki, Komodo)?** Note the system and the
      access pattern.
- [ ] N/A — feature has no persistence impact.

## Worker Impact *(Claude Agent SDK)*

- [ ] **New agent prompt / tool / permission?** Describe.
- [ ] **New worker route in `src/Homespun.Worker/src/routes/`?** Describe.
- [ ] **Changes to A2A / AG-UI message flow?** Describe.
- [ ] N/A — feature does not touch the worker.

## Operational Impact

- [ ] **New env var?** Add to `.env.example` and document in the plan.
- [ ] **New container or compose change?** Update `docker-compose.yml`,
      `Dockerfile`, `Dockerfile.base` as required.
- [ ] **Fleece.Core version bump?** If so, the matching `Fleece.Cli` bump in
      `Dockerfile.base` is part of this feature (Constitution §IX).
- [ ] **Bicep / infra change?** Identify modules under `infra/`.
- [ ] **Architecture diagram update?** If the topology changes, update the
      LikeC4 model under `docs/architecture/`.
- [ ] N/A.

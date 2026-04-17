# Skills Catalogue

## Purpose

Defines how Homespun discovers, surfaces, and dispatches filesystem-based
Claude Code skills. Replaces the DB-backed prompt catalogue. Skills are
`.claude/skills/<name>/SKILL.md` files under a project clone; Homespun
reads each skill's frontmatter to categorise it and renders the skill body
(with composed args) as the dispatch session's initial message.

## Requirements

### Requirement: Skill discovery service

The system SHALL scan `.claude/skills/**/SKILL.md` in the active clone and return categorised skill descriptors.

#### Scenario: OpenSpec skills are identified by hard-coded list
- **WHEN** the service scans skills
- **THEN** the 8 known OpenSpec skill names SHALL be matched by name (`openspec-explore`, `openspec-new-change`, `openspec-propose`, `openspec-continue-change`, `openspec-apply-change`, `openspec-verify-change`, `openspec-sync-specs`, `openspec-archive-change`)
- **AND** each SHALL be returned with its SKILL.md body and description from frontmatter

#### Scenario: Homespun prompt skills are identified by frontmatter
- **WHEN** a SKILL.md contains `homespun: true` in its frontmatter
- **THEN** the service SHALL return it as a Homespun prompt skill
- **AND** SHALL parse `homespun-mode` (plan|build) and `homespun-args` from frontmatter

#### Scenario: All other skills are returned as general skills
- **WHEN** a SKILL.md is neither OpenSpec nor Homespun-flagged
- **THEN** the service SHALL return it with name and description only
- **AND** it SHALL be surfaced in the session chat window

#### Scenario: Missing or malformed SKILL.md is skipped
- **WHEN** a skill directory exists but SKILL.md is absent or has no frontmatter
- **THEN** the service SHALL skip it without error

### Requirement: Skill-based agent dispatch

The system SHALL dispatch agents by reading a skill's SKILL.md body, appending composed args, and sending the result as the session's initial message.

#### Scenario: Dispatch with a Homespun prompt skill
- **WHEN** the user selects a Homespun skill (e.g., `fix-bug`) and provides args (e.g., issue-id `abc123`)
- **THEN** the system SHALL read the SKILL.md body
- **AND** SHALL append the composed args to the body
- **AND** SHALL send the combined text as `session.initialMessage`

#### Scenario: Dispatch with an OpenSpec skill
- **WHEN** the user selects an OpenSpec skill (e.g., `openspec-apply-change`) with a change name
- **THEN** the system SHALL read the SKILL.md body
- **AND** SHALL append the change name as args
- **AND** SHALL inject schema override into the system prompt if the project uses a non-default schema

#### Scenario: Dispatch with no skill (Task Agent)
- **WHEN** the user dispatches from the Task Agent tab without selecting a skill
- **THEN** the system SHALL start a session with no initial message, or with the user's custom free-text message if provided
- **AND** SHALL NOT require a skill selection

### Requirement: Args schema with kind enum

The system SHALL use the `homespun-args` frontmatter to determine what UI input controls to render for each skill.

#### Scenario: Issue kind renders issue picker
- **WHEN** a skill declares `kind: issue` for an arg
- **THEN** the UI SHALL render a Fleece issue picker for that arg

#### Scenario: Change kind renders change picker
- **WHEN** a skill declares `kind: change` for an arg
- **THEN** the UI SHALL render a change picker populated from linked changes

#### Scenario: Phase-list kind renders phase multi-select
- **WHEN** a skill declares `kind: phase-list` for an arg
- **THEN** the UI SHALL render a multi-select of phases from the linked change's tasks.md

#### Scenario: Free-text kind renders text input
- **WHEN** a skill declares `kind: free-text` for an arg
- **THEN** the UI SHALL render a text input field

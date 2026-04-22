## ADDED Requirements

### Requirement: Prompt list with merge logic

The system SHALL return prompts merged from globals and project-scoped, with overrides replacing globals by name.

#### Scenario: Available-for-project returns merged list
- **WHEN** `GET /api/agent-prompts/available-for-project/{projectId}` is called
- **THEN** the response SHALL contain globals unioned with project prompts
- **AND** project prompts matching a global by `Name` SHALL replace the global with `IsOverride = true`

#### Scenario: SessionType prompts excluded from standard lists
- **WHEN** `GET /api/agent-prompts` or `GET /api/agent-prompts/project/{id}` is called
- **THEN** prompts with non-null `SessionType` SHALL be filtered out

#### Scenario: IssueAgent prompts returned via dedicated endpoints
- **WHEN** `GET /api/agent-prompts/issue-agent-prompts` is called
- **THEN** only `Category.IssueAgent` prompts SHALL be returned

### Requirement: Prompt CRUD with duplicate detection

The system SHALL support create, update, and delete of prompts with `(Name, ProjectId)` uniqueness enforcement.

#### Scenario: Create succeeds for unique name
- **WHEN** `POST /api/agent-prompts` is called with a unique `(Name, ProjectId)`
- **THEN** the prompt SHALL be created and `201 Created` returned

#### Scenario: Create rejects duplicate
- **WHEN** `(Name, ProjectId)` already exists
- **THEN** the response SHALL be `409 Conflict`

#### Scenario: Update mutates allowed fields only
- **WHEN** `PUT /api/agent-prompts/by-name/{name}` is called
- **THEN** only `InitialMessage`, `Mode`, and `UpdatedAt` SHALL be mutated

#### Scenario: Delete removes prompt
- **WHEN** `DELETE /api/agent-prompts/by-name/{name}` is called for an existing prompt
- **THEN** the prompt SHALL be removed

### Requirement: Project override mechanism

The system SHALL support creating and removing project-specific overrides of global prompts.

#### Scenario: Create override from global
- **WHEN** `POST /api/agent-prompts/create-override` is called with a valid global name
- **THEN** a project prompt SHALL be created with `Mode`, `Category`, `SessionType` copied from the global
- **AND** `InitialMessage` SHALL be seeded from the global when not provided

#### Scenario: Override rejected when already exists
- **WHEN** an override already exists for `(globalPromptName, projectId)`
- **THEN** the response SHALL be `409 Conflict`

#### Scenario: Remove override restores global
- **WHEN** `DELETE /api/agent-prompts/by-name/{name}/override` is called
- **THEN** the project override SHALL be removed
- **AND** the global prompt SHALL remain untouched

### Requirement: Template rendering

The system SHALL render prompt templates with placeholder substitution and conditional blocks.

#### Scenario: Simple placeholder substitution
- **WHEN** a template contains `{{title}}` and the context has `Title = "Fix bug"`
- **THEN** `{{title}}` SHALL be replaced with `Fix bug` (case-insensitive)

#### Scenario: Conditional block removal
- **WHEN** a template contains `{{#if description}}...{{/if}}` and description is empty
- **THEN** the entire block SHALL be removed

#### Scenario: Conditional block preservation
- **WHEN** a template contains `{{#if description}}...{{/if}}` and description has content
- **THEN** the block markers SHALL be removed and the inner content preserved

### Requirement: Defaults management

The system SHALL support idempotent seed creation, destructive restore, and project-scoped bulk delete.

#### Scenario: Ensure-defaults is idempotent
- **WHEN** `POST /api/agent-prompts/ensure-defaults` is called
- **THEN** missing seed prompts SHALL be created
- **AND** existing prompts SHALL NOT be overwritten

#### Scenario: Restore-defaults overwrites seeds
- **WHEN** `POST /api/agent-prompts/restore-defaults` is called
- **THEN** all 13 seed globals SHALL be overwritten from `default-prompts.json`
- **AND** non-seed globals SHALL be preserved

#### Scenario: Startup seeding runs automatically
- **WHEN** the server starts
- **THEN** `DefaultPromptsInitializationService` SHALL run `EnsureDefaultsAsync` exactly once

#### Scenario: Delete-all-project scopes correctly
- **WHEN** `DELETE /api/agent-prompts/project/{projectId}/all` is called
- **THEN** all project prompts for that project SHALL be removed
- **AND** globals and other projects' prompts SHALL be untouched

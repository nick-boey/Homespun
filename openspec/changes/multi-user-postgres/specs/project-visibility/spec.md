## ADDED Requirements

### Requirement: Project ownership

Every project SHALL have an `owner_user_id` referencing the `User` that created it. The owner SHALL always have read and write access to the project regardless of visibility.

#### Scenario: Owner is set on project creation
- **WHEN** a user creates a new project
- **THEN** the system SHALL set `owner_user_id` to the creating user's id

#### Scenario: Owner can always access their project
- **WHEN** the owner requests a project they created
- **THEN** the system SHALL return the project regardless of its `visibility` setting

### Requirement: Project visibility

Each project SHALL have a `visibility` property with allowed values `private` or `public`. Default SHALL be `private`.

#### Scenario: Default visibility is private
- **WHEN** a user creates a project without specifying visibility
- **THEN** the project SHALL be created with `visibility = 'private'`

#### Scenario: Private project invisible to non-owners
- **WHEN** user B requests a project owned by user A with `visibility = 'private'`
- **THEN** the system SHALL respond with HTTP 404 (treat as not-found to avoid existence leaks)

#### Scenario: Public project visible to all users on the instance
- **WHEN** any authenticated user lists projects
- **THEN** the response SHALL include projects where `visibility = 'public'` OR `owner_user_id` equals the requesting user

#### Scenario: Owner can change visibility
- **WHEN** the project owner updates `visibility` from `'private'` to `'public'` (or vice versa)
- **THEN** the system SHALL persist the new value

#### Scenario: Non-owner cannot change visibility
- **WHEN** a non-owner non-admin user attempts to change a project's visibility
- **THEN** the system SHALL respond with HTTP 403

### Requirement: Project-scoped resource access

Resources owned by a project (pull requests, agent prompts, project-scoped views) SHALL inherit the project's visibility rules.

#### Scenario: Pull requests for a public project are visible to all users
- **WHEN** an authenticated user lists pull requests for a `public` project
- **THEN** the system SHALL return all pull requests for that project

#### Scenario: Pull requests for a private project are visible only to the owner
- **WHEN** a non-owner authenticated user lists pull requests for a `private` project
- **THEN** the system SHALL respond with HTTP 404

#### Scenario: Agent prompts scoped to a public project are visible to all users
- **WHEN** an authenticated user lists agent prompts for a `public` project
- **THEN** the system SHALL return the prompts

### Requirement: Secrets are never subject to project visibility

Per-user secrets SHALL remain strictly user-scoped regardless of any project's visibility setting.

#### Scenario: User's secrets are not visible to other users via a shared project
- **WHEN** user A sets a secret usable in a `public` project
- **AND** user B queries secrets for that project
- **THEN** the system SHALL return only user B's own secrets

### Requirement: Admin override

Users with `is_admin = true` SHALL have read access to all projects regardless of visibility, but admin status SHALL NOT grant write access to projects they do not own.

#### Scenario: Admin can read a private project
- **WHEN** an admin requests a private project owned by another user
- **THEN** the system SHALL return the project

#### Scenario: Admin cannot edit another user's project without being owner
- **WHEN** an admin who is not the owner attempts to update project fields
- **THEN** the system SHALL respond with HTTP 403

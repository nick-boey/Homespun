## ADDED Requirements

### Requirement: User entity

The system SHALL represent each authenticated principal as a `User` record with `id`, `external_id`, `email`, `display_name`, `is_admin`, `created_at`, and `last_login_at` columns.

#### Scenario: New user created on first OIDC login
- **WHEN** an OIDC-authenticated request arrives with a `sub` claim that has no matching `external_id` in the `users` table
- **THEN** the system SHALL create a new `User` row with `external_id = sub`, `email` and `display_name` populated from token claims, `is_admin = false` (unless seeded — see admin seeding), and `created_at = now()`
- **AND** the request SHALL be processed under the new user's identity

#### Scenario: Existing user resolved on subsequent login
- **WHEN** an OIDC-authenticated request arrives with a `sub` claim matching an existing `external_id`
- **THEN** the system SHALL load that `User`, update `email`, `display_name`, and `last_login_at`, and process the request under that user

### Requirement: OIDC authentication on ACA builds

The system SHALL authenticate API requests on ACA deployments using Microsoft.Identity.Web against a configured Entra ID tenant.

#### Scenario: Unauthenticated API request is rejected
- **WHEN** a request to a `[Authorize]`-decorated endpoint arrives without a valid bearer token
- **THEN** the system SHALL respond with HTTP 401

#### Scenario: Valid Entra ID token is accepted
- **WHEN** a request arrives with a valid JWT signed by the configured Entra ID tenant
- **THEN** the system SHALL populate `HttpContext.User` with claims from the token and process the request

### Requirement: VM-mode no-auth development shim

The system SHALL support a no-authentication mode for VM/development deployments in which a single local user record is resolved for every request.

#### Scenario: No-auth mode returns the seeded local user
- **WHEN** the server is started with `Authentication:Mode=None`
- **THEN** the system SHALL ensure a `User` row exists with `external_id='local'`, `is_admin=true`, `email='local@homespun.localhost'`
- **AND** `HttpContext.User` for every request SHALL resolve to that user

#### Scenario: No-auth mode is refused in production without explicit opt-in
- **WHEN** the server is started with `ASPNETCORE_ENVIRONMENT=Production` AND `Authentication:Mode=None`
- **AND** `Authentication:AllowNoAuthInProduction` is not `true`
- **THEN** the system SHALL fail to start and log a fatal configuration error

### Requirement: Admin seeding via configuration

The system SHALL allow declarative seeding of admin users via `Admin:Emails` or `Admin:ExternalIds` configuration keys.

#### Scenario: Seeded email becomes admin on first login
- **WHEN** a new user logs in and their `email` claim matches an entry in `Admin:Emails`
- **THEN** the `User` row SHALL be created with `is_admin=true`

#### Scenario: Existing user is promoted if seeded after creation
- **WHEN** an existing user's `email` or `external_id` matches an admin seed entry
- **AND** the user logs in
- **THEN** the system SHALL set `is_admin=true` on that user if not already set

#### Scenario: Removing a seed entry does not demote
- **WHEN** an admin user's entry is removed from the config list
- **AND** that user logs in
- **THEN** the system SHALL NOT change `is_admin`; demotion requires explicit UI action by another admin

#### Scenario: Empty admin list in production fails startup
- **WHEN** the server is started in production with an empty `Admin:Emails` AND empty `Admin:ExternalIds`
- **AND** `Admin:AllowUnseededStart` is not `true`
- **THEN** the system SHALL fail to start with a fatal configuration error

### Requirement: Admin-only user management API

The system SHALL expose endpoints for admins to list users, promote users to admin, and demote admins.

#### Scenario: Non-admin cannot list users
- **WHEN** a non-admin authenticated user calls `GET /api/users`
- **THEN** the system SHALL respond with HTTP 403

#### Scenario: Admin demotes another admin
- **WHEN** an admin calls `DELETE /api/users/{id}/admin` targeting another admin user
- **THEN** the system SHALL set `is_admin=false` on the target user

#### Scenario: Admin cannot demote the last admin
- **WHEN** an admin calls `DELETE /api/users/{id}/admin` AND only one admin remains (the target)
- **THEN** the system SHALL respond with HTTP 409 and leave `is_admin` unchanged

### Requirement: Current-user accessor

The system SHALL provide an `ICurrentUserAccessor` service that returns the current request's `User` record.

#### Scenario: Accessor resolves to authenticated user
- **WHEN** a service calls `ICurrentUserAccessor.GetCurrentUserAsync()` during an authenticated request
- **THEN** the method SHALL return the `User` row matching `HttpContext.User`'s subject claim

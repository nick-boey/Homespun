# Blazor WASM Split - Progress Tracker

Parent issue: `oU1zLd` - Split into Blazor WASM and ASP.NET server

## Completed

### 8yebWn (aaa) - Create Homespun.Shared project with shared DTOs and contracts
- Created `src/Homespun.Shared/` class library targeting net10.0
- Moved ~90 shared types (models, DTOs, enums, hub contracts) from monolith to Shared project
- Created `GlobalUsings.cs` with type aliases for backward compatibility
- Added ProjectReference from Homespun and test projects to Homespun.Shared
- Added SignalR hub contracts (IClaudeCodeHubClient, IClaudeCodeHub, INotificationHubClient, INotificationHub)
- Added API route constants (ApiRoutes.cs) and request/response DTOs
- Build: 0 errors | Unit tests: 1304 passed, 5 pre-existing failures | API tests: 36 passed

## In Progress

None

## Remaining

- JvWPpe (bbb) - Create Homespun.Server ASP.NET minimal API project
- kwvBrH (ccc) - Create Homespun.Client Blazor WASM project
- m1zqCB (ddd) - Migrate backend services from monolith to Homespun.Server
- kH1MPa (eee) - Create client-side HTTP service layer for Blazor WASM
- XAdrC8 (fff) - Migrate Blazor components to WASM client and rewire to HTTP services
- W3HDnD (ggg) - Implement SignalR client integration for Blazor WASM
- NbRDKw (hhh) - Update Docker build and CI/CD for new multi-project architecture
- XfPAFE (iii) - Update test projects for new architecture
- iP8Q7C (jjj) - Remove old monolith project and final cleanup

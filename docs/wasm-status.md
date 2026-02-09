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

### JvWPpe (bbb) - Create Homespun.Server ASP.NET minimal API project
- Created `src/Homespun.Server/` as `Microsoft.NET.Sdk.Web` project targeting net10.0
- Pure API server Program.cs (no Razor components) with CORS, SignalR hubs, Swagger, health checks
- References Homespun (for Features/), Homespun.Shared, and Homespun.ClaudeAgentSdk
- Added `Microsoft.AspNetCore.Components.WebAssembly.Server` for hosting WASM client files
- Configured `UseBlazorFrameworkFiles()` and `MapFallbackToFile("index.html")` for WASM hosting
- Copied appsettings.json, appsettings.Mock.json, launchSettings.json, start.sh
- Build: 0 errors | Unit tests: 1304 passed, 5 pre-existing failures | API tests: 36 passed

### kwvBrH (ccc) - Create Homespun.Client Blazor WASM project
- Created `src/Homespun.Client/` as `Microsoft.NET.Sdk.BlazorWebAssembly` project targeting net10.0
- WASM entry point with Program.cs, App.razor router, MainLayout, NavMenu
- Copied wwwroot assets (index.html, app.css, tailwind.css, favicon.png)
- Tailwind CSS build pipeline (package.json, tailwind.config.js)
- References Homespun.Shared only (no direct server-side dependencies)
- Server-client WASM hosting deferred to avoid static asset conflicts during transition
- Build: 0 errors | Unit tests: 1304 passed, 5 pre-existing failures | API tests: 36 passed

### m1zqCB (ddd) - Migrate backend services from monolith to Homespun.Server
- Moved entire Features/ directory from monolith to Homespun.Server
- Kept namespaces as `Homespun.Features.*` for compatibility
- Blazor .razor component files kept in monolith (UI stays in monolith during transition)
- Inverted dependency: monolith now references Homespun.Server (instead of vice versa)
- Removed `UseAntiforgery()` from Server Program.cs (not needed for pure API server)
- Updated API test project to reference Homespun.Server directly
- Build: 0 errors | Unit tests: 1304 passed, 5 pre-existing failures | API tests: 36 passed

## In Progress

None

## Remaining

- kH1MPa (eee) - Create client-side HTTP service layer for Blazor WASM
- XAdrC8 (fff) - Migrate Blazor components to WASM client and rewire to HTTP services
- W3HDnD (ggg) - Implement SignalR client integration for Blazor WASM
- NbRDKw (hhh) - Update Docker build and CI/CD for new multi-project architecture
- XfPAFE (iii) - Update test projects for new architecture
- iP8Q7C (jjj) - Remove old monolith project and final cleanup

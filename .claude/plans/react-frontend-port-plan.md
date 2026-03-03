# React Frontend Port Implementation Plan

## Overview

This plan outlines the migration of the Homespun frontend from Blazor WebAssembly to a modern TypeScript stack:
- **Language**: TypeScript
- **Framework**: React + Vite
- **UI Components**: shadcn/ui + Tailwind CSS
- **Routing**: TanStack Router
- **Data Fetching**: TanStack Query
- **State Management**: Zustand
- **API Client Generation**: OpenAPI-TS (hey-api/openapi-ts)

## Current Blazor Frontend Analysis

### Pages (10+)
- Projects.razor - Project list
- ProjectCreate.razor - Create project
- ProjectDetail.razor - Main project dashboard (6 tabs: Issues, Branches, Prompts, Secrets, Settings, Pull Requests)
- ProjectEdit.razor - Edit project
- IssueEdit.razor - Edit issue
- Session.razor - Chat interface
- ArchivedSession.razor - Past sessions
- SessionManagement.razor - Session list
- AgentPrompts.razor - Prompt management
- Settings.razor - Global settings

### Major Features
1. **Projects** - CRUD operations, multi-tab dashboard
2. **Issues** - Hierarchical task management, tree visualization, inline editing
3. **Chat/Sessions** - Real-time agent interaction, message streaming, tool result rendering
4. **Agents** - Agent spawning, status monitoring, prompt management
5. **Branches/Clones** - Git worktree management
6. **Pull Requests** - PR status, integration with issues
7. **Notifications** - Real-time updates via SignalR

### API Services (19)
All current HTTP services need React Query equivalents with proper caching.

### Real-time (SignalR)
- Notifications Hub - Issue changes, system notifications
- Claude Code Hub - Session streaming, agent status

---

## Phase 1: Foundation & Infrastructure

### 1.1 Project Setup
- Initialize Vite + React + TypeScript project at `src/Homespun.Web`
- Configure Tailwind CSS with custom design tokens
- Set up shadcn/ui component library
- Configure TanStack Router with file-based routing
- Configure TanStack Query for data fetching
- Set up Zustand stores structure
- Configure ESLint, Prettier, TypeScript strict mode

### 1.2 OpenAPI Integration
- Generate OpenAPI spec from ASP.NET backend
- Configure hey-api/openapi-ts for client generation
- Create typed API client with interceptors
- Set up authentication handling
- Generate TypeScript types from API

### 1.3 Core Infrastructure
- Create base layout component (sidebar, header, main content)
- Implement routing structure matching Blazor routes
- Set up error boundary and 404 handling
- Create shared UI primitives (Button, Input, Card, etc.)
- Implement SignalR client wrapper for React

---

## Phase 2: Projects & Navigation

### 2.1 Projects List
- Project list page with cards/table view
- Create project modal/page
- Delete project with confirmation
- Project search/filtering

### 2.2 Project Detail Shell
- Tab-based layout (Issues, Branches, Prompts, Secrets, Settings, PRs)
- Project header with breadcrumbs
- Project settings editing
- Route structure for sub-pages

---

## Phase 3: Issues Management (Core Feature)

### 3.1 Issues List & Tree View
- Issues tab with table/list view
- Tree visualization component (TaskGraphView equivalent)
- Issue status badges and indicators
- Filtering by status, type, priority
- Real-time updates via SignalR

### 3.2 Issue Detail Panel
- Side panel for issue details
- Inline editing capabilities
- Status/type/priority dropdowns
- Parent-child relationship management
- Branch/worktree association

### 3.3 Issue Creation & Editing
- Quick create bar (bottom of page)
- Full issue edit page
- Markdown editor for descriptions
- Parent/child linking UI
- Keyboard shortcuts (Enter, Shift+Enter)

### 3.4 Issue History
- Undo/redo functionality
- History state management

---

## Phase 4: Agent Sessions & Chat

### 4.1 Chat Interface
- Session page with message list
- Chat input component
- Message rendering (user, assistant, system)
- Auto-scroll and message streaming

### 4.2 Tool Result Rendering
- Generic tool result component
- Specialized renderers:
  - BashToolResultDisplay
  - ReadToolResultDisplay
  - WriteToolResultDisplay
  - GrepToolResultDisplay
  - GlobToolResultDisplay
  - AgentToolResultDisplay
  - WebToolResultDisplay
- Thinking block display
- Collapsible tool outputs

### 4.3 Session Management
- Sessions list page
- Session history browser
- Archived session viewer
- Session resume functionality

### 4.4 Agent Controls
- Agent launcher component
- Agent status indicators
- Model selector dropdown
- Permission settings dropdown
- Real-time agent status via SignalR

---

## Phase 5: Branches & Pull Requests

### 5.1 Branch/Worktree Management
- Branches tab in project detail
- Clone management panel
- Worktree list with status
- Create/delete worktree actions
- Pull recent changes action

### 5.2 Pull Request Integration
- PR list in project dashboard
- PR status badges (draft, approved, merged)
- Link PRs to issues
- Open/Merged PR detail panels
- PR actions (create from issue, etc.)

---

## Phase 6: Settings & Configuration

### 6.1 Global Settings
- Settings page accessible from sidebar
- GitHub authentication status
- Git credential configuration
- API token management (masked display)

### 6.2 Project Settings
- Project-level configuration
- System prompts management (Prompts tab)
- Secrets/environment variables (Secrets tab)

---

## Phase 7: Real-time & Polish

### 7.1 SignalR Integration
- Notification hub connection
- Claude Code hub connection
- Automatic reconnection handling
- Connection status indicator

### 7.2 Notifications
- Toast notifications
- Issue change notifications
- Agent completion notifications

### 7.3 UI Polish
- Loading states and skeletons
- Error states and retry logic
- Responsive design (mobile support)
- Keyboard navigation
- Accessibility improvements

---

## Phase 8: Testing & Migration

### 8.1 Testing
- Unit tests for components (Vitest)
- Integration tests for API hooks
- E2E tests (Playwright) matching existing test coverage

### 8.2 Migration
- Feature parity verification
- Performance benchmarking
- Documentation updates
- Deprecation plan for Blazor client

---

## Technical Decisions

### State Management Strategy
- **Server State**: TanStack Query for all API data
- **Client State**: Zustand for UI state (selected items, panel visibility, etc.)
- **URL State**: TanStack Router for route params and search params

### Component Architecture
- Feature-based folder structure (matching Blazor vertical slices)
- Shared components in `/components/ui` (shadcn)
- Custom components in `/components/shared`
- Feature components in `/features/{feature}/components`

### API Client Pattern
```
/api
  /client.ts        - Generated OpenAPI client
  /hooks/           - TanStack Query hooks per feature
    /useProjects.ts
    /useIssues.ts
    /useSessions.ts
```

### Routing Structure
```
/                           - Projects list
/projects/new               - Create project
/projects/:id               - Project detail (Issues tab)
/projects/:id/issues        - Issues tab (alias)
/projects/:id/branches      - Branches tab
/projects/:id/prompts       - Prompts tab
/projects/:id/secrets       - Secrets tab
/projects/:id/settings      - Settings tab
/projects/:id/issues/:issueId/edit - Edit issue
/sessions                   - Sessions list
/sessions/:id               - Session chat
/sessions/:id/archived      - Archived session
/settings                   - Global settings
```

---

## Risk Mitigation

1. **SignalR Compatibility**: Test SignalR client early in Phase 1
2. **OpenAPI Generation**: Verify ASP.NET OpenAPI output matches expectations
3. **Real-time Performance**: Benchmark message streaming against Blazor
4. **Feature Parity**: Maintain checklist of all Blazor features
5. **Parallel Development**: Keep Blazor running during transition

---

## Success Criteria

- [ ] All existing Blazor functionality replicated
- [ ] Improved initial load performance
- [ ] Consistent UI with shadcn/ui components
- [ ] Full TypeScript type safety
- [ ] Comprehensive test coverage
- [ ] No regressions in E2E tests

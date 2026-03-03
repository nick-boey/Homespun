# Fleece Issue Hierarchy for React Frontend Port

**Parent Issue**: `eGamgb` - Port frontend to React+Vite+Tailwind+TanStack Router+TanStack Query+Zustand

The following issues should be created as children of `eGamgb` in **Series** execution mode (they depend on each other sequentially).

---

## Phase 1: Foundation & Infrastructure

### Issue 1.1: Project Setup and Toolchain
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Description**:
Initialize the new React frontend project at `src/Homespun.Web` with the following stack:

1. Create Vite + React + TypeScript project
2. Configure Tailwind CSS with the project's design tokens
3. Install and configure shadcn/ui component library
4. Set up TanStack Router with file-based routing structure
5. Configure TanStack Query with default options
6. Set up Zustand store scaffolding
7. Configure ESLint, Prettier, and TypeScript strict mode
8. Add `dev` and `build` scripts to package.json
9. Configure Vite proxy to forward `/api` requests to ASP.NET backend
10. Update docker-compose to include the new frontend service

**Acceptance Criteria**:
- `npm run dev` starts the Vite dev server
- `npm run build` produces a production build
- TypeScript compilation has no errors
- shadcn/ui components can be imported and rendered
- API requests to `/api/*` are proxied to the backend

---

### Issue 1.2: OpenAPI Client Generation
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Parent**: 1.1
**Description**:
Set up OpenAPI-based type generation using hey-api/openapi-ts:

1. Configure ASP.NET backend to export OpenAPI JSON at `/swagger/v1/swagger.json`
2. Install @hey-api/openapi-ts as a dev dependency
3. Create openapi-ts.config.ts configuration file
4. Add `generate:api` script to fetch OpenAPI spec and generate client
5. Generate TypeScript types for all API endpoints
6. Create base API client with:
   - Axios or fetch wrapper
   - Error handling interceptors
   - Authentication header injection
7. Verify all 19 API services have generated types

**Acceptance Criteria**:
- Running `npm run generate:api` generates TypeScript types
- All API endpoints have typed request/response interfaces
- API client includes proper error handling
- Types match the C# DTOs in Homespun.Shared

---

### Issue 1.3: Core Layout and Routing
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Parent**: 1.2
**Description**:
Create the base application layout and routing structure:

1. Create `RootLayout` component with:
   - Sidebar (projects navigation, settings link)
   - Header (breadcrumbs, agent status indicator)
   - Main content area
2. Set up TanStack Router routes matching Blazor routes:
   - `/` - Projects list
   - `/projects/new` - Create project
   - `/projects/$projectId` - Project detail
   - `/projects/$projectId/issues` - Issues tab
   - `/projects/$projectId/branches` - Branches tab
   - `/projects/$projectId/prompts` - Prompts tab
   - `/projects/$projectId/secrets` - Secrets tab
   - `/projects/$projectId/settings` - Settings tab
   - `/projects/$projectId/issues/$issueId/edit` - Edit issue
   - `/sessions` - Sessions list
   - `/sessions/$sessionId` - Session chat
   - `/settings` - Global settings
3. Implement error boundary component
4. Create 404 Not Found page
5. Set up breadcrumb context/hook

**Acceptance Criteria**:
- All routes render placeholder components
- Sidebar navigation works correctly
- Breadcrumbs update based on current route
- Error boundary catches and displays errors gracefully

---

### Issue 1.4: SignalR Client Setup
**Type**: Task
**Priority**: 2
**Execution Mode**: Parallel (with next phase)
**Parent**: 1.3
**Description**:
Create React-compatible SignalR client wrapper:

1. Install @microsoft/signalr package
2. Create `useSignalR` hook for connection management
3. Implement automatic reconnection with exponential backoff
4. Create typed event handlers for:
   - Notifications Hub (`/hubs/notifications`)
     - `IssuesChanged` event
   - Claude Code Hub (`/hubs/claudecode`)
     - `ReceiveMessage` event
     - `SessionStatusChanged` event
     - `AgentStatusChanged` event
5. Create `SignalRProvider` context component
6. Add connection status indicator component
7. Handle connection lifecycle (connect on mount, disconnect on unmount)

**Acceptance Criteria**:
- SignalR connects successfully to both hubs
- Events are received and can trigger React state updates
- Reconnection works after network interruption
- Connection status is visible in the UI

---

## Phase 2: Projects & Navigation

### Issue 2.1: Projects List Page
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Description**:
Implement the projects list page:

1. Create `useProjects` TanStack Query hook
2. Build projects list with cards showing:
   - Project name
   - Repository path
   - Active issues count
   - Recent activity
3. Add "Create Project" button
4. Implement project deletion with confirmation dialog
5. Add loading skeleton states
6. Handle empty state (no projects)

**Acceptance Criteria**:
- Projects are fetched and displayed
- Create button navigates to create page
- Delete removes project after confirmation
- Loading states are shown during fetch

---

### Issue 2.2: Project Creation
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Parent**: 2.1
**Description**:
Implement project creation flow:

1. Create project creation page/modal
2. Form fields:
   - Project name (required)
   - Repository URL or local path
   - Initial branch
3. Validation with error messages
4. Create project via API
5. Navigate to new project on success
6. Handle creation errors

**Acceptance Criteria**:
- Form validates required fields
- Project is created via API
- User is redirected to new project
- Errors are displayed clearly

---

### Issue 2.3: Project Detail Shell
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Parent**: 2.2
**Description**:
Create the project detail page shell with tabbed navigation:

1. Create `ProjectLayout` component with tabs:
   - Issues (default)
   - Branches
   - Prompts
   - Secrets
   - Settings
2. Implement tab routing (URL-based)
3. Create project header with:
   - Project name
   - Breadcrumb navigation
   - Quick actions
4. Set up `useProject` hook for fetching project details
5. Create placeholder content for each tab

**Acceptance Criteria**:
- Tabs switch content based on URL
- Project name displays in header
- Breadcrumbs show current location
- Tab state persists in URL

---

## Phase 3: Issues Management

### Issue 3.1: Issues List View
**Type**: Feature
**Priority**: 1
**Execution Mode**: Series
**Description**:
Implement the issues tab with list/table view:

1. Create `useIssues` hook with filtering support
2. Build issues table with columns:
   - ID
   - Title
   - Type (badge)
   - Status (badge)
   - Priority
   - Assigned PR
3. Add filter controls:
   - Status filter (Open, InProgress, Complete, etc.)
   - Type filter (Task, Feature, Bug, BreakingChange)
   - Search by title
4. Implement row click to open detail panel
5. Add real-time updates via SignalR
6. Show loading and empty states

**Acceptance Criteria**:
- Issues display in a sortable table
- Filters update the displayed issues
- Clicking a row shows the detail panel
- New/updated issues appear without refresh

---

### Issue 3.2: Task Graph Tree View
**Type**: Feature
**Priority**: 2
**Execution Mode**: Parallel (with 3.1)
**Description**:
Implement the hierarchical tree visualization (TaskGraphView equivalent):

1. Research React tree visualization libraries (react-d3-tree, react-flow, etc.)
2. Create tree layout algorithm for parent-child relationships
3. Build tree node component showing:
   - Issue ID and title
   - Status indicator
   - Type icon
4. Implement expand/collapse for parent nodes
5. Add drag-and-drop for reordering (optional)
6. Handle execution mode display (Series vs Parallel)
7. Toggle between list and tree view

**Acceptance Criteria**:
- Issues display in hierarchical tree
- Parent-child relationships are visualized
- Nodes can be expanded/collapsed
- Clicking a node opens detail panel

---

### Issue 3.3: Issue Detail Panel
**Type**: Feature
**Priority**: 1
**Execution Mode**: Series
**Parent**: 3.1
**Description**:
Create the side panel for viewing/editing issue details:

1. Create slide-out panel component
2. Display issue details:
   - Title (editable inline)
   - Description (Markdown rendered, editable)
   - Status dropdown
   - Type dropdown
   - Priority selector
   - Parent issues list
   - Linked PR
   - Working branch
3. Add "Edit" button to open full edit page
4. Implement inline save for quick edits
5. Show related sessions and agent status
6. Add action buttons:
   - Start agent
   - Create PR
   - Delete issue

**Acceptance Criteria**:
- Panel slides in when issue selected
- All issue fields are displayed
- Inline edits save immediately
- Actions work correctly

---

### Issue 3.4: Quick Issue Creation
**Type**: Feature
**Priority**: 1
**Execution Mode**: Series
**Parent**: 3.3
**Description**:
Implement the quick create bar at bottom of issues page:

1. Create fixed bottom bar component
2. Form fields:
   - Type selector (dropdown)
   - Title input (required)
3. Keyboard shortcuts:
   - Enter: Create issue
   - Shift+Enter: Create and open edit page
4. Show creation feedback (toast)
5. Clear form after creation
6. Focus management

**Acceptance Criteria**:
- Quick create bar is always visible
- Enter creates issue with defaults
- Shift+Enter creates and navigates to edit
- New issues appear in list immediately

---

### Issue 3.5: Issue Edit Page
**Type**: Feature
**Priority**: 1
**Execution Mode**: Series
**Parent**: 3.4
**Description**:
Create the full issue edit page:

1. Create edit page at `/projects/$projectId/issues/$issueId/edit`
2. Full form with all fields:
   - Title
   - Description (Markdown editor)
   - Status
   - Type
   - Priority
   - Execution mode
   - Working branch ID
   - Parent issues (multi-select)
   - Tags
3. Implement save and cancel actions
4. Add unsaved changes warning
5. Validate required fields

**Acceptance Criteria**:
- All issue fields are editable
- Changes save correctly
- Cancel returns without saving
- Unsaved changes prompt on navigation

---

### Issue 3.6: Issue History (Undo/Redo)
**Type**: Feature
**Priority**: 3
**Execution Mode**: Series
**Parent**: 3.5
**Description**:
Implement undo/redo functionality for issues:

1. Create `useIssueHistory` hook
2. Add undo/redo buttons to issues toolbar
3. Show history state (can undo, can redo)
4. Implement keyboard shortcuts (Ctrl+Z, Ctrl+Shift+Z)
5. Display history stack (optional)

**Acceptance Criteria**:
- Undo reverts last issue change
- Redo restores undone change
- Keyboard shortcuts work
- Buttons show disabled state appropriately

---

## Phase 4: Agent Sessions & Chat

### Issue 4.1: Chat Message List
**Type**: Feature
**Priority**: 1
**Execution Mode**: Series
**Description**:
Create the chat message display component:

1. Create session page at `/sessions/$sessionId`
2. Build message list component with:
   - User messages (right-aligned)
   - Assistant messages (left-aligned)
   - System messages (centered, muted)
3. Implement auto-scroll to bottom
4. Handle message streaming (incremental updates)
5. Show timestamps and status indicators
6. Add loading state for historical messages

**Acceptance Criteria**:
- Messages display correctly by role
- Auto-scroll follows new messages
- Streaming messages update in real-time
- Historical sessions load properly

---

### Issue 4.2: Chat Input Component
**Type**: Feature
**Priority**: 1
**Execution Mode**: Series
**Parent**: 4.1
**Description**:
Create the chat input component:

1. Multi-line text input with auto-resize
2. Send button with loading state
3. Permission mode selector:
   - Default
   - Bypass permissions
   - Accept edits
   - Plan mode
4. Model selector dropdown (Opus, Sonnet, Haiku)
5. Keyboard shortcuts:
   - Enter: Send message
   - Shift+Enter: New line
6. Disable input when agent is processing

**Acceptance Criteria**:
- Messages send correctly
- Permission mode is passed to API
- Model selection works
- Input is disabled during processing

---

### Issue 4.3: Tool Result Renderers
**Type**: Feature
**Priority**: 2
**Execution Mode**: Parallel
**Description**:
Create specialized components for rendering tool outputs:

1. Create base `ToolResultRenderer` component with:
   - Tool name header
   - Collapsible content
   - Copy to clipboard action
2. Specialized renderers:
   - `BashToolResult`: Command + output with syntax highlighting
   - `ReadToolResult`: File path + content preview
   - `WriteToolResult`: File path + diff view
   - `GrepToolResult`: Search results with context
   - `GlobToolResult`: File list
   - `AgentToolResult`: Nested agent status
   - `WebToolResult`: URL + summary
3. Create `ThinkingBlock` component (expandable)
4. Create `ToolUseBlock` component (shows tool invocation)

**Acceptance Criteria**:
- Each tool type renders appropriately
- Long outputs are collapsible
- Syntax highlighting for code
- Copy functionality works

---

### Issue 4.4: Sessions List Page
**Type**: Feature
**Priority**: 1
**Execution Mode**: Series
**Parent**: 4.3
**Description**:
Create the sessions management page:

1. Create page at `/sessions`
2. List active and recent sessions with:
   - Session ID
   - Associated issue/PR
   - Status (active, completed, error)
   - Last activity timestamp
   - Message count
3. Filter by status and project
4. Click to open session
5. Archive/delete actions
6. Real-time status updates via SignalR

**Acceptance Criteria**:
- Sessions list displays correctly
- Filtering works
- Status updates in real-time
- Navigation to session works

---

### Issue 4.5: Agent Launcher and Controls
**Type**: Feature
**Priority**: 1
**Execution Mode**: Series
**Parent**: 4.4
**Description**:
Implement agent spawning and control UI:

1. Create `AgentLauncher` component for issue detail panel:
   - Agent type selector (prompts dropdown)
   - Model selector
   - Start button
2. Create `AgentStatusIndicator` component:
   - Working/Waiting state
   - Token count
   - Duration
3. Create header status indicator:
   - Count of active agents
   - Click to navigate to sessions
4. Implement stop/pause agent actions

**Acceptance Criteria**:
- Agents can be started on issues
- Status displays correctly
- Header shows active agent count
- Agents can be stopped

---

## Phase 5: Branches & Pull Requests

### Issue 5.1: Branches Tab
**Type**: Feature
**Priority**: 2
**Execution Mode**: Series
**Description**:
Implement the branches/worktrees management tab:

1. Create branches tab in project detail
2. List local worktrees with:
   - Branch name
   - Status (clean, dirty, conflicts)
   - Associated issue/PR
   - Last commit info
3. List remote branches (not pulled)
4. Actions per worktree:
   - Pull changes
   - Start agent
   - Delete (with confirmation)
5. Create new worktree from remote branch
6. Refresh all button

**Acceptance Criteria**:
- Local worktrees display
- Remote branches show separately
- Pull/delete actions work
- New worktrees can be created

---

### Issue 5.2: Pull Requests Display
**Type**: Feature
**Priority**: 2
**Execution Mode**: Parallel (with 5.1)
**Description**:
Implement PR status display and integration:

1. Create PR section in project dashboard
2. Display open PRs with:
   - PR number and title
   - Status badges (draft, approved, changes requested)
   - Review status
   - CI status
3. Display recently merged PRs
4. Link PRs to issues
5. Quick actions:
   - View on GitHub
   - Start agent on PR branch
6. Loading skeletons for PR data

**Acceptance Criteria**:
- Open and merged PRs display
- Status badges are accurate
- GitHub links work
- PR-issue linkage shows

---

## Phase 6: Settings & Configuration

### Issue 6.1: Global Settings Page
**Type**: Feature
**Priority**: 2
**Execution Mode**: Series
**Description**:
Create the global settings page:

1. Create page at `/settings`
2. GitHub authentication section:
   - Auth status indicator
   - Login/logout button
   - Token display (masked)
3. Git configuration section:
   - Credential helper status
   - User name/email display
4. Application settings:
   - Theme toggle (if applicable)
   - Notification preferences
5. Save settings action

**Acceptance Criteria**:
- Settings page accessible from sidebar
- GitHub auth status displays correctly
- Settings can be saved

---

### Issue 6.2: Project Prompts Tab
**Type**: Feature
**Priority**: 2
**Execution Mode**: Series
**Parent**: 6.1
**Description**:
Implement the prompts management tab:

1. Create prompts tab in project detail
2. List custom agent prompts:
   - Prompt name
   - Description
   - Last modified
3. Create new prompt form:
   - Name
   - System prompt content (Markdown editor)
4. Edit existing prompts
5. Delete prompts with confirmation
6. Set default prompt for project

**Acceptance Criteria**:
- Prompts list displays
- CRUD operations work
- Prompts available in agent launcher

---

### Issue 6.3: Project Secrets Tab
**Type**: Feature
**Priority**: 3
**Execution Mode**: Parallel (with 6.2)
**Description**:
Implement the secrets/environment variables tab:

1. Create secrets tab in project detail
2. List secrets (name only, values masked)
3. Add new secret:
   - Key name
   - Value (password input)
4. Edit secret value
5. Delete secret with confirmation
6. Security: never display full values

**Acceptance Criteria**:
- Secrets list displays (masked)
- Add/edit/delete work
- Values never exposed in UI

---

## Phase 7: Real-time & Polish

### Issue 7.1: Notifications System
**Type**: Feature
**Priority**: 2
**Execution Mode**: Series
**Description**:
Implement the notifications system:

1. Create toast notification component
2. Connect to SignalR notifications hub
3. Handle notification types:
   - Issue changes
   - Agent status updates
   - System notifications
4. Notification history dropdown
5. Mark as read/dismiss actions
6. Sound/visual alerts (optional)

**Acceptance Criteria**:
- Toasts appear for notifications
- SignalR events trigger notifications
- Notifications can be dismissed

---

### Issue 7.2: Loading States and Skeletons
**Type**: Task
**Priority**: 2
**Execution Mode**: Parallel
**Description**:
Add comprehensive loading states throughout the app:

1. Create skeleton components for:
   - Project cards
   - Issue list rows
   - PR cards
   - Session messages
2. Replace all loading spinners with skeletons
3. Add Suspense boundaries
4. Implement optimistic updates where appropriate
5. Add error retry mechanisms

**Acceptance Criteria**:
- All loading states use skeletons
- Content doesn't jump on load
- Errors can be retried

---

### Issue 7.3: Responsive Design
**Type**: Task
**Priority**: 3
**Execution Mode**: Series
**Parent**: 7.2
**Description**:
Ensure the app works well on mobile devices:

1. Review all pages on mobile viewport
2. Fix layout issues:
   - Sidebar as drawer on mobile
   - Stack layouts vertically
   - Touch-friendly tap targets
3. Test issue detail panel on mobile
4. Test chat interface on mobile
5. Ensure all actions are accessible

**Acceptance Criteria**:
- App is usable on mobile
- No horizontal scroll issues
- All features accessible on touch

---

## Phase 8: Testing & Migration

### Issue 8.1: Unit and Integration Tests
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Description**:
Set up and write tests:

1. Configure Vitest for unit testing
2. Set up React Testing Library
3. Write tests for:
   - All custom hooks
   - Complex components
   - Form validation
   - API integration
4. Set up MSW for API mocking
5. Achieve >80% coverage on critical paths

**Acceptance Criteria**:
- Test suite runs with `npm test`
- Coverage meets threshold
- CI runs tests on PR

---

### Issue 8.2: E2E Tests
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Parent**: 8.1
**Description**:
Create end-to-end tests matching existing Playwright coverage:

1. Configure Playwright for React app
2. Port existing Blazor E2E tests
3. Test critical flows:
   - Project creation
   - Issue management
   - Agent session
   - Settings
4. Run E2E in CI pipeline

**Acceptance Criteria**:
- E2E tests pass
- Coverage matches Blazor tests
- Tests run in CI

---

### Issue 8.3: Feature Parity Verification
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Parent**: 8.2
**Description**:
Verify all Blazor features are implemented:

1. Create checklist of all Blazor features
2. Manually test each feature
3. Compare UI/UX between implementations
4. Document any intentional differences
5. Fix any missing functionality

**Acceptance Criteria**:
- All features verified
- Differences documented
- No critical regressions

---

### Issue 8.4: Migration Completion
**Type**: Task
**Priority**: 1
**Execution Mode**: Series
**Parent**: 8.3
**Description**:
Complete the migration:

1. Update documentation:
   - README
   - Development setup guide
   - Architecture docs
2. Update CI/CD:
   - Build React app in pipeline
   - Deploy React app
3. Create deprecation plan for Blazor
4. Performance benchmarks vs Blazor
5. Create PR with migration summary

**Acceptance Criteria**:
- Documentation updated
- Deployment working
- Performance acceptable
- PR created for final review

---

## Summary

**Total Issues**: 27 (organized in 8 phases)

**Execution Strategy**:
- Phases 1-3 are strictly sequential (foundation → projects → issues)
- Phase 4 (chat) can partially overlap with Phase 3 completion
- Phases 5-6 (branches, settings) can run in parallel
- Phase 7 (polish) can overlap with Phase 5-6
- Phase 8 (testing) runs after all features complete

**Estimated Complexity**:
- Phase 1: Foundation - 4 issues
- Phase 2: Projects - 3 issues
- Phase 3: Issues - 6 issues
- Phase 4: Sessions - 5 issues
- Phase 5: Branches/PRs - 2 issues
- Phase 6: Settings - 3 issues
- Phase 7: Polish - 3 issues
- Phase 8: Testing - 4 issues

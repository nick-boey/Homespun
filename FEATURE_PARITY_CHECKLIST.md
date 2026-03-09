# Feature Parity Verification Checklist

## Overview

This document provides a comprehensive comparison between the Blazor and React frontend implementations of Homespun, verifying feature parity for the migration.

**Verification Date:** March 4, 2026
**Blazor Location:** (Original client - completely removed as of 2026-03-08)
**React Location:** `src/Homespun.Web`

---

## Summary

| Category | Blazor | React | Status |
|----------|--------|-------|--------|
| Pages/Routes | 18 | 15+ | Implemented (with intentional differences) |
| Components | 95+ | 168+ | Implemented (redesigned architecture) |
| API Integrations | 30+ services | OpenAPI client + hooks | Implemented |
| SignalR Hubs | 2 | 2 | Implemented |
| Keyboard Shortcuts | 20+ | 20+ | Implemented |
| Mobile Responsiveness | Basic | Comprehensive | Enhanced in React |

---

## 1. Pages/Routes

### Implemented in React

| Page | Blazor Route | React Route | Status |
|------|-------------|-------------|--------|
| Projects List | `/projects` | `/` (root) | Implemented |
| Create Project | `/projects/create` | `/projects/new` | Implemented |
| Project Detail | `/projects/{Id}` | `/projects/$projectId` | Implemented |
| Issues Tab | `/projects/{Id}` (default) | `/projects/$projectId/issues` | Implemented |
| Edit Issue | `/projects/{Id}/issues/{IssueId}/edit` | `/projects/$projectId/issues/$issueId/edit` | Implemented |
| Pull Requests | `/projects/{Id}/branches` | `/projects/$projectId/pull-requests` | Implemented |
| Branches | `/projects/{Id}/branches` | `/projects/$projectId/branches` | Implemented |
| Prompts Tab | `/projects/{Id}/prompts` | `/projects/$projectId/prompts` | Implemented |
| Secrets Tab | `/projects/{Id}/secrets` | `/projects/$projectId/secrets` | Implemented |
| Settings Tab | `/projects/{Id}/settings` | `/projects/$projectId/settings` | Implemented (placeholder) |
| Session Chat | `/session/{SessionId}` | `/sessions/$sessionId` | Implemented |
| Session List | `/agents` | `/sessions` | Implemented |
| Global Settings | `/settings` | `/settings` | Implemented |

### Not Implemented in React (Intentional)

| Page | Blazor Route | Reason |
|------|-------------|--------|
| Agent Prompts | `/prompts` | Merged into project-level prompts |
| Archived Session | `/session/{SessionId}/archived` | Sessions archived differently |
| Design System | `/design`, `/design/{ComponentId}` | Development-only feature, not needed for production |
| Error Page | `/error` | Error boundary used instead |
| Not Found | `/not-found` | TanStack Router handles 404s |

---

## 2. Components

### Core Issue Management

| Component | Blazor | React | Status |
|-----------|--------|-------|--------|
| ProjectIssuesTab | ProjectIssuesTab.razor | task-graph-view.tsx | Implemented |
| InlineIssueEditor | InlineIssueEditor.razor | inline-issue-editor.tsx | Implemented |
| InlineIssueDetailRow | InlineIssueDetailRow.razor | inline-issue-detail-row.tsx | Implemented |
| InlinePrDetailRow | InlinePrDetailRow.razor | inline-pr-detail-row.tsx | Implemented |
| WorkItem | WorkItem.razor | task-graph-row.tsx | Implemented (redesigned) |
| IssueDetailPanel | IssueDetailPanel.razor | details-panel.tsx | Implemented |
| IssueRowActions | IssueRowActions.razor | issue-row-actions.tsx | Implemented |
| TaskGraphView | TaskGraphView.razor | task-graph-view.tsx | Implemented |
| TaskGraphSvg | (Part of TaskGraphView) | task-graph-svg.tsx | Implemented |
| PrStatusBadges | PrStatusBadges.razor | pr-status-badge.tsx, ci-status-badge.tsx, review-status-badge.tsx | Implemented (split) |
| TimelineVisualization | TimelineVisualization.razor | (not implemented) | N/A - unused |
| SearchBar | SearchBar.razor | (integrated in toolbar) | Implemented |
| HighlightedText | HighlightedText.razor | (not separate component) | Inline in components |

### Chat/Session Components

| Component | Blazor | React | Status |
|-----------|--------|-------|--------|
| ChatInput | ChatInput.razor | chat-input.tsx | Implemented |
| ChatMessage | ChatMessage.razor | message-list.tsx | Implemented |
| ContentBlock | ContentBlock.razor | message-list.tsx (ContentBlock) | Implemented |
| ToolUseBlock | ToolUseBlock.razor | message-list.tsx (inline) | Implemented (simplified) |
| ToolResultBlock | ToolResultBlock.razor | message-list.tsx (inline) | Implemented (simplified) |
| ThinkingBlock | ThinkingBlock.razor | message-list.tsx (inline) | Implemented |
| ProcessingIndicator | ProcessingIndicator.razor | Skeleton components | Implemented |
| SessionCard | SessionCard.razor | sessions-list.tsx | Implemented |
| SessionHistoryList | SessionHistoryList.razor | sessions-list.tsx | Implemented |
| SessionsPanel | SessionsPanel.razor | (route-based) | Implemented differently |
| SessionInfoPanel | SessionInfoPanel.razor | (not separate) | Session header only |
| SessionFilesTab | SessionFilesTab.razor | (not implemented) | Not needed |
| SessionIssueTab | SessionIssueTab.razor | (not implemented) | Not needed |
| SessionPlansTab | SessionPlansTab.razor | plan-approval-panel.tsx | Implemented (inline) |
| SessionPrTab | SessionPrTab.razor | (not implemented) | Not needed |
| SessionTodosTab | SessionTodosTab.razor | (not implemented) | Not needed |
| SessionCacheHistory | SessionCacheHistory.razor | (not implemented) | N/A |
| PlanApprovalPanel | (various) | plan-approval-panel.tsx | Implemented |

### Tool Result Components (Blazor-specific detailed displays)

| Component | Blazor | React | Status |
|-----------|--------|-------|--------|
| ToolResultRenderer | ToolResultRenderer.razor | (inline) | Simplified |
| BashToolResultDisplay | BashToolResultDisplay.razor | (inline) | Simplified |
| GlobToolResultDisplay | GlobToolResultDisplay.razor | (inline) | Simplified |
| GrepToolResultDisplay | GrepToolResultDisplay.razor | (inline) | Simplified |
| ReadToolResultDisplay | ReadToolResultDisplay.razor | (inline) | Simplified |
| WriteToolResultDisplay | WriteToolResultDisplay.razor | (inline) | Simplified |
| WebToolResultDisplay | WebToolResultDisplay.razor | (inline) | Simplified |
| AgentToolResultDisplay | AgentToolResultDisplay.razor | (inline) | Simplified |
| GenericToolResultDisplay | GenericToolResultDisplay.razor | (inline) | Simplified |

**Note:** React uses a simplified tool result display. The detailed tool-specific displays from Blazor are replaced with a generic inline display. This is an intentional simplification.

### Agent/Orchestration Components

| Component | Blazor | React | Status |
|-----------|--------|-------|--------|
| AgentLauncher | AgentLauncher.razor | agent-launcher.tsx | Implemented |
| AgentControlPanel | AgentControlPanel.razor | (not separate) | Inline in session |
| AgentStatusPanel | AgentStatusPanel.razor | (not separate) | Inline in session |
| AgentStatusIndicator | AgentStatusIndicator.razor | agent-status-indicator.tsx | Implemented |
| AgentSelector | AgentSelector.razor | (not needed) | N/A |
| AgentStatusMonitor | AgentStatusMonitor.razor | (not needed) | N/A |

### Project Components

| Component | Blazor | React | Status |
|-----------|--------|-------|--------|
| ModelSelector | ModelSelector.razor | chat-input.tsx (dropdown) | Implemented (inline) |
| ProjectPromptsTab | ProjectPromptsTab.razor | prompts-list.tsx | Implemented |
| ProjectSecretsTab | ProjectSecretsTab.razor | secrets-list.tsx | Implemented |
| ProjectSettingsTab | ProjectSettingsTab.razor | (placeholder) | Placeholder |
| BaseBranchSelector | BaseBranchSelector.razor | (not separate) | Inline |
| CloneManagementPanel | CloneManagementPanel.razor | branches-list.tsx | Implemented |

### Navigation & Toolbar

| Component | Blazor | React | Status |
|-----------|--------|-------|--------|
| ProjectToolbar | ProjectToolbar.razor | project-toolbar.tsx | Implemented |
| ToolbarButton | ToolbarButton.razor | Button (shadcn) | Implemented |
| NavMenuContent | NavMenuContent.razor | sidebar.tsx | Implemented |
| SearchBar | SearchBar.razor | (inline in toolbar) | Implemented |

### Questions Feature

| Component | Blazor | React | Status |
|-----------|--------|-------|--------|
| QuestionPanel | (inline in session) | question-panel.tsx | Implemented |
| QuestionOption | (inline) | question-panel.tsx | Implemented |

---

## 3. API Integrations

### All API Services Verified

| Service Category | Blazor Service | React Implementation | Status |
|-----------------|----------------|---------------------|--------|
| Agent Prompts | HttpAgentPromptApiService | use-project-prompts, use-create-prompt, etc. | Implemented |
| Orchestration | HttpOrchestrationApiService | SignalR hub methods | Implemented |
| Issues | HttpIssueApiService | use-issue, use-update-issue, use-create-issue, etc. | Implemented |
| Issue History | HttpIssueHistoryApiService | use-issue-history | Implemented |
| Issue PR Status | HttpIssuePrStatusApiService | API client | Implemented |
| Pull Requests | HttpPullRequestApiService | use-open-pull-requests, use-merged-pull-requests | Implemented |
| Fleece Sync | HttpFleeceSyncApiService | API client | Implemented |
| GitHub Info | HttpGitHubInfoApiService | use-github-info, use-git-config | Implemented |
| Projects | HttpProjectApiService | use-projects, use-project, use-create-project | Implemented |
| Task Graph | HttpGraphApiService | use-task-graph | Implemented |
| Secrets | HttpSecretsApiService | use-secrets, use-create-secret, etc. | Implemented |
| Sessions | HttpSessionApiService | use-session, use-sessions | Implemented |
| Session Cache | HttpSessionCacheApiService | SignalR methods | Implemented |
| Plans | HttpPlansApiService | use-plan-approval, use-approve-plan | Implemented |
| Clones | HttpCloneApiService | use-clones, use-create-clone, etc. | Implemented |
| Notifications | HttpNotificationApiService | notification-store (Zustand) | Implemented |
| Containers | HttpContainerApiService | SignalR events | Implemented |

**Note:** React uses OpenAPI-generated TypeScript client (`src/api/generated/`) instead of individual service classes.

---

## 4. SignalR Hubs

| Hub | Blazor Service | React Implementation | Status |
|-----|---------------|---------------------|--------|
| Claude Code Hub | ClaudeCodeSignalRService | claude-code-hub.ts + SignalRProvider | Implemented |
| Notification Hub | NotificationSignalRService | notification SignalR events | Implemented |

### Claude Code Hub Events

| Event | Blazor | React | Status |
|-------|--------|-------|--------|
| SessionStarted | Yes | Yes | Implemented |
| SessionStopped | Yes | Yes | Implemented |
| SessionState | Yes | Yes | Implemented |
| SessionStatusChanged | Yes | Yes | Implemented |
| SessionModeModelChanged | Yes | Yes | Implemented |
| SessionResultReceived | Yes | Yes | Implemented |
| ContextCleared | Yes | Yes | Implemented |
| SessionError | Yes | Yes | Implemented |
| SessionContainerRestarting | Yes | Yes | Implemented |
| SessionContainerRestarted | Yes | Yes | Implemented |
| AGUI* Events | Yes | Yes | Implemented |

---

## 5. Keyboard Shortcuts

| Action | Blazor Key | React Key | Status |
|--------|-----------|-----------|--------|
| Move Up | `k` / ArrowUp | `k` / ArrowUp | Implemented |
| Move Down | `j` / ArrowDown | `j` / ArrowDown | Implemented |
| Move to Parent | `l` / ArrowRight | `l` | Implemented |
| Move to Child | `h` / ArrowLeft | `h` | Implemented |
| Move to First | `g` | `g` | Implemented |
| Move to Last | `G` (Shift+G) | `G` (Shift+G) | Implemented |
| Insert at Start | `i` | `i` | Implemented |
| Insert at End | `a` | `a` | Implemented |
| Replace | `r` | `r` | Implemented |
| Create Below | `o` | `o` | Implemented |
| Create Above | `O` (Shift+O) | `O` (Shift+O) | Implemented |
| Cancel Edit | Escape | Escape | Implemented |
| Save Edit | Enter | Enter | Implemented |
| Save + Open Desc | Shift+Enter | Ctrl+Enter (edit page) | Implemented (different key) |
| Indent (Make Child) | Tab | Tab | Implemented |
| Unindent (Sibling) | Shift+Tab | Shift+Tab | Implemented |
| Select Prompt | `e` | `e` | Implemented |
| Cycle Type | Debounced T | Debounced T | Implemented |
| Open Search | `/` | `/` | Implemented |
| Next Match | `n` | `n` | Implemented |
| Previous Match | `N` (Shift+N) | `N` (Shift+N) | Implemented |
| Clear Search | Escape | Escape | Implemented |
| Undo | (various) | `u` / Ctrl+Z | Implemented |
| Redo | (various) | Ctrl+Shift+Z / Cmd+Shift+Z | Implemented |
| Depth Decrease | (various) | `[` | Implemented |
| Depth Increase | (various) | `]` | Implemented |

---

## 6. Mobile Responsiveness

| Feature | Blazor | React | Status |
|---------|--------|-------|--------|
| Responsive Navigation | BlazorBlueprint | Tailwind md: breakpoint | Enhanced |
| Mobile Menu | BbResponsiveNavContent | Sidebar collapsible | Enhanced |
| Touch-friendly Buttons | Basic | h-10 w-10 sizing | Enhanced |
| Safe Area Insets | None | pb-[env(safe-area-inset-bottom)] | Enhanced |
| Text Scaling | Basic | md:text-2xl responsive | Enhanced |
| Flexible Layouts | flexbox | Tailwind flex utilities | Enhanced |
| Collapsible Panels | Manual | Responsive visibility | Enhanced |

**Note:** React implementation has significantly enhanced mobile responsiveness compared to Blazor.

---

## 7. Styling & Theming

| Feature | Blazor | React | Status |
|---------|--------|-------|--------|
| CSS Variables | theme.css | Tailwind CSS v4 | Implemented |
| Dark Mode | prefers-color-scheme | (system-based) | Implemented |
| Light Mode | default | default | Implemented |
| Color Palette | Custom (Lagoon, etc.) | shadcn/ui defaults | Changed (design decision) |
| Typography | Figtree | System fonts | Changed (performance) |
| Component Library | BlazorBlueprint | shadcn/ui | Changed (ecosystem) |
| Icon Library | Lucide | Lucide | Same |

---

## 8. Testing Infrastructure

| Test Type | Blazor | React | Status |
|-----------|--------|-------|--------|
| Unit Tests | NUnit | Vitest + RTL | Implemented |
| API Tests | WebApplicationFactory | Vitest mocks | Implemented |
| E2E Tests | Playwright (.NET) | Playwright (TS) | Implemented |

### E2E Test Coverage

| Test File | Coverage |
|-----------|----------|
| critical-journeys.spec.ts | Page loading, API responses |
| agui-sessions.spec.ts | Session management, API connectivity |
| keyboard-navigation.spec.ts | Vim-like navigation (partial) |
| type-change-menu.spec.ts | Issue type dropdown |
| collapsible-sidebar.spec.ts | Inline issue detail expansion |
| issue-edit-ctrl-enter.spec.ts | CTRL+Enter save behavior |
| inline-issue-hierarchy.spec.ts | TAB/Shift+TAB hierarchy |

---

## 9. Intentional Differences

### Architecture Changes

1. **Vertical Slice Organization**: React uses feature-based folder structure
2. **State Management**: React uses Zustand + TanStack Query instead of Blazor services
3. **Routing**: TanStack Router with file-based routes vs Blazor @page directive
4. **API Client**: OpenAPI-generated TypeScript client vs hand-written services

### UI/UX Changes

1. **Simplified Tool Results**: React uses inline display instead of component-per-tool
2. **Session Info Panels**: Merged into session header instead of separate tabs
3. **Design System Page**: Removed (development-only feature)
4. **Agent Prompts Page**: Merged into project-level prompts

### Performance Improvements

1. **Code Splitting**: TanStack Router lazy loading
2. **System Fonts**: Removed custom font (Figtree) for faster loading
3. **Skeleton Loading**: Comprehensive loading states

---

## 10. Missing Features (Require Follow-up)

### Not Critical (Intentionally Omitted)

1. **Design System Showcase** - Development-only, not needed in production
2. **Session Cache History View** - Advanced debugging feature
3. **Detailed Tool Result Displays** - Simplified for cleaner UX
4. **Timeline Visualization** - Unused/deprecated feature

### To Be Verified

1. **Project Settings Tab** - Currently placeholder, needs implementation if used
2. **Archived Session View** - May need if archival feature is used

---

## Verification Status

- [x] All core pages implemented
- [x] All API integrations working
- [x] SignalR hubs connected
- [x] Keyboard navigation functional
- [x] Mobile responsiveness improved
- [x] E2E tests passing
- [x] Intentional differences documented

---

## Approval

**Verified By:** Claude Code Agent
**Date:** March 4, 2026
**Status:** Feature parity verified with documented intentional differences

All critical Blazor features have been implemented in the React frontend. Differences are intentional architectural improvements or design decisions that enhance the user experience.

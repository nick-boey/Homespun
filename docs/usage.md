# Usage Guide

This guide covers day-to-day operations with Homespun, from creating projects to running AI agents and managing pull requests.

## Table of contents

- [Project management](#project-management)
- [Issue tracking](#issue-tracking)
- [Running agents](#running-agents)
- [Git and PR workflow](#git-and-pr-workflow)

## Project management

### Creating a new project

Navigate to **Projects** and click **New Project**. Homespun supports two project types:

| Type | Description |
|---|---|
| **GitHub Repository** | Links to an existing GitHub repository. Enter the owner/repo (e.g. `acme/my-app`) or the full URL. |
| **Local Project** | Creates a new local git repository. Names may contain letters, numbers, hyphens, and underscores only. |

Both types require a **project name**. You can optionally set a **default branch** (defaults to `main`).

Once created, Homespun clones the repository locally to `~/.homespun/src/{repository-name}/{branch-name}`.

### Configuring project settings

Open a project and select the **Settings** tab to configure:

- **Default model** — choose the Claude model used for new agent sessions (Opus, Sonnet, or Haiku).
- **GitHub sync** — trigger a full refresh to re-download all PRs from GitHub.

#### System prompts

The **Prompts** tab lets you create custom system prompts for agents working in this project. Prompts support template variables that are filled in automatically when a session starts:

| Variable | Description |
|---|---|
| `{{ProjectName}}` | The project name |
| `{{FeatureTitle}}` | Title of the linked issue or feature |
| `{{FeatureDescription}}` | Full description of the linked issue |
| `{{BranchName}}` | The working branch name |
| `{{ClonePath}}` | Path to the agent's clone directory |

#### Secrets

The **Secrets** tab manages project-level environment variables that are made available to agents during sessions.

### Project overview and navigation

The project page has tabs for each major feature area:

| Tab | Purpose |
|---|---|
| **Issues** | Fleece-based issue tracking with a visual task graph |
| **Pull Requests** | GitHub PR synchronization and status |
| **Branches** | Local git branch management |
| **Clones** | Git clones created for agent work |
| **Prompts** | Custom agent system prompts |
| **Secrets** | Project-level environment variables |
| **Settings** | Project configuration |

## Issue tracking

Homespun integrates with [Fleece](https://github.com/nick-boey/fleece), a local issue tracker that stores issues as JSONL files in the `.fleece/` directory of your repository.

### Creating issues

**From the UI:**

1. Open a project and go to the **Issues** tab.
2. Click to create a new issue in the task graph.
3. Fill in the title, description (markdown supported), type, and priority.
4. Issues can be created above or below existing items in the tree.

**From the Fleece CLI:**

```bash
fleece create -t "Add login page" -y feature -d "Implement OAuth login flow"
```

### Issue types

| Type | Usage |
|---|---|
| **Task** | General work item |
| **Bug** | Defect or error to fix |
| **Feature** | New functionality |
| **Chore** | Maintenance or housekeeping work |
| **Verify** | Groups related work; acts as a checkpoint to confirm all child tasks are complete |

### Issue hierarchy and parent-child relationships

Issues can be organized into hierarchies to break down complex work:

- **Create child issues** under a parent to decompose work into smaller tasks.
- **Drag and drop** issues in the task graph to rearrange the hierarchy.
- Use the **Make child of** or **Make parent of** actions to restructure relationships.
- **Execution mode** controls how child issues are processed:
  - **Series** — children are worked on sequentially.
  - **Parallel** — children can be worked on concurrently.

Use `fleece list --tree` to view the hierarchy in the CLI, or `fleece list --next` to see the execution order.

### Issue status workflow

Issues progress through these statuses:

```
open → progress → review → complete
                         ↘ archived (no longer relevant)
                         ↘ closed (abandoned/won't fix)
```

| Status | Meaning |
|---|---|
| **Open** | Ready to be worked on |
| **Progress** | Actively being worked on |
| **Review** | Work is done and awaiting review (typically linked to a PR) |
| **Complete** | Work is finished and verified |
| **Archived** | No longer relevant |
| **Closed** | Abandoned or won't fix |

### Filtering and searching issues

The issue view supports filtering and search:

- **Search** — full-text search across visible issues.
- **Filter by assignee** — use `assignee:me` to show only your issues.
- **Filter by status** — e.g. `status:progress` to show only in-progress work.
- **Filter by type** — narrow down to specific issue types.
- **Depth control** — adjust how many levels of the hierarchy are visible (1 to unlimited).

By default, completed, archived, and closed issues are hidden. Use `fleece list --all` in the CLI to include them.

## Running agents

Homespun uses Claude Code to run AI agents that can analyze and modify your codebase. Agents operate in one of two modes.

### Plan mode vs Build mode

| | Plan mode | Build mode |
|---|---|---|
| **Purpose** | Analysis and planning | Implementation |
| **File access** | Read-only | Full read/write |
| **Available tools** | Read, Glob, Grep, WebFetch, WebSearch | All tools including Write, Edit, Bash |
| **Output** | Plan document for review | Code changes, PRs, commits |

A typical workflow starts in **Plan mode** to analyze the problem and produce a plan, then switches to **Build mode** after the plan is approved.

### Starting an agent session

1. Navigate to the **Issues** tab in your project.
2. Click **Run Agent** on the issue you want to work on.
3. In the launcher dialog, configure:
   - **Model** — Opus, Sonnet, or Haiku.
   - **System prompt** — select a custom prompt or use the default.
   - **Base branch** — the branch to check out from.
4. Click to start. Homespun creates a dedicated git clone and agent session in the background.
5. You'll receive a notification when the session is ready.

### Monitoring agent progress

Once a session starts, the **Sessions** page shows all active and past sessions. Each session displays:

- **Status badge** — Starting, Running, Waiting for Input, Stopped, Error, etc.
- **Mode indicator** — Plan or Build.
- **Model** — which Claude model is being used.
- **Cost and duration** — tracked automatically.

Session statuses:

| Status | Description |
|---|---|
| **Starting** | Session is initializing |
| **Running** | Agent is generating a response |
| **Waiting for Input** | Agent is waiting for your message |
| **Waiting for Plan Execution** | A plan is ready for your approval |
| **Stopped** | Session ended normally |
| **Error** | An error occurred |

### Agent chat interface

The chat interface provides real-time interaction with the agent:

- **Message stream** — see agent responses as they're generated, including tool calls (file reads, bash commands, edits) and their results.
- **Send messages** — type in the input box to guide the agent, ask questions, or provide feedback.
- **Stop button** — halt the agent mid-response if needed.
- **New Session** — clear context and start a fresh conversation on the same issue.
- **Info panel** — toggle the right sidebar to see session metadata, history, and linked entities.

### Plan approval workflow

When an agent completes its analysis in Plan mode:

1. The agent submits a plan and the status changes to **Waiting for Plan Execution**.
2. The plan content is displayed in a dedicated approval panel with rendered markdown.
3. You have three options:
   - **Approve & Clear Context** — accept the plan and start a fresh Build mode session for implementation.
   - **Approve & Keep Context** — accept the plan and continue the conversation in Build mode.
   - **Reject with Feedback** — send the agent back to refine the plan with your notes.

### Understanding agent outputs

Agents produce several types of output visible in the chat:

- **Text responses** — analysis, explanations, and status updates.
- **Tool calls** — displayed inline showing what the agent read, searched, edited, or executed. Each tool call shows its result (file contents, command output, etc.).
- **Code changes** — edits and new files are shown as diffs.
- **Commits and PRs** — in Build mode, agents can create git commits and open pull requests.

## Git and PR workflow

### How git clones work for feature isolation

When you start an agent on an issue, Homespun creates a **dedicated git clone** for that work. This provides:

- **Isolation** — each issue gets its own working directory, so agents don't interfere with each other.
- **Clean state** — clones start from a specified base branch.
- **Traceability** — clones are linked to their source issue and any resulting PR.

Clones are visible in the **Clones** tab of your project. Each clone shows:

- The clone path and folder name.
- Expected vs actual branch.
- Linked issue (with status).
- Linked PR (with status and GitHub link).

### Managing clones

Clones become **stale** when their linked PR is merged/closed or their linked issue reaches a terminal status (complete, archived, closed). Stale clones can be deleted individually or in bulk using the **Delete All Stale** button.

### Creating and managing branches

The **Branches** tab lists all branches in the project repository. From here you can:

- View branch details and recent commits.
- Create new branches.
- Delete branches that are no longer needed.

Agents automatically create and work on branches named after the issue they're assigned to.

### PR synchronization with GitHub

Homespun keeps pull requests in sync with GitHub:

- **Automatic sync** — PRs are updated when agents create or modify them.
- **Manual sync** — click **Sync from GitHub** on the Pull Requests tab to fetch the latest state.
- **Full refresh** — available in project Settings to re-download all PR data.

The **Pull Requests** tab shows two sections:

| Section | Content |
|---|---|
| **Open Pull Requests** | PRs currently in development or review |
| **Recently Merged** | PRs that have been merged |

Each PR displays its title, author, creation date, review status, linked issue, and a direct link to GitHub.

### PR status indicators

| Status | Meaning |
|---|---|
| **In Progress** | PR is being worked on |
| **Ready for Review** | PR is ready for code review |
| **Ready for Merging** | Reviews are approved, ready to merge |
| **Checks Failing** | CI checks are failing |
| **Conflict** | Merge conflicts need resolution |
| **Merged** | PR has been merged |
| **Closed** | PR was closed without merging |

### Reviewing and merging PRs

1. Click on a PR in the Pull Requests tab to see its details.
2. Review the description, linked issues, and status.
3. Use the **GitHub link** to review code changes on GitHub.
4. Once reviews are approved and checks pass, merge from the Homespun UI or directly on GitHub.
5. After merging, the linked clone becomes stale and can be cleaned up.

## Typical workflow

Here is a complete workflow for developing a feature with Homespun:

1. **Create an issue** — add a Feature issue in the task graph with a clear title and description.
2. **Break it down** — create child Task issues if the feature is complex.
3. **Start an agent** — click Run Agent on the issue, select Plan mode.
4. **Review the plan** — read the agent's analysis and approve or refine.
5. **Build** — approve the plan to switch to Build mode. The agent implements the changes.
6. **PR created** — the agent opens a pull request linked to the issue.
7. **Review on GitHub** — review the code, request changes if needed.
8. **Merge** — merge the PR once all checks pass.
9. **Clean up** — delete stale clones and mark the issue as Complete.

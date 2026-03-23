# Multi-User Configuration

Homespun is designed as a **single-user-per-instance** application. Each deployment runs with one GitHub identity, one git author, and one set of credentials. To support multiple users, deploy separate Homespun instances — one per user.

## Table of contents

- [How user identity works](#how-user-identity-works)
- [Setting up multiple instances](#setting-up-multiple-instances)
- [GitHub authentication per user](#github-authentication-per-user)
- [User-specific settings](#user-specific-settings)
- [Access control](#access-control)

## How user identity works

Each Homespun instance uses three forms of identity:

| Identity | Purpose | How it's set |
|---|---|---|
| **GitHub token** | API access, PR sync, repo operations | `GITHUB_TOKEN` environment variable |
| **Git author** | Commit attribution (`Author` and `Committer` fields) | `GIT_AUTHOR_NAME` / `GIT_AUTHOR_EMAIL` environment variables |
| **User email** | Issue assignment and filtering within Homespun | Settings page in the UI (`/settings`) |

### GitHub token resolution

The server resolves the GitHub token from the first available source:

1. .NET User Secrets (`GitHub:Token` key)
2. Configuration / environment variable (`GITHUB_TOKEN`)
3. System environment variable (`GITHUB_TOKEN`)

At the host level, `run.sh` resolves the token before passing it to the container:

1. `~/.homespun/env` file (sourced at startup)
2. `HSP_GITHUB_TOKEN` environment variable (VM secrets, highest priority)
3. `GITHUB_TOKEN` environment variable
4. .NET User Secrets JSON file
5. `.env` file in the repository root

### Git author identity

The git author used for all commits is configured via environment variables:

| Variable | Default | Description |
|---|---|---|
| `GIT_AUTHOR_NAME` | `Homespun Bot` | Name shown on git commits |
| `GIT_AUTHOR_EMAIL` | `homespun@localhost` | Email shown on git commits |

These are set at container startup and apply to all git operations within that instance.

## Setting up multiple instances

Each user needs their own Homespun instance with separate credentials, ports, and data directories.

### 1. Create per-user credential files

For each user, create a credential file:

```bash
# User: alice
mkdir -p ~/.homespun/alice
cat > ~/.homespun/alice/env << 'EOF'
export GITHUB_TOKEN=ghp_alice_token_here
export CLAUDE_CODE_OAUTH_TOKEN=alice_oauth_token_here
export GIT_AUTHOR_NAME="Alice Smith"
export GIT_AUTHOR_EMAIL="alice@example.com"
EOF

# User: bob
mkdir -p ~/.homespun/bob
cat > ~/.homespun/bob/env << 'EOF'
export GITHUB_TOKEN=ghp_bob_token_here
export CLAUDE_CODE_OAUTH_TOKEN=bob_oauth_token_here
export GIT_AUTHOR_NAME="Bob Jones"
export GIT_AUTHOR_EMAIL="bob@example.com"
EOF
```

### 2. Launch separate instances

Use `run.sh` with `--port`, `--data-dir`, and `--container-name` to isolate each instance:

```bash
# Alice's instance — port 8080
source ~/.homespun/alice/env
./scripts/run.sh \
  --port 8080 \
  --data-dir ~/.homespun-container/alice/data \
  --container-name homespun-alice

# Bob's instance — port 8081
source ~/.homespun/bob/env
./scripts/run.sh \
  --port 8081 \
  --data-dir ~/.homespun-container/bob/data \
  --container-name homespun-bob
```

Each instance gets:

- Its own container name (avoids conflicts)
- Its own host port
- Its own data directory (projects, issues, settings are isolated)
- Its own GitHub token and git identity (from the sourced env file)

### 3. Access each instance

| User | URL |
|---|---|
| Alice | `http://localhost:8080` |
| Bob | `http://localhost:8081` |

### 4. Set user email in each instance

After starting each instance, open the Settings page (`/settings`) and set the user email. This email is used for Fleece issue assignment and filtering.

## GitHub authentication per user

### Token requirements

Each user needs a GitHub Personal Access Token (PAT) with the following scopes:

| Scope | Purpose |
|---|---|
| `repo` | Full repository access — clone, push, PR creation and sync |

Classic PATs work out of the box. Fine-grained PATs need repository-level permissions for each repo the user works with.

### Creating a token

1. Go to [GitHub Settings > Developer settings > Personal access tokens](https://github.com/settings/tokens)
2. Generate a **classic** token with `repo` scope
3. Add the token to the user's credential file (see above)

### How tokens are used

The GitHub token is used for:

- **Git operations**: Passed via a `GIT_ASKPASS` helper script that echoes the token when git prompts for credentials
- **GitHub API**: Used with Octokit for PR synchronization, repository listing, and other API calls
- **gh CLI**: Injected as `GH_TOKEN` for any `gh` commands run inside the container

All agent sessions spawned by the instance inherit the same token.

## User-specific settings

### Per-instance isolation

Each Homespun instance maintains its own:

| Resource | Storage location | Isolation |
|---|---|---|
| Projects | `<data-dir>/homespun-data.json` | Fully isolated per instance |
| Pull requests | `<data-dir>/homespun-data.json` | Fully isolated per instance |
| Agent sessions | In-memory + container-scoped | Fully isolated per instance |
| Fleece issues | `.fleece/` in each git clone | Shared via git (by design) |
| User email setting | `<data-dir>/homespun-data.json` | Fully isolated per instance |

### Fleece issue assignment

The user email (set in Settings) is used when assigning Fleece issues. When multiple users work on the same repository, each user's Homespun instance will create and assign issues using their configured email. Since Fleece issues are stored in the git repository, all users see the same issues after syncing.

### Agent session ownership

Agent sessions (Claude Code sessions for Plan and Build operations) are scoped to the container that created them. Each user's instance manages its own agent sessions independently.

## Access control

Homespun does not implement authentication or role-based access control at the application level. Access control is managed through deployment isolation:

| Concern | How it's handled |
|---|---|
| **Who can access the UI** | Network-level — restrict access to each instance's port via firewall rules, VPN (Tailscale), or reverse proxy authentication |
| **What repos a user can access** | GitHub token scope — each user's PAT determines which repositories they can interact with |
| **What a user can modify** | Full access — within their instance, users have unrestricted access to all features |
| **Shared resources** | Git repositories are shared via GitHub; Fleece issues within repos are visible to all collaborators |

### Securing instances

For deployments accessible beyond localhost:

1. **Use Tailscale** — Each instance can run with a Tailscale sidecar for VPN-based access (see `run.sh --no-tailscale` to disable)
2. **Use a reverse proxy** — Place nginx or Caddy in front of each instance with authentication (basic auth, OAuth proxy, etc.)
3. **Restrict network access** — Use firewall rules to limit which hosts can reach each instance's port

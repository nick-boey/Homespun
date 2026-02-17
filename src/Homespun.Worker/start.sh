#!/bin/bash
set -e

# Homespun Worker Container Startup Script
# This script handles:
# 1. Git identity configuration
# 2. GitHub token resolution and git credential setup
# 3. Starting the Node.js worker application

# Ensure HOME is set correctly
if [ "$(id -u)" = "0" ]; then
    export HOME=/root
else
    export HOME=/home/homespun
fi

# Configure git to trust mounted directories (avoids "dubious ownership" errors)
git config --global --add safe.directory '*' 2>/dev/null || true

# Configure git identity for commits
git config --global user.name "${GIT_AUTHOR_NAME:-Homespun Bot}" 2>/dev/null || true
git config --global user.email "${GIT_AUTHOR_EMAIL:-homespun@localhost}" 2>/dev/null || true

# Resolve GitHub token from multiple sources:
# - GITHUB_TOKEN (standard, set by Bicep or Docker passthrough)
# - GitHub__Token (ASP.NET Core config style, set by Azure Container Apps Bicep)
if [ -z "$GITHUB_TOKEN" ] && [ -n "$GitHub__Token" ]; then
    export GITHUB_TOKEN="$GitHub__Token"
fi

# Also set GH_TOKEN for the gh CLI (it prefers GH_TOKEN over GITHUB_TOKEN)
if [ -n "$GITHUB_TOKEN" ] && [ -z "$GH_TOKEN" ]; then
    export GH_TOKEN="$GITHUB_TOKEN"
fi

# Configure git credentials using askpass if we have a GitHub token
if [ -n "$GITHUB_TOKEN" ]; then
    ASKPASS_SCRIPT="$HOME/git-askpass.sh"
    cat > "$ASKPASS_SCRIPT" << 'ASKPASS_EOF'
#!/bin/sh
echo "$GITHUB_TOKEN"
ASKPASS_EOF
    chmod +x "$ASKPASS_SCRIPT"

    git config --global core.askpass "$ASKPASS_SCRIPT" 2>/dev/null || true
    git config --global credential.helper '' 2>/dev/null || true
fi

# Ensure required .claude subdirectories exist in mounted volume
# The mounted host directory may not have these, and the Claude SDK expects them
mkdir -p "$HOME/.claude/debug" "$HOME/.claude/todos" "$HOME/.claude/projects" "$HOME/.claude/statsig" "$HOME/.claude/plans" 2>/dev/null || true

echo "Starting Homespun Worker..."
exec node dist/index.js

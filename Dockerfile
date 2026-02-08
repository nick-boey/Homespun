# Homespun Dockerfile
# Multi-stage build for .NET 10 Blazor Server application
# Includes: git, gh CLI, and Fleece issue tracking tools
#
# Environment Variables (passed at runtime via scripts/run.sh):
#   GITHUB_TOKEN              - GitHub personal access token for PR operations
#   CLAUDE_CODE_OAUTH_TOKEN   - Claude Code OAuth token for authentication
#   TAILSCALE_AUTH_KEY        - Tailscale auth key for VPN access (optional)

# ARG before any FROM so it's available in the FROM instruction below
ARG BASE_IMAGE=homespun-base:local

# =============================================================================
# Stage 1: Build
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install Node.js (required for Tailwind CSS build during dotnet publish)
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    gnupg \
    && curl -fsSL https://deb.nodesource.com/setup_lts.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Copy solution and project files first for better layer caching
COPY Homespun.sln ./
COPY src/Homespun/Homespun.csproj src/Homespun/
COPY src/Homespun.AgentWorker/Homespun.AgentWorker.csproj src/Homespun.AgentWorker/
COPY src/Homespun.ClaudeAgentSdk/Homespun.ClaudeAgentSdk.csproj src/Homespun.ClaudeAgentSdk/
COPY tests/Homespun.Tests/Homespun.Tests.csproj tests/Homespun.Tests/
COPY tests/Homespun.Api.Tests/Homespun.Api.Tests.csproj tests/Homespun.Api.Tests/
COPY tests/Homespun.E2E.Tests/Homespun.E2E.Tests.csproj tests/Homespun.E2E.Tests/

# Restore dependencies
RUN dotnet restore

# Cache-busting build argument
ARG CACHEBUST=1
ARG VERSION=1.0.0
ARG BUILD_CONFIGURATION=Release

# Copy everything else
COPY . .

# Install npm dependencies for Tailwind CSS build
# (node_modules is excluded by .dockerignore, so we must install here)
# Use npm ci for clean, reproducible installs from package-lock.json
RUN cd src/Homespun && rm -rf node_modules && npm ci

# Build and publish
# Note: Cannot use --no-restore here because Blazor framework files
# (blazor.web.js, etc.) are in an implicit package that's only resolved during publish
RUN dotnet publish src/Homespun/Homespun.csproj \
    -c $BUILD_CONFIGURATION \
    /p:Version=$VERSION \
    -o /app/publish

# =============================================================================
# Stage 2: Runtime
# =============================================================================
# Derives from shared base image (Dockerfile.base) which includes:
#   .NET 10 SDK, Node.js, gh CLI, Claude Code, Playwright MCP + Chromium,
#   Fleece CLI, Docker CLI
# Local default: homespun-base:local (built by scripts/run.sh)
# CI override: ghcr.io/<repo>-base:latest (passed via --build-arg)
FROM ${BASE_IMAGE} AS runtime
WORKDIR /app

# Install Tailscale for VPN access (main app only)
RUN curl -fsSL https://pkgs.tailscale.com/stable/debian/bookworm.noarmor.gpg | tee /usr/share/keyrings/tailscale-archive-keyring.gpg >/dev/null \
    && curl -fsSL https://pkgs.tailscale.com/stable/debian/bookworm.tailscale-keyring.list | tee /etc/apt/sources.list.d/tailscale.list \
    && apt-get update \
    && apt-get install -y tailscale \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN useradd --create-home --shell /bin/bash homespun

# Create data directory
RUN mkdir -p /data \
    && chown -R homespun:homespun /data

# Make home directory accessible to any runtime user
# This is needed because docker-compose may override the runtime user (HOST_UID/HOST_GID)
# for proper file ownership on mounted volumes, but HOME still points to /home/homespun
# Also create .claude directory structure for Claude Code runtime data (todos, debug, sessions)
RUN chmod 777 /home/homespun \
    && mkdir -p /home/homespun/.local/share /home/homespun/.config /home/homespun/.cache \
    && mkdir -p /home/homespun/.claude/todos /home/homespun/.claude/debug /home/homespun/.claude/projects /home/homespun/.claude/statsig \
    && chmod -R 777 /home/homespun/.local /home/homespun/.config /home/homespun/.cache /home/homespun/.claude

# Copy published application
COPY --from=build /app/publish .

# Copy test session data for mock mode
# MockDataSeederService loads these from /app/test-sessions when HOMESPUN_MOCK_MODE=true
# Note: We use /app/test-sessions instead of /data/sessions because /data is mounted
# as a volume at runtime, which would hide any files copied during build
COPY --from=build /src/tests/data/sessions /app/test-sessions
RUN chown -R homespun:homespun /app/test-sessions

# Copy start script
COPY src/Homespun/start.sh .
RUN chmod +x start.sh

# Set ownership
RUN chown -R homespun:homespun /app

# Switch to non-root user
USER homespun

# Configure environment
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV HOMESPUN_DATA_PATH=/data/homespun-data.json
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false
ENV PATH="${PATH}:/root/.dotnet/tools"
ENV SignalR__InternalBaseUrl=http://localhost:8080

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set entrypoint
ENTRYPOINT ["./start.sh"]

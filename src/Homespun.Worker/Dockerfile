# syntax=docker/dockerfile:1
# =============================================================================
# Homespun Worker Container Image
# =============================================================================
# Extends the shared base image with worker-specific tooling:
#   - .NET SDKs 8 and 9 (base has 10)
#   - C# Language Server for LSP support
#   - Playwright 1.58.2 browsers for E2E tests
# =============================================================================

# ARG before any FROM so it's available in the FROM instruction below
ARG BASE_IMAGE=homespun-base:local

# Build stage
FROM node:22-bookworm-slim AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY tsconfig.json ./
COPY src/ src/
RUN npm run build

# Runtime stage - use shared base image for common tooling
# Base image provides: .NET 10 SDK, Node.js, gh CLI, Claude Code, Playwright MCP,
#                      Fleece CLI, Docker CLI, Blazor Blueprint MCP
FROM ${BASE_IMAGE}
WORKDIR /app

# Build argument for version (set by CD workflow via Nerdbank.GitVersioning)
ARG VERSION=0.0.0-dev
ENV APP_VERSION=${VERSION}

# Install .NET SDKs 8 and 9 (base image has 10)
# Worker needs multiple SDK versions for building various projects
ENV DOTNET_ROOT=/usr/share/dotnet
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 8.0 --install-dir $DOTNET_ROOT \
    && /tmp/dotnet-install.sh --channel 9.0 --install-dir $DOTNET_ROOT \
    && rm /tmp/dotnet-install.sh
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Install C# Language Server for LSP support (not in base image)
RUN dotnet tool install csharp-ls --tool-path /usr/local/bin

# Install C# LSP plugin for Claude Code
RUN claude plugin marketplace add anthropics/claude-plugins-official \
    && claude plugin install csharp-lsp@claude-plugins-official

# Install Playwright browsers for E2E tests
# The Node.js @playwright/mcp in base image installs browsers for its version, but E2E tests use
# @playwright/test 1.58.2 which requires chromium revision 1208. Use the matching
# Node.js playwright version to download the correct browsers.
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/playwright-browsers
RUN --mount=type=cache,target=/root/.npm \
    npx playwright@1.58.2 install chromium --with-deps \
    && chmod -R 777 /opt/playwright-browsers

# Copy built app and production dependencies
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./

# Copy startup script
COPY start.sh ./
RUN chmod +x start.sh

# Create non-root user for security (don't force UID to avoid conflicts with base image)
RUN useradd --create-home --shell /bin/bash homespun

# Make home directory accessible to any runtime user and create data directories
# Docker containers run with --user UID:GID (from DockerAgentExecutionService/ProcessUserInfo)
# that may differ from the homespun user's UID, so /home/homespun must be world-writable
RUN chmod 777 /home/homespun \
    && mkdir -p /home/homespun/.local/share /home/homespun/.config /home/homespun/.cache \
    && mkdir -p /home/homespun/.claude/todos /home/homespun/.claude/debug /home/homespun/.claude/projects /home/homespun/.claude/statsig /home/homespun/.claude/plans \
    && mkdir -p /workdir \
    && chmod -R 777 /home/homespun/.local /home/homespun/.config /home/homespun/.cache /home/homespun/.claude

# Copy Claude plugins and config from root user (installed by claude commands above)
RUN cp -r /root/.claude/plugins /home/homespun/.claude/plugins 2>/dev/null || true \
    && cp -r /root/.mcp.json /home/homespun/.mcp.json 2>/dev/null || true

# Set ownership of all directories
RUN chown -R homespun:homespun /home/homespun /workdir /app

USER homespun
ENV HOME=/home/homespun
ENV PORT=8080

EXPOSE 8080

ENTRYPOINT ["./start.sh"]

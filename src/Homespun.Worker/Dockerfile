# Build stage
FROM node:22-bookworm-slim AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY tsconfig.json ./
COPY src/ src/
RUN npm run build

# Runtime stage
FROM node:22-bookworm-slim
WORKDIR /app

# Install git, curl, and other essentials (including .NET runtime dependencies)
RUN apt-get update && apt-get install -y --no-install-recommends \
    git curl ca-certificates gnupg sudo \
    libicu72 libssl3 zlib1g \
    && rm -rf /var/lib/apt/lists/*

# Install .NET SDKs 8, 9, and 10 via official install script
ENV DOTNET_ROOT=/usr/share/dotnet
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 8.0 --install-dir $DOTNET_ROOT \
    && /tmp/dotnet-install.sh --channel 9.0 --install-dir $DOTNET_ROOT \
    && /tmp/dotnet-install.sh --channel 10.0 --install-dir $DOTNET_ROOT \
    && rm /tmp/dotnet-install.sh \
    && ln -sf $DOTNET_ROOT/dotnet /usr/local/bin/dotnet
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Install Fleece CLI for issue tracking
RUN dotnet tool install Fleece.Cli --tool-path /usr/local/bin

# GitHub CLI
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
      | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
      > /etc/apt/sources.list.d/github-cli.list \
    && apt-get update && apt-get install -y gh && rm -rf /var/lib/apt/lists/*

# Claude Code CLI + Playwright MCP + Chromium
RUN npm install -g @anthropic-ai/claude-code @playwright/mcp@latest
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/playwright-browsers
RUN npx playwright install chromium --with-deps \
    && chmod -R 777 /opt/playwright-browsers

# Copy built app and production dependencies
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./

# Copy startup script
COPY start.sh ./
RUN chmod +x start.sh

# Non-root user (UID 1000 to match the main container's homespun user)
# The base node image has a 'node' user at UID 1000 — remove it first
RUN userdel node \
    && useradd --create-home --shell /bin/bash --uid 1000 homespun \
    && mkdir -p /home/homespun/.claude/todos /home/homespun/.claude/debug /home/homespun/.claude/projects /home/homespun/.claude/statsig /home/homespun/.claude/plans \
    && mkdir -p /home/homespun/.local/share /home/homespun/.config /home/homespun/.cache \
    && mkdir -p /workdir \
    && chown -R homespun:homespun /home/homespun /workdir /app \
    && chmod -R 777 /home/homespun/.local /home/homespun/.config /home/homespun/.cache /home/homespun/.claude

USER homespun
ENV HOME=/home/homespun
ENV PORT=8080

EXPOSE 8080

ENTRYPOINT ["./start.sh"]

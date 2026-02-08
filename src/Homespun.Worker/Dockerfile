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

# Install git, curl, and other essentials
RUN apt-get update && apt-get install -y --no-install-recommends \
    git curl ca-certificates gnupg sudo \
    && rm -rf /var/lib/apt/lists/*

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

# Non-root user
RUN useradd --create-home --shell /bin/bash homespun \
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

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/Homespun.AgentWorker/Homespun.AgentWorker.csproj", "Homespun.AgentWorker/"]
COPY ["src/Homespun.ClaudeAgentSdk/Homespun.ClaudeAgentSdk.csproj", "Homespun.ClaudeAgentSdk/"]

# Restore dependencies
RUN dotnet restore "Homespun.AgentWorker/Homespun.AgentWorker.csproj"

# Copy source code
COPY ["src/Homespun.AgentWorker/", "Homespun.AgentWorker/"]
COPY ["src/Homespun.ClaudeAgentSdk/", "Homespun.ClaudeAgentSdk/"]

# Build
WORKDIR "/src/Homespun.AgentWorker"
RUN dotnet build -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
# Use SDK image instead of aspnet runtime to support building projects
# that need to run dotnet build, test, and other SDK commands via Bash tool
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime
WORKDIR /app

# Install dependencies: Node.js, git, and other tools for Claude Code
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    gnupg \
    git \
    ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_lts.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install GitHub CLI
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | tee /etc/apt/sources.list.d/github-cli.list > /dev/null \
    && apt-get update \
    && apt-get install -y gh \
    && rm -rf /var/lib/apt/lists/*

# Install build dependencies for native npm packages (node-pty requires compilation)
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    python3-setuptools \
    && rm -rf /var/lib/apt/lists/*

# Install Claude Code globally
RUN npm install -g @anthropic-ai/claude-code

# Install Playwright MCP and Chromium browser with all dependencies
# Install browsers to /opt/playwright-browsers with world-writable permissions
# This is required because the container runs with --user $HOST_UID:$HOST_GID
# and the default location (/root/.cache/ms-playwright) is not accessible to non-root users
# Permissions must be 777 to allow Playwright to create lock files and temp directories
# IMPORTANT: Use the playwright CLI bundled with @playwright/mcp to ensure browser version matches
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/playwright-browsers
RUN npm install -g @playwright/mcp@latest \
    && /usr/lib/node_modules/@playwright/mcp/node_modules/.bin/playwright install chromium --with-deps \
    && chmod -R 777 /opt/playwright-browsers

# Install Fleece CLI for issue tracking
# Install as root, then copy to /usr/local/bin for universal access
# Note: Symlinks don't work because /root/.dotnet/tools/ is inaccessible to non-root users
RUN dotnet tool install Fleece.Cli -g \
    && cp /root/.dotnet/tools/fleece /usr/local/bin/fleece \
    && chmod 755 /usr/local/bin/fleece

# Clean up build dependencies to reduce image size
RUN apt-get update && apt-get remove -y build-essential && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

# Install Docker CLI for DooD (Docker outside of Docker)
# This allows the container to communicate with the host's Docker daemon
# via a mounted /var/run/docker.sock socket
RUN curl -fsSL https://download.docker.com/linux/debian/gpg | gpg --dearmor -o /usr/share/keyrings/docker-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/debian bookworm stable" | tee /etc/apt/sources.list.d/docker.list > /dev/null \
    && apt-get update \
    && apt-get install -y docker-ce-cli \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user matching main Homespun container
RUN useradd --create-home --shell /bin/bash homespun

# Create data directory with proper ownership
RUN mkdir -p /data \
    && chown -R homespun:homespun /data

# Make home directory accessible to any runtime user
# This is needed because the container may run with host UID/GID for file ownership
# Also create .claude directory structure for Claude Code runtime data
RUN chmod 777 /home/homespun \
    && mkdir -p /home/homespun/.local/share /home/homespun/.config /home/homespun/.cache \
    && mkdir -p /home/homespun/.claude/todos /home/homespun/.claude/debug /home/homespun/.claude/projects /home/homespun/.claude/statsig \
    && chmod -R 777 /home/homespun/.local /home/homespun/.config /home/homespun/.cache /home/homespun/.claude

# Configure git to trust mounted directories
RUN git config --global --add safe.directory '*'

COPY --from=publish /app/publish .

# Set ownership
RUN chown -R homespun:homespun /app

# Switch to non-root user
USER homespun

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV HOME=/home/homespun
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false

EXPOSE 8080

ENTRYPOINT ["dotnet", "Homespun.AgentWorker.dll"]

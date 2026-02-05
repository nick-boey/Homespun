# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
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
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS runtime
WORKDIR /app

# Install dependencies: Node.js, git, and other tools for Claude CLI
RUN apt-get update && apt-get install -y \
    curl \
    gnupg \
    git \
    ca-certificates \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_20.x nodistro main" > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install Claude CLI globally
RUN npm install -g @anthropic-ai/claude-code

# Install Playwright browsers
# Install browsers to /opt/playwright-browsers with world-writable permissions
# This allows any runtime user (matched to host UID/GID) to use the browsers
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/playwright-browsers
RUN npx playwright install --with-deps chromium \
    && chmod -R 777 /opt/playwright-browsers

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

EXPOSE 8080

ENTRYPOINT ["dotnet", "Homespun.AgentWorker.dll"]

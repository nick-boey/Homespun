# Build stage
ARG BASE_IMAGE=homespun-base:local
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
# Derives from shared base image (Dockerfile.base) which includes:
#   .NET 10 SDK, Node.js, gh CLI, Claude Code, Playwright MCP + Chromium,
#   Fleece CLI, Docker CLI
# Local default: homespun-base:local (built by scripts/run.sh)
# CI override: ghcr.io/<repo>-base:latest (passed via --build-arg)
FROM ${BASE_IMAGE} AS runtime
WORKDIR /app

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

COPY --from=publish /app/publish .

# Copy startup script
COPY ["src/Homespun.AgentWorker/start.sh", "/app/start.sh"]
RUN chmod +x /app/start.sh

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

ENTRYPOINT ["./start.sh"]

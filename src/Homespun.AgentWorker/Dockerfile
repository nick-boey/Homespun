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
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Install Node.js for Playwright MCP and Claude CLI
RUN apt-get update && apt-get install -y \
    curl \
    gnupg \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_20.x nodistro main" > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# Install Claude CLI globally
RUN npm install -g @anthropic-ai/claude-code

# Install Playwright browsers
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/playwright-browsers
RUN npx playwright install --with-deps chromium

# Create non-root user
RUN useradd -m -s /bin/bash worker
USER worker

COPY --from=publish /app/publish .

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "Homespun.AgentWorker.dll"]

# =============================================================================
# Worker Container Dockerfile
# =============================================================================
# Uses the shared base image (Dockerfile.base) for runtime tooling.
# Build with: docker build -t homespun-worker:local --build-arg BASE_IMAGE=homespun-base:local -f src/Homespun.Worker/Dockerfile src/Homespun.Worker
# =============================================================================

# Base image argument - allows overriding for local builds vs CI
ARG BASE_IMAGE=homespun-base:local

# Build stage - compile TypeScript
FROM node:22-bookworm-slim AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY tsconfig.json ./
COPY src/ src/
RUN npm run build

# Runtime stage - uses shared base image with all tooling pre-installed
FROM ${BASE_IMAGE}
WORKDIR /app

# Copy built app and production dependencies
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./

# Copy startup script
COPY start.sh ./
RUN chmod +x start.sh

# Non-root user (UID 1000 to match the main container's homespun user)
# The base image uses .NET SDK which doesn't have a conflicting user at UID 1000
RUN useradd --create-home --shell /bin/bash --uid 1000 homespun \
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

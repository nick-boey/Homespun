# Build stage
FROM node:22-alpine AS build

WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY . .

# Build the application
RUN npm run build

# Production stage
FROM nginx:alpine AS production

# Copy built assets from build stage
COPY --from=build /app/dist /usr/share/nginx/html

# Copy nginx config template — the nginx:alpine entrypoint runs envsubst on
# files in /etc/nginx/templates/ at container start, substituting only the
# UPSTREAM_* variables (so nginx's own $vars like $uri / $host stay literal).
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
ENV NGINX_ENVSUBST_FILTER="^UPSTREAM_"
ENV UPSTREAM_HOST=homespun
ENV UPSTREAM_PORT=8080

# Expose port
EXPOSE 80

# Start nginx
CMD ["nginx", "-g", "daemon off;"]

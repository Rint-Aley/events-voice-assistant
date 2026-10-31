# Build static assets
FROM node:22-alpine AS build

WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite embeds the value at build time; "/api/v1" is proxied by nginx to TaskManager.
ARG VITE_API_BASE_URL=/api/v1
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

# Serve static files and proxy /api/ to TaskManager
FROM nginx:stable-alpine AS runtime

COPY default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

# Resolved at request time, so nginx starts even if TaskManager is not up yet.
ENV TASKMANAGER_URL=http://taskmanager:8080

EXPOSE 80

HEALTHCHECK --interval=10s --timeout=3s --retries=3 \
  CMD wget -qO- http://127.0.0.1/healthz >/dev/null || exit 1

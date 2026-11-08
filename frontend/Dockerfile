FROM node:20-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm install

COPY . .
ARG VITE_API_URL=http://localhost:8080
ENV VITE_API_URL=$VITE_API_URL
RUN npm run build

FROM nginx:alpine AS final
COPY --from=build /app/dist /usr/share/nginx/html

# Custom nginx config to support Single Page Application routing
RUN echo 'server { \
    listen 3000; \
    location / { \
        root /usr/share/nginx/html; \
        index index.html index.htm; \
        try_files $uri $uri/ /index.html; \
    } \
}' > /etc/nginx/conf.d/default.conf

# Inject runtime environment variables into /usr/share/nginx/html/env-config.js at container startup
RUN echo '#!/bin/sh' > /docker-entrypoint.d/40-env-config.sh && \
    echo 'API_URL="${VITE_API_URL:-${API_URL:-${BACKEND_URL:-http://localhost:8080}}}"' >> /docker-entrypoint.d/40-env-config.sh && \
    echo 'echo "window.__ENV__ = { VITE_API_URL: \"$API_URL\" };" > /usr/share/nginx/html/env-config.js' >> /docker-entrypoint.d/40-env-config.sh && \
    chmod +x /docker-entrypoint.d/40-env-config.sh

EXPOSE 3000
CMD ["nginx", "-g", "daemon off;"]

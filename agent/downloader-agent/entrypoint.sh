#!/bin/sh
set -e
# Ensure /app/downloads exists and is owned by the app user so the agent can read/write and serve files.
# When a volume is mounted at /app/downloads, Docker often creates it as root; we fix that at startup.
mkdir -p /app/downloads
chown -R "${APP_UID:-1000}:${APP_GID:-1000}" /app/downloads
exec gosu "${APP_UID:-1000}" "$@"

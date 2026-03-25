#!/bin/sh
set -e
# Ensure download dir exists. chown fixes root-owned Docker volume mounts on normal Linux fs;
# on exFAT/vfat it fails harmlessly — those fs use mount uid/gid instead of POSIX ownership.
mkdir -p /app/downloads
chown -R "${APP_UID:-1000}:${APP_GID:-1000}" /app/downloads 2>/dev/null || true

# exFAT/FAT32: host mount often maps files to root or a fixed uid. If writes fail as APP_UID,
# set AGENT_RUN_AS_ROOT=1, or remount the stick with uid=1000,gid=1000 (preferred).
if [ "${AGENT_RUN_AS_ROOT:-}" = "1" ] || [ "${AGENT_RUN_AS_ROOT:-}" = "true" ]; then
  exec "$@"
fi

exec gosu "${APP_UID:-1000}" "$@"

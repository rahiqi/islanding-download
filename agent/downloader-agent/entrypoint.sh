#!/bin/sh
set -e
# Resolve AGENT_DOWNLOAD_PATH like the app does (relative -> /app/<path>).
DOWNLOAD_PATH="${AGENT_DOWNLOAD_PATH:-downloads}"
case "$DOWNLOAD_PATH" in
  /*) TARGET_PATH="$DOWNLOAD_PATH" ;;
  *) TARGET_PATH="/app/$DOWNLOAD_PATH" ;;
esac

# Ensure target exists. chown fixes normal Linux fs ownership; on exFAT/vfat it is ignored.
mkdir -p "$TARGET_PATH"
chown -R "${APP_UID:-1000}:${APP_GID:-1000}" "$TARGET_PATH" 2>/dev/null || true

# exFAT/FAT32: host mount often maps files to root or a fixed uid. If writes fail as APP_UID,
# set AGENT_RUN_AS_ROOT=1, or remount with uid=1000,gid=1000 (preferred).
if [ "${AGENT_RUN_AS_ROOT:-}" = "1" ] || [ "${AGENT_RUN_AS_ROOT:-}" = "true" ]; then
  exec "$@"
fi

APP_UID_VALUE="${APP_UID:-1000}"
APP_GID_VALUE="${APP_GID:-1000}"
WRITE_TEST="$TARGET_PATH/.write-test.$$"

# Probe actual write permission as the app uid; if it fails, auto-fallback to root.
if gosu "$APP_UID_VALUE:$APP_GID_VALUE" sh -c "touch \"$WRITE_TEST\" && rm -f \"$WRITE_TEST\""; then
  exec gosu "$APP_UID_VALUE:$APP_GID_VALUE" "$@"
fi

echo "Warning: user ${APP_UID_VALUE}:${APP_GID_VALUE} cannot write to $TARGET_PATH; falling back to root." >&2
exec "$@"

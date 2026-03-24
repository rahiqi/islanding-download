##!/bin/sh
set -e
## Ensure /app/downloads exists and is owned by the app user so the agent can read/write and serve files.
## When a volume is mounted at /app/downloads, Docker often creates it as root; we fix that at startup.
#mkdir -p /app/downloads
#chown -R "${APP_UID:-1000}:${APP_GID:-1000}" /app/downloads
#exec gosu "${APP_UID:-1000}" "$@"

#!/bin/sh
set -e

mkdir -p /app/downloads

# Only attempt chown on filesystems that support it
FS_TYPE=$(stat -f -c %T /app/downloads)

case "$FS_TYPE" in
  exfat|ntfs|fuseblk)
    echo "Skipping chown on unsupported filesystem: $FS_TYPE"
    ;;
  *)
    echo "Applying ownership..."
    chown -R "${APP_UID:-1000}:${APP_GID:-1000}" /app/downloads
    ;;
esac

exec gosu "${APP_UID:-1000}" "$@"
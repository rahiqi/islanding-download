#!/usr/bin/env sh
# Run docker compose for the agent. From repo root:
#   ./agent/compose.sh up -d          # main (64-bit)
#   BUILD_ARM32=1 ./agent/compose.sh up -d   # Raspberry Pi 32-bit

set -e
cd "$(dirname "$0")/.."

if [ "$BUILD_ARM32" = "1" ] || [ "$BUILD_ARM32" = "true" ] || [ "$BUILD_ARM32" = "yes" ]; then
  exec docker compose -f agent/docker-compose.yml -f agent/docker-compose.arm32.yml "$@"
else
  exec docker compose -f agent/docker-compose.yml "$@"
fi

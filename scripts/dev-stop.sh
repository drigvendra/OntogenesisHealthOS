#!/usr/bin/env bash
# Stops core-api and admin-web. --all also stops the container stack (data is kept in volumes).
set -uo pipefail
cd "$(dirname "$0")/.."

for app in core-api admin-web; do
  if [ -f ".run/$app.pid" ]; then
    kill "$(cat ".run/$app.pid")" 2>/dev/null || true
    rm -f ".run/$app.pid"
  fi
done
pkill -f "HealthOS.CoreApi.dll" 2>/dev/null || true
pkill -f "vite" 2>/dev/null || true

if [ "${1:-}" = "--all" ]; then
  docker compose --env-file .env -f deploy/local/compose.yaml down
fi
echo "Stopped."

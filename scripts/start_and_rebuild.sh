#!/usr/bin/env bash
set -euo pipefail

# Resolve script directory and project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$SCRIPT_DIR/.."
cd "$ROOT_DIR"

echo "Building 'app' image..."
docker compose build app

echo "Recreating 'app' container (no dependencies)..."
docker compose up -d --force-recreate --no-deps app

echo "Starting any stopped services (so all containers are running)..."
docker compose start || true

echo "Waiting for http://localhost:5000/health to return success..."
MAX_ATTEMPTS=30
ATTEMPT=0
while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
  if curl -fsS http://localhost:5000/health >/dev/null 2>&1; then
    echo "\nApp is healthy."
    break
  fi
  ATTEMPT=$((ATTEMPT + 1))
  printf "."
  sleep 1
done

if [ $ATTEMPT -ge $MAX_ATTEMPTS ]; then
  echo "\nTimed out waiting for app health. Check logs with: docker compose logs --tail=200 app"
else
  ADMIN_URL="http://localhost:5000/admin.html"
  echo "Opening admin UI at: $ADMIN_URL"
  if command -v xdg-open >/dev/null 2>&1; then
    xdg-open "$ADMIN_URL" || true
  elif command -v open >/dev/null 2>&1; then
    open "$ADMIN_URL" || true
  else
    echo "$ADMIN_URL"
  fi
fi

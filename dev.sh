#!/usr/bin/env bash
#
# Starts the backend API and the frontend dev server together.
# Press Ctrl-C once to stop both.
#
#   ./dev.sh
#
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Kill the whole process group (both servers) when this script exits.
trap 'echo; echo "Stopping…"; kill 0' EXIT INT TERM

# 1. Frontend dependencies — install on first run.
if [ ! -d "$ROOT_DIR/frontend/node_modules" ]; then
  echo "Installing frontend dependencies…"
  (cd "$ROOT_DIR/frontend" && npm install)
fi
if [ ! -f "$ROOT_DIR/frontend/.env" ]; then
  cp "$ROOT_DIR/frontend/.env.example" "$ROOT_DIR/frontend/.env"
fi
if [ ! -f "$ROOT_DIR/backend/src/TripPlanner.WebApi/.env" ]; then
  cp "$ROOT_DIR/backend/src/TripPlanner.WebApi/.env.example" "$ROOT_DIR/backend/src/TripPlanner.WebApi/.env"
fi

# 2. Start the backend API (http://localhost:5080).
echo "Starting backend API…"
(cd "$ROOT_DIR/backend" && dotnet run --project src/TripPlanner.WebApi) &

# 3. Start the frontend dev server (http://localhost:5173).
echo "Starting frontend…"
(cd "$ROOT_DIR/frontend" && npm run dev) &

echo
echo "  API:      http://localhost:5080/swagger"
echo "  Frontend: http://localhost:5173"
echo "  (Ctrl-C to stop both)"
echo

# Wait for either process; the trap cleans up the rest.
wait

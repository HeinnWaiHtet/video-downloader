#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SOLUTION="$ROOT_DIR/Downloader.sln"
HOST_PROJECT="$ROOT_DIR/Downloader.Host/Downloader.Host.csproj"
UI_PROJECT="$ROOT_DIR/Downloader.UI/Downloader.UI.csproj"
TEST_PROJECT="$ROOT_DIR/Downloader.Tests/Downloader.Tests.csproj"

HOST_PID=""
UI_PID=""

cleanup() {
  set +e
  if [[ -n "$HOST_PID" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
  fi

  if [[ -n "$UI_PID" ]] && kill -0 "$UI_PID" 2>/dev/null; then
    kill "$UI_PID" 2>/dev/null || true
  fi
}

trap cleanup EXIT INT TERM

echo "==> Building solution"
dotnet build "$SOLUTION" --no-restore --nologo

echo "==> Running tests"
dotnet run --project "$TEST_PROJECT" --no-build

echo "==> Starting Downloader.Host"
dotnet run --project "$HOST_PROJECT" --no-build &
HOST_PID=$!

echo "==> Starting Downloader.UI"
dotnet run --project "$UI_PROJECT" --no-build &
UI_PID=$!

echo
echo "Smoke environment is running."
echo "- Host PID: $HOST_PID"
echo "- UI PID:   $UI_PID"
echo "Open browser, test Download button, then press Ctrl+C to stop."

wait "$HOST_PID" "$UI_PID"

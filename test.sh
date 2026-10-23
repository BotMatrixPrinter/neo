#!/usr/bin/env bash
# Repo test: Core unit tests, Godot project build, headless main-scene load.
# Offline: packages come only from $NUGET_PACKAGES (see nuget.config).
set -euo pipefail
cd "$(dirname "$0")"

echo "== restore (offline)"
dotnet restore Neo.sln --nologo -v q

echo "== Core tests"
dotnet test tests/Neo.Core.Tests --no-restore --nologo -v q

echo "== build Godot project"
dotnet build src/Neo.Game --no-restore --nologo -v q
if ! grep -q '"tfm": "net10.0"' src/Neo.Game/.godot/mono/temp/bin/Debug/Neo.Game.runtimeconfig.json; then
  echo "FAIL: Godot project does not target net10.0"; exit 1
fi

echo "== load main scene headless"
log=$(mktemp)
trap 'rm -f "$log"' EXIT
status=0
timeout 60 godot --headless --path src/Neo.Game --quit-after 2 >"$log" 2>&1 || status=$?
cat "$log"
if [ "$status" -ne 0 ]; then
  echo "FAIL: godot exited with $status"; exit 1
fi
if grep -E -q '(^|[^A-Z_])(ERROR|SCRIPT ERROR|USER ERROR):' "$log"; then
  echo "FAIL: godot reported errors"; exit 1
fi
if ! grep -q 'NEO_READY' "$log"; then
  echo "FAIL: main scene did not run its C# (no NEO_READY)"; exit 1
fi
echo "== all passed"

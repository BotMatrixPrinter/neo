#!/usr/bin/env bash
# Repo test.
#   ./test.sh          every module's tests and the shell's, Godot build, headless main-scene load.
#   ./test.sh <Name>   tests of module <Name> and of modules whose projects reference
#                      <Name>'s Contract, then the same Godot build and headless load.
# Works offline once the pinned packages are in the NuGet global packages folder (see nuget.config).
set -euo pipefail
cd "$(dirname "$0")"

module="${1:-}"
tests=()
if [ -z "$module" ]; then
  tests+=(tests/*/*.csproj modules/*/Tests/*.csproj)
else
  if ! [[ "$module" =~ ^[A-Za-z0-9_]+$ ]] || [ ! -d "modules/$module" ]; then
    echo "FAIL: no module modules/$module"; exit 1
  fi
  tests+=(modules/"$module"/Tests/*.csproj)
  # Modules that use <Name>'s Contract: their tests must still pass.
  for dir in modules/*/; do
    other=$(basename "$dir")
    [ "$other" = "$module" ] && continue
    if grep -E -q "(^|[\"/\\\\])$module[/\\\\]Contract[/\\\\]" "$dir"*/*.csproj 2>/dev/null; then
      echo "   $other uses the $module Contract"
      tests+=("$dir"Tests/*.csproj)
    fi
  done
fi
for t in "${tests[@]}"; do
  [ -f "$t" ] || { echo "FAIL: missing test project $t"; exit 1; }
done

echo "== restore (offline)"
for p in Neo.csproj "${tests[@]}"; do
  dotnet restore "$p" --nologo -v q
done

for t in "${tests[@]}"; do
  echo "== tests: $t"
  dotnet test "$t" --no-restore --nologo -v q
done

echo "== build Godot project"
dotnet build Neo.csproj --no-restore --nologo -v q
if ! grep -q '"tfm": "net10.0"' .godot/mono/temp/bin/Debug/Neo.runtimeconfig.json; then
  echo "FAIL: Godot project does not target net10.0"; exit 1
fi

echo "== load main scene headless"
log=$(mktemp)
trap 'rm -f "$log"' EXIT
status=0
# --fixed-fps: every frame is one 1/60 s physics step, so modules really tick.
timeout 60 godot --headless --path . --fixed-fps 60 --quit-after 10 >"$log" 2>&1 || status=$?
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
# A module the shell disabled fails its own run (and the full run), not other modules' runs.
if [ -z "$module" ]; then
  checked=()
  for core in modules/*/Core/*.csproj; do
    checked+=("$(basename "$(dirname "$(dirname "$core")")")")
  done
  disabled_pattern='NEO_MODULE_DISABLED '
else
  checked=("$module")
  disabled_pattern="NEO_MODULE_DISABLED $module:"
fi
for name in "${checked[@]}"; do
  if ! grep -q "^NEO_MODULE_READY $name\$" "$log"; then
    echo "FAIL: module $name was not found and started at runtime"; exit 1
  fi
done
if grep -q "$disabled_pattern" "$log"; then
  echo "FAIL: the shell disabled a module at runtime (see NEO_MODULE_DISABLED above)"; exit 1
fi
echo "== all passed"

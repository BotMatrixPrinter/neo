#!/usr/bin/env bash
# Screenshot of a scene, rendered for real (OpenGL under Xvfb, no GPU needed).
#   ./screenshot.sh [-n] [scene] [name]
#   scene  res:// path or repo path of a .tscn (default: the main scene)
#   name   file name without .png (default: the module's name for modules/<Name>/..., else the
#          scene's file name, e.g. Main)
#   -n     only print the path it would save to
# Saves to $SCREENSHOT_DIR/<name>.png when SCREENSHOT_DIR is set (job stages: copied to the hub's
# outbox), else to ./screenshots/<name>.png (git-ignored, for local use).
# Runs the scene for $FRAMES frames (default 120 = 2 s at 60 fps) and saves the last one, 1920x1080.
# Not a test: run it after any visual change. Never commit screenshots.
set -euo pipefail
cd "$(dirname "$0")"

dry_run=0
if [ "${1:-}" = "-n" ]; then
  dry_run=1
  shift
fi
scene="${1:-}"
if [ -z "$scene" ]; then
  scene=$(sed -n 's/^run\/main_scene="\(.*\)"$/\1/p' project.godot)
fi
case "$scene" in
  res://*) ;;
  *) scene="res://${scene#./}" ;;
esac
file="${scene#res://}"
[ -f "$file" ] || { echo "FAIL: no scene $file"; exit 1; }
case "$file" in
  modules/*/*) name="${file#modules/}"; name="${name%%/*}" ;;
  *) name=$(basename "$file" .tscn) ;;
esac
name="${2:-$name}"
name="${name%.png}"
case "$name" in
  ""|*/*) echo "FAIL: bad screenshot name '$name' (a file name, no folders)"; exit 1 ;;
esac
if [ -n "${SCREENSHOT_DIR:-}" ]; then
  dir="$SCREENSHOT_DIR"
else
  dir=screenshots
fi
out="$dir/$name.png"
if [ "$dry_run" -eq 1 ]; then
  echo "$out"; exit 0
fi
frames="${FRAMES:-120}"
mkdir -p "$dir"
# Keep Godot from importing local screenshots as project assets.
[ -n "${SCREENSHOT_DIR:-}" ] || touch "$dir/.gdignore"
out_abs="$(cd "$(dirname "$out")" && pwd)/$(basename "$out")"
rm -f "$out_abs"

echo "== build Godot project"
dotnet build Neo.csproj --nologo -v q

echo "== render $scene ($frames frames) -> $out"
log=$(mktemp)
trap 'rm -f "$log"' EXIT
status=0
timeout 120 xvfb-run -a -s "-screen 0 1920x1080x24" \
  godot --path . --rendering-driver opengl3 --resolution 1920x1080 --fixed-fps 60 \
  -s res://shell/Godot/Screenshot.gd -- "$scene" "$out_abs" "$frames" >"$log" 2>&1 || status=$?
cat "$log"
if [ "$status" -ne 0 ] || ! grep -q '^NEO_SCREENSHOT .* 1920x1080$' "$log" || [ ! -s "$out_abs" ]; then
  echo "FAIL: no 1920x1080 screenshot (godot exited with $status)"; exit 1
fi
echo "== saved $out"

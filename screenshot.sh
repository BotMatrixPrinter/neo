#!/usr/bin/env bash
# Screenshot of a scene, rendered for real (OpenGL under Xvfb, no GPU needed).
#   ./screenshot.sh [scene] [out.png]
#   scene    res:// path or repo path of a .tscn (default: the main scene)
#   out.png  default docs/screenshots/<scene name>.png
# Runs the scene for $FRAMES frames (default 120 = 2 s at 60 fps) and saves the last one, 1920x1080.
# Not a test: run it after any visual change and commit the PNG.
set -euo pipefail
cd "$(dirname "$0")"

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
name=$(basename "$file" .tscn)
out="${2:-docs/screenshots/$name.png}"
frames="${FRAMES:-120}"
mkdir -p "$(dirname "$out")"
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

#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ "$(uname -s)" != Darwin ]]; then
  echo "This app requires macOS and Xcode Command Line Tools." >&2
  exit 1
fi
if [[ "$(uname -m)" != arm64 ]]; then
  echo "The initial preview targets Apple Silicon. Use an Apple Silicon Mac." >&2
  exit 1
fi
swift test
swift build -c release --arch arm64
binary_dir="$(swift build -c release --arch arm64 --show-bin-path)"
app="dist/Ansora AI.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$binary_dir/AnsoraAI" "$app/Contents/MacOS/AnsoraAI"
cp Support/Info.plist "$app/Contents/Info.plist"
iconset="dist/AppIcon.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" Support/ansora-mark.png --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" Support/ansora-mark.png --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
plutil -lint "$app/Contents/Info.plist"
identity="${SIGNING_IDENTITY:--}"
if [[ "$identity" == "-" ]]; then
  codesign --force --sign - --entitlements Support/AnsoraAI.entitlements "$app"
  echo "LOCAL PREVIEW ONLY: ad-hoc signed. Not a distributable/notarized release."
else
  codesign --force --options runtime --timestamp --sign "$identity" --entitlements Support/AnsoraAI.entitlements "$app"
fi
codesign --verify --deep --strict --verbose=2 "$app"
echo "Built $PWD/$app"
echo "Run: open \"$PWD/$app\""

#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${SIGNING_IDENTITY:?Set your Developer ID Application signing identity}"
: "${NOTARY_PROFILE:?Set a previously configured notarytool Keychain profile name}"
if [[ "$SIGNING_IDENTITY" == "-" ]]; then echo "Developer ID signing is required." >&2; exit 1; fi
bash scripts/build-app.sh
ditto -c -k --keepParent "dist/Ansora AI.app" dist/AnsoraAI-Mac-notary.zip
xcrun notarytool submit dist/AnsoraAI-Mac-notary.zip --keychain-profile "$NOTARY_PROFILE" --wait
xcrun stapler staple "dist/Ansora AI.app"
spctl --assess --type execute --verbose=2 "dist/Ansora AI.app"
ditto -c -k --keepParent "dist/Ansora AI.app" dist/AnsoraAI-Mac.zip
echo "Notarized artifact: $PWD/dist/AnsoraAI-Mac.zip"

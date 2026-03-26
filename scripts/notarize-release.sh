#!/usr/bin/env bash
#
# notarize-release.sh — Download a signed macOS DMG from a GitHub release,
# submit it for Apple notarization, poll until complete, staple the ticket,
# and re-upload the notarized DMG to the release.
#
# Supports resuming: if you kill the script or your machine restarts, re-run
# with the same tag and it picks up the existing submission instead of
# resubmitting.
#
# Prerequisites:
#   - Xcode command line tools (xcrun notarytool, xcrun stapler)
#   - gh CLI (authenticated)
#   - Environment variables or Keychain credentials for notarytool
#
# Usage:
#   ./scripts/notarize-release.sh v0.9.0-rc1
#   ./scripts/notarize-release.sh v0.9.0-rc1 --upload   # also replace release artifact
#
# Environment variables (or set in ~/.pmview-notarize.env):
#   APPLE_ID                    — Apple ID email
#   APPLE_APP_SPECIFIC_PASSWORD — App-specific password
#   APPLE_TEAM_ID               — Apple Developer Team ID

set -euo pipefail

REPO="tallpsmith/pmview-nextgen"
POLL_INTERVAL=120  # seconds between status checks
STATE_DIR="${HOME}/.pmview-notarize"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

die()  { echo "ERROR: $*" >&2; exit 1; }
info() { echo "==> $*"; }

load_env() {
  local envfile="${HOME}/.pmview-notarize.env"
  if [[ -f "$envfile" ]]; then
    info "Loading credentials from $envfile"
    # shellcheck source=/dev/null
    source "$envfile"
  fi
  [[ -n "${APPLE_ID:-}" ]]                    || die "APPLE_ID not set"
  [[ -n "${APPLE_APP_SPECIFIC_PASSWORD:-}" ]] || die "APPLE_APP_SPECIFIC_PASSWORD not set"
  [[ -n "${APPLE_TEAM_ID:-}" ]]               || die "APPLE_TEAM_ID not set"
}

notary_auth() {
  echo --apple-id "$APPLE_ID" \
       --password "$APPLE_APP_SPECIFIC_PASSWORD" \
       --team-id "$APPLE_TEAM_ID"
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

TAG="${1:-}"
UPLOAD="${2:-}"
[[ -n "$TAG" ]] || die "Usage: $0 <release-tag> [--upload]"

VERSION="${TAG#v}"
ARTIFACT="pmview-${VERSION}-macos-universal.dmg"

mkdir -p "$STATE_DIR"
STATE_FILE="${STATE_DIR}/${TAG}.json"
WORK_DIR="${STATE_DIR}/${TAG}"
mkdir -p "$WORK_DIR"

load_env

# Step 1: Download the signed DMG (skip if already downloaded)
DMG_PATH="${WORK_DIR}/${ARTIFACT}"
if [[ -f "$DMG_PATH" ]]; then
  info "DMG already downloaded: $DMG_PATH"
else
  info "Downloading ${ARTIFACT} from release ${TAG}..."
  gh release download "$TAG" \
    --repo "$REPO" \
    --pattern "$ARTIFACT" \
    --dir "$WORK_DIR"
  [[ -f "$DMG_PATH" ]] || die "Download failed — ${ARTIFACT} not found in release ${TAG}"
  info "Downloaded: $DMG_PATH"
fi

# Step 2: Submit for notarization (or resume existing submission)
if [[ -f "$STATE_FILE" ]]; then
  SUB_ID=$(python3 -c "import json; print(json.load(open('$STATE_FILE'))['id'])" 2>/dev/null || echo "")
  if [[ -n "$SUB_ID" ]]; then
    info "Resuming existing submission: $SUB_ID"
  else
    rm -f "$STATE_FILE"
  fi
fi

if [[ ! -f "$STATE_FILE" ]]; then
  info "Submitting ${ARTIFACT} for notarization..."
  xcrun notarytool submit "$DMG_PATH" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_APP_SPECIFIC_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --no-s3-acceleration \
    --output-format json \
    > "$STATE_FILE" 2>&1

  cat "$STATE_FILE"

  SUB_ID=$(python3 -c "import json; print(json.load(open('$STATE_FILE'))['id'])" 2>/dev/null || echo "")
  [[ -n "$SUB_ID" ]] || die "Submission failed — no ID returned"
  info "Submission ID: $SUB_ID"
fi

# Step 3: Poll until complete
info "Polling notarization status every ${POLL_INTERVAL}s..."
info "(You can safely kill this script and re-run to resume)"
echo ""

while true; do
  TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')
  STATUS_JSON=$(xcrun notarytool info "$SUB_ID" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_APP_SPECIFIC_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --output-format json 2>&1 || true)

  STATUS=$(echo "$STATUS_JSON" | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])" 2>/dev/null || echo "unknown")

  case "$STATUS" in
    "Accepted")
      echo ""
      info "[$TIMESTAMP] Notarization ACCEPTED!"
      break
      ;;
    "Invalid"|"Rejected")
      echo ""
      info "[$TIMESTAMP] Notarization REJECTED (status: $STATUS)"
      info "Fetching rejection log..."
      xcrun notarytool log "$SUB_ID" \
        --apple-id "$APPLE_ID" \
        --password "$APPLE_APP_SPECIFIC_PASSWORD" \
        --team-id "$APPLE_TEAM_ID" || true
      die "Notarization failed. Fix the issues above and try again."
      ;;
    "In Progress")
      printf "\r[%s] Status: In Progress — waiting %ds..." "$TIMESTAMP" "$POLL_INTERVAL"
      sleep "$POLL_INTERVAL"
      ;;
    *)
      printf "\r[%s] Status: %s — waiting %ds..." "$TIMESTAMP" "$STATUS" "$POLL_INTERVAL"
      sleep "$POLL_INTERVAL"
      ;;
  esac
done

# Step 4: Staple the notarization ticket
info "Stapling notarization ticket..."
for i in $(seq 1 10); do
  if xcrun stapler staple "$DMG_PATH"; then
    info "Stapled successfully"
    break
  fi
  if [[ $i -eq 10 ]]; then
    die "Stapling failed after 10 attempts"
  fi
  info "Staple attempt $i/10 failed — waiting 60s for CDN propagation..."
  sleep 60
done

# Step 5: Verify
info "Verifying stapled ticket..."
xcrun stapler validate "$DMG_PATH"
spctl --assess --type open --context context:primary-signature --verbose=2 "$DMG_PATH" 2>&1 || true

info ""
info "Notarized DMG: $DMG_PATH"

# Step 6: Optionally upload back to the release
if [[ "$UPLOAD" == "--upload" ]]; then
  info "Replacing release artifact with notarized DMG..."
  gh release upload "$TAG" \
    --repo "$REPO" \
    "$DMG_PATH" \
    --clobber
  info "Done — notarized DMG uploaded to release ${TAG}"
else
  info ""
  info "To replace the release artifact, run:"
  info "  gh release upload ${TAG} --repo ${REPO} '${DMG_PATH}' --clobber"
  info ""
  info "Or re-run this script with --upload:"
  info "  $0 ${TAG} --upload"
fi

# Cleanup state
rm -f "$STATE_FILE"
info "Complete."

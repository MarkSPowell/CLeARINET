#!/usr/bin/env bash
#
# Smoke-tests a finished CLeARINET .dmg on the macOS release runner, so a
# broken bundle fails the build instead of reaching someone with a Mac:
#
#   1. The .dmg mounts, and holds CLeARINET.app and an Applications link.
#   2. The app's signature verifies the way macOS checks it (strict, and
#      every nested file sealed), and Contents/MacOS holds only the
#      executable (anything else there breaks the signature).
#   3. The User Guide is in the bundle (Help > Documentation).
#   4. The Optional Extensions folder has each extension, its licence, the
#      notices file and the README.
#   5. The app launches through Launch Services (the same path as
#      double-clicking it) and is still running after a short wait.
#
# What it can't test: the first-launch prompt a downloaded (quarantined)
# app gets, the certificate and system proxy prompts, and anything visual.
# Those still need a person on a real Mac.
#
# Usage: smoke-test.sh <dmgPath>

set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <dmgPath>" >&2
  exit 1
fi

DMG_PATH="$1"
APP_NAME="CLeARINET"
EXECUTABLE_NAME="Clearinet.DesktopUi"
LAUNCH_WAIT_SECONDS=20

failures=0
pass() { echo "  ok: $1"; }
fail() { echo "  FAILED: $1" >&2; failures=$((failures + 1)); }

WORK_DIR="$(mktemp -d)"
MOUNT_POINT="$WORK_DIR/mount"
mkdir -p "$MOUNT_POINT"

cleanup() {
  pkill -f "$APP_NAME.app/Contents/MacOS/$EXECUTABLE_NAME" 2>/dev/null || true
  hdiutil detach "$MOUNT_POINT" -quiet 2>/dev/null || true
  rm -rf "$WORK_DIR"
}
trap cleanup EXIT

echo "1. Mounting $DMG_PATH"
hdiutil attach "$DMG_PATH" -nobrowse -readonly -mountpoint "$MOUNT_POINT" -quiet
MOUNTED_APP="$MOUNT_POINT/$APP_NAME.app"
[[ -d "$MOUNTED_APP" ]] && pass "$APP_NAME.app is in the .dmg" || fail "$APP_NAME.app is missing from the .dmg"
[[ -L "$MOUNT_POINT/Applications" ]] && pass "Applications link is in the .dmg" || fail "Applications link is missing"

# Copied off the read-only image first, the way a user drags it to
# Applications. ditto keeps the signature and extended attributes intact.
APP="$WORK_DIR/$APP_NAME.app"
ditto "$MOUNTED_APP" "$APP"

echo "2. Signature"
if codesign --verify --deep --strict --verbose=2 "$APP"; then
  pass "signature verifies (--deep --strict)"
else
  fail "signature doesn't verify"
fi
macos_entries="$(ls -A "$APP/Contents/MacOS")"
if [[ "$macos_entries" == "$EXECUTABLE_NAME" ]]; then
  pass "Contents/MacOS holds only $EXECUTABLE_NAME"
else
  fail "Contents/MacOS holds more than the executable: $(echo "$macos_entries" | tr '\n' ' ')"
fi
# Informational only: an ad-hoc signed app is expected to be rejected here
# (it isn't Developer ID signed or notarized). Printed so a change in what
# Gatekeeper says shows up in the log.
echo "  (Gatekeeper's view, expected to reject an ad-hoc signed app:)"
spctl --assess --type execute --verbose=2 "$APP" 2>&1 | sed 's/^/    /' || true

echo "3. Documentation"
[[ -f "$APP/Contents/Resources/Documentation/User Guide.md" ]] \
  && pass "User Guide is in Contents/Resources/Documentation" \
  || fail "User Guide is missing from Contents/Resources/Documentation"

echo "4. Optional Extensions"
OPTIONAL_DIR="$MOUNT_POINT/Optional Extensions"
for expected in \
  CLeARINETNetLog.dll CLeARINETCSP.dll PrivacyScanner.dll \
  README.txt THIRD-PARTY-NOTICES.txt \
  licenses/CLeARINETNetLog-LICENSE.txt licenses/CLeARINETCSP-LICENSE.txt licenses/PrivacyScanner-LICENSE.txt; do
  [[ -f "$OPTIONAL_DIR/$expected" ]] && pass "$expected" || fail "Optional Extensions/$expected is missing"
done

echo "5. Launch"
LOG="$WORK_DIR/launch.log"
# -n: a new instance; --stdout/--stderr: keep the app's console output.
open -n --stdout "$LOG" --stderr "$LOG" "$APP"
sleep "$LAUNCH_WAIT_SECONDS"
if pgrep -f "$APP_NAME.app/Contents/MacOS/$EXECUTABLE_NAME" > /dev/null; then
  pass "still running after ${LAUNCH_WAIT_SECONDS}s"
else
  fail "not running ${LAUNCH_WAIT_SECONDS}s after launch (crashed or quit)"
fi
echo "  App output:"
sed 's/^/    /' "$LOG" 2>/dev/null || echo "    (none)"
# The app's own crash report, if macOS wrote one.
crash="$(ls -t "$HOME/Library/Logs/DiagnosticReports/"*"$EXECUTABLE_NAME"* 2>/dev/null | head -1 || true)"
if [[ -n "$crash" ]]; then
  echo "  Crash report: $crash"
  head -60 "$crash" | sed 's/^/    /'
fi

echo
if [[ $failures -gt 0 ]]; then
  echo "Smoke test FAILED: $failures check(s)." >&2
  exit 1
fi
echo "Smoke test passed."

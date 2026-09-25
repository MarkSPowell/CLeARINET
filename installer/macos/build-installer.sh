#!/usr/bin/env bash
#
# Assembles a `dotnet publish -r osx-arm64` output into a real CLeARINET.app
# bundle, then packages that bundle into a .dmg -- the macOS equivalent of
# what installer/CLeARINET.iss (Inno Setup) does for the Windows installer.
# There's no push-button "ISCC.exe" for macOS the way there is for Windows,
# so this is a plain script instead of a config file for some other tool --
# see .github/workflows/release-macos.yml, the only caller, for how this is
# invoked in CI. Runnable by hand too (on a real Mac) for local testing;
# nothing here is CI-specific.
#
# Deliberately narrow in scope, matching this project's macOS work so far
# (see the Interception Certificate Design doc's own "Testing scope,
# deliberately narrow" note for the same posture elsewhere):
#   - arm64 only, no universal/x64 build -- see the Project Plan's own
#     "macOS release pipeline" update paragraph for why.
#   - Unsigned. Not ad-hoc signed, not Developer ID signed, not notarized --
#     no Apple Developer account was available to this session. The
#     individual published executable is already ad-hoc-signed by the .NET
#     SDK itself (a macOS kernel requirement for any arm64 binary to
#     execute at all, unrelated to Gatekeeper's separate "trusted
#     developer" check) -- this script doesn't touch that signature or add
#     a bundle-level one on top of it. What that means in practice for a
#     real user, and the one genuinely open question this session couldn't
#     resolve without a real Mac (whether Gatekeeper additionally refuses
#     to *launch* a from-scratch-assembled bundle like this one outright,
#     as "damaged", rather than just warning about an unidentified
#     developer), is spelled out in the Project Plan doc -- flagged there,
#     not assumed away.
#   - No custom app icon (no CFBundleIconFile below) -- matches
#     installer/CLeARINET.iss's own precedent of shipping no standalone
#     .ico today either; see that file's own remarks.
#
# Usage: build-installer.sh <version> <bundleVersion> <publishDir> <outputDmgPath>
#   version        Full version string, e.g. "0.1.1-preview.1" -- goes into
#                   CFBundleShortVersionString (Finder's "Get Info" version)
#                   and the .app bundle's own display name is left as plain
#                   "CLeARINET", matching the Windows installer's own
#                   MyAppName/MyAppVersion split (installer/CLeARINET.iss).
#   bundleVersion  Purely numeric X.X.X.X derivative of the above (no
#                   prerelease suffix), e.g. "0.1.1.0" -- goes into
#                   CFBundleVersion. Same reasoning as the Windows release
#                   workflow's own $fileVersion: Inno Setup's
#                   VersionInfoVersion can't take a "-preview.1" suffix,
#                   and neither should a well-formed CFBundleVersion.
#   publishDir     The `dotnet publish -r osx-arm64 --self-contained
#                   -p:PublishSingleFile=true` output directory -- expects
#                   to find the Clearinet.DesktopUi executable directly
#                   inside it (see Clearinet.DesktopUi.csproj's own remarks
#                   on PublishSingleFile for why nothing else needs
#                   flattening here the way the legacy-host build does on
#                   the Windows side).
#   outputDmgPath  Where to write the finished .dmg.

set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "Usage: $0 <version> <bundleVersion> <publishDir> <outputDmgPath>" >&2
  exit 1
fi

VERSION="$1"
BUNDLE_VERSION="$2"
PUBLISH_DIR="$3"
OUTPUT_DMG_PATH="$4"

APP_NAME="CLeARINET"
EXECUTABLE_NAME="Clearinet.DesktopUi"
# Arbitrary, not tied to any domain CLeARINET actually owns -- reverse-DNS
# bundle identifiers only need to be locally unique, not a real registered
# domain, and this project doesn't have one yet (see the Project Plan's
# "Still undecided" section, which already flags the name/trademark
# question as open). Easy to change later; nothing else in this script or
# the app itself depends on this specific string.
BUNDLE_IDENTIFIER="net.clearinet.desktopui"

EXECUTABLE_PATH="$PUBLISH_DIR/$EXECUTABLE_NAME"
if [[ ! -f "$EXECUTABLE_PATH" ]]; then
  echo "error: expected published executable not found at '$EXECUTABLE_PATH'" >&2
  echo "  (did the dotnet publish step run with -r osx-arm64 first?)" >&2
  exit 1
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

APP_BUNDLE="$WORK_DIR/$APP_NAME.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

# The executable itself goes straight into Contents/MacOS, matching every
# other Mac app bundle's own layout, under its own actual name --
# CFBundleExecutable below points at this same name, not "CLeARINET", so
# the two stay in sync without needing a rename.
cp "$EXECUTABLE_PATH" "$MACOS_DIR/$EXECUTABLE_NAME"
chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"

# Everything else PublishSingleFile left loose beside the executable --
# today that's just the Documentation/User Guide.md folder
# (Clearinet.DesktopUi.csproj's own None/Link entry for it) -- goes into
# Resources rather than MacOS, which is the conventional home for an app
# bundle's own non-executable content. OpenDocumentationCommand resolves
# this relative to AppContext.BaseDirectory, which for a bundled macOS app
# is Contents/MacOS (where the executable itself lives, not Resources) --
# so this also drops a copy next to the executable, matching what that
# command actually expects to find. Keeping both isn't wasted space worth
# avoiding: this is a handful of KB, nowhere near worth the fragility of
# only supporting one of the two layouts.
for entry in "$PUBLISH_DIR"/*; do
  name="$(basename "$entry")"
  if [[ "$name" == "$EXECUTABLE_NAME" ]]; then
    continue
  fi
  cp -R "$entry" "$RESOURCES_DIR/$name"
  cp -R "$entry" "$MACOS_DIR/$name"
done

# A minimal Info.plist -- just what an Avalonia app actually needs. No
# NSPrincipalClass: that's an AppKit/Xcode-project convention for locating
# the app's principal NSApplication subclass, and Avalonia manages its own
# native macOS window/application lifecycle internally rather than relying
# on Info.plist to wire one up, so there's nothing here for it to point at.
# No comments inside the plist body itself, even though XML comments are
# technically legal there -- unconfirmed whether every plist reader this
# build might meet tolerates them, and there's nothing to gain by risking
# it. LSMinimumSystemVersion is set to 11.0 (the first macOS release that
# runs on Apple Silicon at all) purely for documentation purposes -- this
# build is arm64-only in the first place (see this script's own header
# remarks), so nothing older than that could run it regardless of what
# this key says.
cat > "$CONTENTS_DIR/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_IDENTIFIER</string>
    <key>CFBundleVersion</key>
    <string>$BUNDLE_VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleExecutable</key>
    <string>$EXECUTABLE_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.developer-tools</string>
</dict>
</plist>
PLIST

mkdir -p "$(dirname "$OUTPUT_DMG_PATH")"
rm -f "$OUTPUT_DMG_PATH"

# UDZO = compressed, read-only -- the same format every ordinary
# drag-to-Applications .dmg distribution uses. The Applications symlink
# alongside the .app in the staged volume is what makes Finder show the
# familiar "drag CLeARINET.app onto Applications" arrangement when the
# .dmg is opened; nothing generates that automatically just from
# -srcfolder containing only the .app itself.
DMG_STAGING_DIR="$WORK_DIR/dmg-staging"
mkdir -p "$DMG_STAGING_DIR"
cp -R "$APP_BUNDLE" "$DMG_STAGING_DIR/"
ln -s /Applications "$DMG_STAGING_DIR/Applications"

hdiutil create \
  -volname "$APP_NAME" \
  -srcfolder "$DMG_STAGING_DIR" \
  -ov \
  -format UDZO \
  "$OUTPUT_DMG_PATH"

echo "Built $OUTPUT_DMG_PATH"

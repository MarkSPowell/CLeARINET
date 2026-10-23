#!/usr/bin/env bash
#
# Builds VioLens.app from a `dotnet publish -r osx-arm64` output and packages
# it as a .dmg: the macOS counterpart of installer/VioLens.iss. Called by
# .github/workflows/release-macos.yml, and runs the same on a Mac by hand.
#
#   - arm64 only.
#   - Ad-hoc signed, not notarized: there's no Apple Developer account.
#     Copying the SDK-signed executable into a hand-built bundle breaks its
#     signature, and macOS calls such a downloaded app "damaged", so the
#     whole bundle is ad-hoc signed below. Gatekeeper then asks once
#     (System Settings > Privacy & Security > Open Anyway, or
#     `xattr -dr com.apple.quarantine /Applications/VioLens.app`).
#   - The icon (VioLens.icns) is made at build time with sips and iconutil
#     from violens-icon-1024.png beside this script, the app icon at the
#     largest size macOS uses.
#
# Usage: build-installer.sh <version> <bundleVersion> <publishDir> <outputDmgPath> [extensionsDir]
#   version        Full version, e.g. "0.1.1-preview.1", for
#                   CFBundleShortVersionString.
#   bundleVersion  Numeric form, e.g. "0.1.1.0", for CFBundleVersion.
#   publishDir     The `dotnet publish -r osx-arm64 --self-contained
#                   -p:PublishSingleFile=true` output, with the
#                   Violens.DesktopUi executable directly inside.
#   outputDmgPath  Where to write the .dmg.
#   extensionsDir  Optional: installer/build-extensions.ps1's output, put in
#                   the .dmg as an "Optional Extensions" folder with a README.

set -euo pipefail

if [[ $# -ne 4 && $# -ne 5 ]]; then
  echo "Usage: $0 <version> <bundleVersion> <publishDir> <outputDmgPath> [extensionsDir]" >&2
  exit 1
fi

VERSION="$1"
BUNDLE_VERSION="$2"
PUBLISH_DIR="$3"
OUTPUT_DMG_PATH="$4"
EXTENSIONS_DIR="${5:-}"

APP_NAME="VioLens"
EXECUTABLE_NAME="Violens.DesktopUi"
# Reverse-DNS of violens.dev, which the project owns. Changed from
# net.clearinet.desktopui with the rename, so macOS sees VioLens as a new
# app; its settings are carried over by the app itself, not by macOS.
BUNDLE_IDENTIFIER="dev.violens.desktopui"

ICON_SOURCE="$(cd "$(dirname "$0")" && pwd)/violens-icon-1024.png"
if [[ ! -f "$ICON_SOURCE" ]]; then
  echo "error: app icon artwork not found at '$ICON_SOURCE'" >&2
  exit 1
fi

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

# The executable goes in Contents/MacOS under its own name, which
# CFBundleExecutable names.
cp "$EXECUTABLE_PATH" "$MACOS_DIR/$EXECUTABLE_NAME"
chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"

# Everything else PublishSingleFile left beside the executable (the
# Documentation folder) goes in Resources. Contents/MacOS may hold only
# code, or codesign fails with "code object is not signed at all". .pdb
# files aren't shipped.
for entry in "$PUBLISH_DIR"/*; do
  name="$(basename "$entry")"
  if [[ "$name" == "$EXECUTABLE_NAME" || "$name" == *.pdb ]]; then
    continue
  fi
  cp -R "$entry" "$RESOURCES_DIR/$name"
done

# The app icon Finder, the Dock and Launchpad show. Without one (and
# CFBundleIconFile pointing at it) macOS shows the generic app icon. An
# .iconset folder holds each size under the names iconutil expects; each
# @2x is the Retina version of the size before it.
ICONSET_DIR="$WORK_DIR/$APP_NAME.iconset"
mkdir -p "$ICONSET_DIR"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$ICON_SOURCE" --out "$ICONSET_DIR/icon_${size}x${size}.png" > /dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$ICON_SOURCE" --out "$ICONSET_DIR/icon_${size}x${size}@2x.png" > /dev/null
done
iconutil --convert icns --output "$RESOURCES_DIR/$APP_NAME.icns" "$ICONSET_DIR"

# A minimal Info.plist. Avalonia runs its own application lifecycle, so
# there's no NSPrincipalClass. LSMinimumSystemVersion 11.0 is the first
# release on Apple Silicon.
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
    <key>CFBundleIconFile</key>
    <string>$APP_NAME.icns</string>
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

# Ad-hoc sign the whole bundle ("-" means no identity: free, no account),
# so its signature covers Info.plist and every file in it (Contents/MacOS
# holds only the executable; everything else is in Resources, sealed as
# the bundle's resources). Verify straight away, so a bad bundle fails the
# build rather than reaching users.
codesign --force --sign - "$APP_BUNDLE"
codesign --verify --verbose=2 "$APP_BUNDLE"

mkdir -p "$(dirname "$OUTPUT_DMG_PATH")"
rm -f "$OUTPUT_DMG_PATH"

# A compressed, read-only .dmg with an Applications link beside the app,
# for the usual drag-to-install window.
DMG_STAGING_DIR="$WORK_DIR/dmg-staging"
mkdir -p "$DMG_STAGING_DIR"
cp -R "$APP_BUNDLE" "$DMG_STAGING_DIR/"
ln -s /Applications "$DMG_STAGING_DIR/Applications"

if [[ -n "$EXTENSIONS_DIR" ]]; then
  if [[ ! -d "$EXTENSIONS_DIR" ]]; then
    echo "error: extensions folder not found at '$EXTENSIONS_DIR'" >&2
    exit 1
  fi
  OPTIONAL_DIR="$DMG_STAGING_DIR/Optional Extensions"
  mkdir -p "$OPTIONAL_DIR"
  cp -R "$EXTENSIONS_DIR"/. "$OPTIONAL_DIR/"
  cat > "$OPTIONAL_DIR/README.txt" <<'README'
Optional extensions for VioLens
=================================

VioLens works without these. To add one, copy its .dll into

    ~/Documents/VioLens/Extensions

(create the folder if it isn't there), then restart VioLens.

  VioLensNetLog.dll      NetLog importer: File > Import via Extension,
                           for Chromium NetLog JSON captures.
  VioLensCSP.dll         CSP Rule Collector: builds a
                           Content-Security-Policy for the sites you browse.
  PrivacyScanner.dll       Privacy Scanner: colours responses that set
                           cookies and checks P3P headers (P3P is obsolete;
                           mostly useful as an example extension).

To remove one, delete its .dll from that folder and restart VioLens.

Each extension is a separate work under its own licence: see
THIRD-PARTY-NOTICES.txt and the licenses folder.
README
fi

hdiutil create \
  -volname "$APP_NAME" \
  -srcfolder "$DMG_STAGING_DIR" \
  -ov \
  -format UDZO \
  "$OUTPUT_DMG_PATH"

echo "Built $OUTPUT_DMG_PATH"

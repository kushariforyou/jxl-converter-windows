#!/bin/zsh
# Downloads the Windows builds of cjxl (libjxl) and ExifTool into tools-win/, which the release bundles.
set -euo pipefail
cd "$(dirname "$0")/.."
JXL_VERSION=0.12.0
EXIFTOOL_VERSION=13.59
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

curl -fL --retry 5 -o "$tmp/jxl.zip" \
  "https://github.com/libjxl/libjxl/releases/download/v$JXL_VERSION/jxl-x64-windows-static.zip"
curl -fL --retry 5 -o "$tmp/exiftool.zip" \
  "https://downloads.sourceforge.net/project/exiftool/exiftool-${EXIFTOOL_VERSION}_64.zip"
unzip -q "$tmp/jxl.zip" -d "$tmp/jxl"
unzip -q "$tmp/exiftool.zip" -d "$tmp/exif"

rm -rf tools-win
mkdir -p tools-win/licenses/libjxl
cp "$tmp/jxl/x64-windows-static/bin/cjxl.exe" tools-win/
cp -R "$tmp/jxl/x64-windows-static/licenses/." tools-win/licenses/libjxl/
exif="$tmp/exif/exiftool-${EXIFTOOL_VERSION}_64"
cp "$exif/exiftool(-k).exe" tools-win/exiftool.exe   # renamed so it doesn't pause for a keypress
cp -R "$exif/exiftool_files" tools-win/
cp "$exif/README.txt" tools-win/licenses/exiftool-README.txt
echo "Fetched cjxl $JXL_VERSION and ExifTool $EXIFTOOL_VERSION into tools-win/"

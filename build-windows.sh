#!/bin/zsh
# Builds the Windows release (publish/JXL-Converter-Windows.zip). Works from macOS or any OS with the .NET 10 SDK.
set -euo pipefail
cd "$(dirname "$0")"
export PATH=/usr/local/share/dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
[[ -f tools-win/cjxl.exe ]] || ./scripts/fetch-tools.sh
rm -rf "publish/JXL Converter" publish/JXL-Converter-Windows.zip
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "publish/JXL Converter"
cp dist/README.txt "publish/JXL Converter/README.txt"
(cd publish && zip -qr -X JXL-Converter-Windows.zip "JXL Converter")
echo "Built publish/JXL-Converter-Windows.zip"

# JXL Converter for Windows

A Windows app for batch-converting images to **JPEG XL** (`.jxl`).

- Drag in images or whole folders: JPEG, PNG, GIF, HEIC, TIFF, WebP, AVIF, BMP, RAW and more
- **Add Folder** asks whether to include subfolders and which file types to pick up (JPEG, PNG, HEIC, RAW, …), with a live count of matching images
- **Lossless** checkbox: JPEGs are repacked losslessly (about 20% smaller, restorable bit-for-bit); other formats are pixel-perfect
- Lossy mode with a quality slider, plus an effort slider for speed vs. size
- **Preserve EXIF & XMP metadata** (camera info, GPS, dates, rotation, file timestamps), or strip it
- **Keep original if JPEG XL is larger**: discards outputs that grew (common for HEIC)
- Live "space saved" total that counts up as each file finishes, with per-file savings
- Converts several files in parallel; originals are never modified

Looking for Mac? See [jxl-converter-mac](https://github.com/kushariforyou/jxl-converter-mac).

## Install

1. Download `JXL-Converter-Windows.zip` from [Releases](../../releases) and unzip it.
2. Run **JXL Converter.exe**. Keep the `tools` folder next to it.
3. The app isn't code-signed, so Windows may show "Windows protected your PC". Click **More info → Run anyway**.

Requires 64-bit Windows 10 or 11. Everything it needs is included.

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). It builds on Windows, macOS or Linux:

```bash
./build-windows.sh
```

The first build downloads the Windows `cjxl.exe` and ExifTool into `tools-win/` (see `scripts/fetch-tools.sh`). The result is `publish/JXL-Converter-Windows.zip`.

To run the UI locally while developing: `dotnet run`.

## How it works

Built with [Avalonia](https://avaloniaui.net) on .NET 10. Encoding uses [`cjxl`](https://github.com/libjxl/libjxl) from libjxl. Formats `cjxl` can't read (HEIC, TIFF, WebP, RAW, …) are decoded with [Magick.NET](https://github.com/dlemstra/Magick.NET) first, and their metadata is copied with [ExifTool](https://exiftool.org).

## Third-party software

The release bundles libjxl's `cjxl` (BSD-3-Clause) and ExifTool by Phil Harvey (Perl Artistic License / GPL). Their license files are in `tools/licenses`.

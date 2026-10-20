JXL Converter for Windows
=========================

Batch-converts images to JPEG XL (.jxl).

How to use
  1. Keep this whole folder together: "JXL Converter.exe" needs the "tools" folder next to it.
  2. Double-click "JXL Converter.exe".
     If Windows shows "Windows protected your PC", click "More info" -> "Run anyway"
     (the app isn't code-signed).
  3. Drag images or folders into the window (or use Add Images / Add Folder),
     pick your options and click Convert.

Options
  Lossless          Pixel-perfect output. JPEGs are repacked losslessly (~20% smaller).
  Quality / Effort  Used for lossy output / speed-vs-size trade-off.
  Preserve EXIF & XMP metadata   Keeps camera info, GPS, dates and file timestamps.
  Skip files that would get larger     If the JPEG XL would be bigger than the original (common for
                                       HEIC), that file is not converted.
  Output            Next to the originals, or a folder you choose.
  Delete originals after converting   Asks for confirmation. When all conversions finish, originals of
                                      successfully converted files go to the Recycle Bin. Skipped or
                                      failed files are kept. Otherwise originals are never changed.

Adding a folder asks whether to include subfolders and which file types (JPEG, PNG, HEIC, ...) to add.

Shortcuts: Ctrl+O add images, Ctrl+Shift+O add folder, Ctrl+Enter convert, Delete removes selected files.

Requires 64-bit Windows 10 or 11. Nothing else needs installing.
Bundled: libjxl cjxl 0.12.0 (BSD license) and ExifTool 13.59 by Phil Harvey (Perl Artistic license),
see tools/licenses.

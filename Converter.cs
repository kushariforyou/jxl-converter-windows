using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ImageMagick;

namespace JxlConverter;

public record ConvertOptions(bool Lossless, int Quality, int Effort, bool KeepMetadata, string? OutputDir, bool Overwrite, bool SkipLarger);

public abstract record Outcome;
public record Success(string Output, long Size, string? Note) : Outcome;
public record Skipped(string Message) : Outcome;
public record Failure(string Message) : Outcome;

public record ToolSet(string Cjxl, string? Exiftool);

public record FoundFile(string Path, string RelativeDir);

/// File-type groups offered when adding a folder.
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<FileKind>))]
public enum FileKind { Jpeg, Png, Gif, Heic, Tiff, WebP, Avif, Bmp, Raw, Other }

public static class FileKinds
{
    public static readonly FileKind[] All = Enum.GetValues<FileKind>();

    public static string Title(FileKind kind) => kind switch
    {
        FileKind.Jpeg => "JPEG",
        FileKind.Png => "PNG",
        FileKind.Gif => "GIF",
        FileKind.Heic => "HEIC / HEIF",
        FileKind.Tiff => "TIFF",
        FileKind.WebP => "WebP",
        FileKind.Avif => "AVIF",
        FileKind.Bmp => "BMP",
        FileKind.Raw => "Camera RAW",
        _ => "Other",
    };

    public static string[] Extensions(FileKind kind) => kind switch
    {
        FileKind.Jpeg => new[] { ".jpg", ".jpeg", ".jpe" },
        FileKind.Png => new[] { ".png", ".apng" },
        FileKind.Gif => new[] { ".gif" },
        FileKind.Heic => new[] { ".heic", ".heif", ".hif" },
        FileKind.Tiff => new[] { ".tif", ".tiff" },
        FileKind.WebP => new[] { ".webp" },
        FileKind.Avif => new[] { ".avif" },
        FileKind.Bmp => new[] { ".bmp" },
        FileKind.Raw => new[] { ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".orf", ".rw2" },
        _ => new[] { ".psd", ".tga", ".jp2", ".ico", ".ppm", ".pnm", ".pgm", ".pbm", ".pam", ".pfm", ".exr" },
    };
}

// MARK: External tools

public static class Tools
{
    public static string? Find(string name)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var dirs = new List<string> { Path.Combine(AppContext.BaseDirectory, "tools") };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        dirs.AddRange(new[] { "/opt/homebrew/bin", "/usr/local/bin" });
        return dirs.Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
    }

    /// Runs a tool, killing it if the batch is cancelled.
    public static (int Code, string Output) Run(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        // Keep the UI snappy while encoders saturate the CPU.
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output + err.Result);
    }

    public static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", new[] { "-R", path });
            else
                Process.Start("xdg-open", Path.GetDirectoryName(path)!);
        }
        catch { }
    }
}

// MARK: Conversion

public static class Converter
{
    /// Formats cjxl reads natively (metadata carried over by cjxl itself).
    static readonly HashSet<string> DirectExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jpe", ".png", ".apng", ".gif", ".ppm", ".pnm", ".pgm", ".pbm", ".pam", ".pfm", ".exr" };

    /// Formats decoded with ImageMagick first (metadata copied with exiftool).
    static readonly HashSet<string> DecodedExts = new(StringComparer.OrdinalIgnoreCase)
        { ".heic", ".heif", ".hif", ".tif", ".tiff", ".bmp", ".webp", ".avif", ".dng", ".cr2", ".cr3",
          ".nef", ".arw", ".raf", ".orf", ".rw2", ".psd", ".tga", ".jp2", ".ico" };

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return DirectExts.Contains(ext) || DecodedExts.Contains(ext);
    }

    /// Lists matching images in a folder. RelativeDir includes the folder's own name so a custom
    /// output folder mirrors the original structure.
    public static List<FoundFile> ScanFolder(string folder, bool recursive, ISet<string> extensions)
    {
        var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar)) ?? "";
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var found = new List<FoundFile>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*", opts))
                if (extensions.Contains(System.IO.Path.GetExtension(f)))
                    found.Add(new FoundFile(f, System.IO.Path.GetRelativePath(root, System.IO.Path.GetDirectoryName(f)!)));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return found;
    }

    public static Outcome Convert(string src, string relativeDir, ConvertOptions opts, ToolSet tools, CancellationToken ct)
    {
        var ext = Path.GetExtension(src).ToLowerInvariant();
        var isJpeg = ext is ".jpg" or ".jpeg" or ".jpe";
        var direct = DirectExts.Contains(ext);

        var destDir = opts.OutputDir is { Length: > 0 } od ? Path.Combine(od, relativeDir) : Path.GetDirectoryName(src)!;
        try { Directory.CreateDirectory(destDir); }
        catch (Exception e) { return new Failure("Can't create folder: " + e.Message); }
        var output = ReserveOutput(src, destDir, opts.Overwrite);

        // Formats cjxl can't read are decoded to a temporary PNG first.
        string input = src;
        string? tempPng = null;
        try
        {
            if (!direct)
            {
                tempPng = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
                try { DecodeToPng(src, tempPng); }
                catch (Exception e)
                {
                    TryDelete(output);
                    return new Failure("Couldn't decode image: " + FirstLine(e.Message));
                }
                input = tempPng;
            }

            var args = new List<string> { CliPath(input), CliPath(output), "-e", opts.Effort.ToString(), "--quiet" };
            if (opts.Lossless)
            {
                args.AddRange(new[] { "-d", "0" });
                if (isJpeg) args.Add("--lossless_jpeg=1");
            }
            else
            {
                args.AddRange(new[] { "-q", opts.Quality.ToString() });
                if (isJpeg) args.Add("--lossless_jpeg=0");
            }
            if (!opts.KeepMetadata)
            {
                args.AddRange(new[] { "-x", "strip=exif", "-x", "strip=xmp", "-x", "strip=jumbf" });
                if (isJpeg && opts.Lossless) args.Add("--allow_jpeg_reconstruction=0");
            }

            var (code, text) = Tools.Run(tools.Cjxl, args, ct);
            if (code != 0 || FileSize(output) == 0)
            {
                TryDelete(output);
                if (ct.IsCancellationRequested) return new Failure("Cancelled");
                var last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
                return new Failure(last ?? $"cjxl failed ({code})");
            }

            string? note = null;
            if (opts.KeepMetadata)
            {
                // cjxl keeps EXIF/XMP itself for JPEG→lossless and PNG; lossy JPEG drops XMP, decoded formats lose all.
                if (!direct || (isJpeg && !opts.Lossless))
                {
                    if (tools.Exiftool is { } exiftool)
                    {
                        if (!CopyMetadata(exiftool, src, output, resetOrientation: !direct, ct)) note = "Metadata not copied";
                    }
                    else note = "exiftool missing – metadata not copied";
                }
                CopyDates(src, output);
            }
            var newSize = FileSize(output);
            if (opts.SkipLarger && newSize >= FileSize(src))
            {
                TryDelete(output);
                return new Skipped("Kept original – JXL was larger");
            }
            return new Success(output, newSize, note);
        }
        finally
        {
            if (tempPng != null) TryDelete(tempPng);
        }
    }

    public static long FileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    /// Picks `name.jxl` (or `name (n).jxl`) and atomically claims it so parallel jobs never collide.
    static string ReserveOutput(string src, string dir, bool overwrite)
    {
        var name = Path.GetFileNameWithoutExtension(src);
        var path = Path.Combine(dir, name + ".jxl");
        if (overwrite)
        {
            // Make sure the file exists so a short (8.3) path can be resolved for it on Windows.
            if (!File.Exists(path)) try { File.Create(path).Dispose(); } catch { }
            return path;
        }
        for (int n = 1; ; n++)
        {
            try
            {
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                path = Path.Combine(dir, $"{name} ({n}).jxl");
            }
        }
    }

    /// Decodes with ImageMagick and writes an upright PNG (some decoders, like HEIC, already rotate the pixels).
    static void DecodeToPng(string src, string dest)
    {
        using var img = new MagickImage(src);
        img.AutoOrient();
        // Metadata is copied straight from the original afterwards; only the colour profile travels with the pixels.
        foreach (var profile in new[] { "exif", "xmp", "iptc", "8bim" }) img.RemoveProfile(profile);
        img.Write(dest, MagickFormat.Png);
    }

    static bool CopyMetadata(string exiftool, string src, string dst, bool resetOrientation, CancellationToken ct)
    {
        var args = new List<string> { "-q", "-q", "-m", "-overwrite_original", "-tagsFromFile", src, "-all:all" };
        // Decoded pixels are already upright, so the copied orientation tag must not rotate them again.
        if (resetOrientation) args.Add("-EXIF:Orientation#=1");
        args.Add(dst);

        if (!OperatingSystem.IsWindows())
            return Tools.Run(exiftool, args, ct).Code == 0;

        // On Windows exiftool only handles non-ASCII file names when they arrive as UTF-8, so pass them in an arg file.
        var argFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".args");
        try
        {
            File.WriteAllLines(argFile, args, new UTF8Encoding(false));
            return Tools.Run(exiftool, new[] { "-charset", "filename=utf8", "-@", argFile }, ct).Code == 0;
        }
        finally { TryDelete(argFile); }
    }

    static void CopyDates(string src, string dst)
    {
        try
        {
            File.SetCreationTimeUtc(dst, File.GetCreationTimeUtc(src));
            File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
        }
        catch { }
    }

    /// cjxl on Windows reads its arguments in the ANSI code page, so non-ASCII paths are swapped for their 8.3 short form.
    static string CliPath(string path)
    {
        if (!OperatingSystem.IsWindows() || path.All(c => c < 128)) return path;
        var sb = new StringBuilder(1024);
        var len = GetShortPathName(path, sb, sb.Capacity);
        return len > 0 && len < sb.Capacity ? sb.ToString() : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetShortPathName(string longPath, StringBuilder shortPath, int bufferSize);

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    static string FirstLine(string s) => s.Split('\n')[0].Trim();
}

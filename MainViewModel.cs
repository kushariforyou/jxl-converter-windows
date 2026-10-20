using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JxlConverter;

public enum ItemStatus { Pending, Converting, Done, Skipped, Failed }

public partial class ConvItem : ObservableObject
{
    public ConvItem(string source, string relativeDir, long originalSize)
    {
        Source = source;
        RelativeDir = relativeDir;
        OriginalSize = originalSize;
    }

    public string Source { get; }
    public string RelativeDir { get; }
    public long OriginalSize { get; }
    public string FileName => Path.GetFileName(Source);
    public string OriginalText => Bytes.Format(OriginalSize);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewText), nameof(SavedText), nameof(SavedBrush))]
    private long? newSize;

    [ObservableProperty]
    private string? output;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusGlyph), nameof(StatusBrush), nameof(IsConverting), nameof(ShowGlyph))]
    private ItemStatus status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusGlyph), nameof(StatusBrush))]
    private string? message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool originalTrashed;

    public string NewText => NewSize is { } n ? Bytes.Format(n) : "—";

    public string SavedText
    {
        get
        {
            if (NewSize is not { } n || OriginalSize <= 0) return "—";
            var f = (double)(OriginalSize - n) / OriginalSize * 100;
            return f >= 0 ? $"−{Math.Round(f)}%" : $"+{Math.Round(-f)}%";
        }
    }

    public IBrush SavedBrush => NewSize is { } n && n > OriginalSize ? Palette.Orange : NewSize is null ? Palette.Muted : Palette.Green;

    public bool IsConverting => Status == ItemStatus.Converting;
    public bool ShowGlyph => Status != ItemStatus.Converting;

    public string StatusText => Status switch
    {
        ItemStatus.Pending => "Waiting",
        ItemStatus.Converting => "Converting",
        ItemStatus.Done => Message ?? (OriginalTrashed ? $"Done – original in {Trash.Name}" : "Done"),
        ItemStatus.Skipped => Message ?? "Skipped",
        _ => Message ?? "Failed",
    };

    public string StatusGlyph => Status switch
    {
        ItemStatus.Done => Message == null ? "✓" : "!",
        ItemStatus.Failed => "✕",
        ItemStatus.Skipped => "↩",
        _ => "",
    };

    public IBrush StatusBrush => Status switch
    {
        ItemStatus.Done => Message == null ? Palette.Green : Palette.Orange,
        ItemStatus.Failed => Palette.Red,
        _ => Palette.Muted,
    };
}

public static class Palette
{
    public static readonly IBrush Green = new SolidColorBrush(Color.Parse("#2EA043"));
    public static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#E08A2E"));
    public static readonly IBrush Red = new SolidColorBrush(Color.Parse("#D73A49"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#888888"));
}

public static class Bytes
{
    /// Matches File Explorer: 1024-based units.
    public static string Format(long n)
    {
        double v = Math.Abs(n);
        if (v < 1024) return $"{n} bytes";
        string[] units = { "KB", "MB", "GB", "TB" };
        int i = -1;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
        var s = i == 0 ? v.ToString("0") : v.ToString(v >= 100 ? "0" : "0.#");
        return (n < 0 ? "-" : "") + s + " " + units[i];
    }
}

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<ConvItem> Items { get; } = new();

    // Settings
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LosslessHint))]
    private bool lossless = true;
    [ObservableProperty] private double quality = 90;
    [ObservableProperty] private double effort = 7;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetadataHint), nameof(ShowExiftoolWarning))]
    private bool keepMetadata = true;
    [ObservableProperty] private bool overwrite;
    [ObservableProperty] private bool skipLarger = true;

    // Deliberately not saved between launches, so it is never on by surprise.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginalsHint), nameof(OriginalsHintBrush))]
    private bool deleteOriginals;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrashedText), nameof(HasTrashed))]
    private int trashedCount;

    // Folder import choices, remembered between uses.
    public bool FolderRecursive { get; set; } = true;
    public HashSet<FileKind> FolderExcludedKinds { get; set; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputNextToOriginals), nameof(HasOutputFolder))]
    private string outputFolder = "";

    // State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConvert), nameof(CanEditList))]
    private bool isRunning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CjxlMissing), nameof(CanConvert))]
    private string? cjxlPath;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExiftoolWarning))]
    private string? exiftoolPath;

    [ObservableProperty] private long bytesIn;
    [ObservableProperty] private long bytesOut;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishedCount), nameof(ProgressText))]
    private int doneCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishedCount), nameof(ProgressText), nameof(FailedText), nameof(HasFailures))]
    private int failedCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText), nameof(HasBatch))]
    private int batchTotal;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishedCount), nameof(ProgressText), nameof(SkippedText), nameof(HasSkipped))]
    private int skippedCount;

    // Animated "space saved" counter
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavedText), nameof(SavedBrush), nameof(SavedLabel), nameof(PercentText), nameof(HasResults), nameof(BeforeAfterText))]
    private double displayedSaved;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private double displayedPercent;

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private double _fromSaved, _toSaved, _fromPct, _toPct;
    private CancellationTokenSource? _cts;
    private bool _loading;

    public MainViewModel()
    {
        Items.CollectionChanged += (_, _) => RefreshCounts();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, Tick);
        LoadSettings();
        RefreshTools();
    }

    // MARK: Derived text

    public long Saved => BytesIn - BytesOut;
    public int FinishedCount => DoneCount + FailedCount + SkippedCount;
    public int PendingCount => Items.Count(i => i.Status == ItemStatus.Pending);
    public bool HasItems => Items.Count > 0;
    public bool CanEditList => !IsRunning && HasItems;
    public bool CanConvert => !IsRunning && PendingCount > 0 && CjxlPath != null;
    public bool CjxlMissing => CjxlPath == null;
    public bool ShowExiftoolWarning => KeepMetadata && ExiftoolPath == null;
    public bool OutputNextToOriginals => OutputFolder.Length == 0;
    public bool HasOutputFolder => OutputFolder.Length > 0;
    public bool HasResults => BytesIn > 0;
    public bool HasBatch => BatchTotal > 0;
    public bool HasFailures => FailedCount > 0;
    public bool HasSkipped => SkippedCount > 0;
    public bool HasTrashed => TrashedCount > 0;
    public string TrashedText => $"🗑 {TrashedCount} original{(TrashedCount == 1 ? "" : "s")} moved to {Trash.Name}";
    public string OriginalsHint => DeleteOriginals
        ? $"When all conversions finish, originals of successfully converted files go to the {Trash.Name}."
        : "Original files are never modified.";
    public IBrush OriginalsHintBrush => DeleteOriginals ? Palette.Orange : Palette.Muted;

    public string ItemCountText => HasItems ? $"{Items.Count} images" : "";
    public string ConvertButtonText => PendingCount > 0 ? $"Convert {PendingCount} to JPEG XL" : "Convert to JPEG XL";
    public string SavedLabel => DisplayedSaved < -0.5 ? "SIZE INCREASE" : "SPACE SAVED";
    public string SavedText => (DisplayedSaved < -0.5 ? "+" : "") + Bytes.Format((long)Math.Abs(DisplayedSaved));
    public IBrush SavedBrush => DisplayedSaved < -0.5 ? Palette.Orange : Palette.Green;
    public string PercentText => $"{Math.Abs(DisplayedPercent):0.0}% {(Saved < 0 ? "larger" : "smaller")}";
    public string BeforeAfterText => $"{Bytes.Format(BytesIn)} → {Bytes.Format(BytesOut)}";
    public string ProgressText => $"{FinishedCount} of {BatchTotal} done";
    public string FailedText => $"{FailedCount} failed";
    public string SkippedText => $"{SkippedCount} skipped";
    public string LosslessHint => Lossless
        ? "Pixel-perfect. JPEGs are repacked losslessly (≈20% smaller) and can be restored bit-for-bit."
        : "Lossy. Smaller files; 90 is visually lossless.";
    public string MetadataHint => KeepMetadata
        ? "Keeps camera info, GPS, dates and file timestamps."
        : "Strips EXIF, XMP and JUMBF. Color profile is kept.";
    public string OutputFolderDisplay => OutputFolder;

    private void RefreshCounts()
    {
        foreach (var n in new[] { nameof(PendingCount), nameof(HasItems), nameof(CanEditList), nameof(CanConvert),
                                  nameof(ConvertButtonText), nameof(ItemCountText) })
            OnPropertyChanged(n);
    }

    public void RefreshTools()
    {
        CjxlPath = Tools.Find("cjxl");
        ExiftoolPath = Tools.Find("exiftool");
    }

    // MARK: Adding files

    public void AddFound(IEnumerable<FoundFile> found)
    {
        if (IsRunning) return;
        var known = new HashSet<string>(Items.Select(i => Path.GetFullPath(i.Source)), StringComparer.OrdinalIgnoreCase);
        var added = new List<ConvItem>();
        foreach (var f in found)
        {
            var full = Path.GetFullPath(f.Path);
            if (!Converter.IsSupported(full) || !known.Add(full)) continue;
            added.Add(new ConvItem(full, f.RelativeDir, Converter.FileSize(full)));
        }
        foreach (var item in added.OrderBy(i => i.Source, StringComparer.CurrentCultureIgnoreCase)) Items.Add(item);
    }

    public void Remove(IEnumerable<ConvItem> items)
    {
        if (IsRunning) return;
        foreach (var i in items.ToList()) Items.Remove(i);
    }

    public void Clear()
    {
        if (IsRunning) return;
        Items.Clear();
        ResetStats();
    }

    /// Puts all files back in the queue (e.g. to try different settings).
    public void Requeue()
    {
        if (IsRunning) return;
        // Files whose originals were moved to the trash can't be converted again.
        foreach (var gone in Items.Where(i => i.OriginalTrashed).ToList()) Items.Remove(gone);
        foreach (var i in Items)
        {
            i.Status = ItemStatus.Pending;
            i.NewSize = null;
            i.Output = null;
            i.Message = null;
        }
        ResetStats();
        RefreshCounts();
    }

    private void ResetStats()
    {
        DoneCount = FailedCount = SkippedCount = BatchTotal = TrashedCount = 0;
        BytesIn = BytesOut = 0;
        _timer.Stop();
        DisplayedSaved = DisplayedPercent = 0;
        _toSaved = _toPct = 0;
    }

    // MARK: Converting

    public async Task StartAsync()
    {
        if (!CanConvert || CjxlPath is not { } cjxl) return;
        SaveSettings();
        var jobs = Items.Where(i => i.Status == ItemStatus.Pending).ToList();
        var opts = new ConvertOptions(Lossless, (int)Quality, (int)Effort, KeepMetadata,
                                      OutputFolder.Length > 0 ? OutputFolder : null, Overwrite, SkipLarger);
        var tools = new ToolSet(cjxl, ExiftoolPath);

        IsRunning = true;
        BatchTotal = FinishedCount + jobs.Count;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var limit = Math.Clamp(Environment.ProcessorCount / 2, 1, 6);
        using var gate = new SemaphoreSlim(limit);

        // Continuations resume on the UI thread; only the conversion itself runs in the background.
        var tasks = jobs.Select(async item =>
        {
            await gate.WaitAsync();
            try
            {
                if (ct.IsCancellationRequested)
                {
                    BatchTotal--;
                    return;
                }
                item.Status = ItemStatus.Converting;
                RefreshCounts();
                var outcome = await Task.Run(() => Converter.Convert(item.Source, item.RelativeDir, opts, tools, ct));
                Apply(item, outcome);
            }
            finally { gate.Release(); }
        }).ToList();

        await Task.WhenAll(tasks);
        // Originals are only removed once the whole batch has finished and wasn't stopped.
        if (DeleteOriginals && !ct.IsCancellationRequested) await TrashOriginalsAsync(jobs);
        IsRunning = false;
        RefreshCounts();
    }

    public void Stop() => _cts?.Cancel();

    private async Task TrashOriginalsAsync(IEnumerable<ConvItem> batch)
    {
        // Only originals whose .jxl really exists are removed; skipped and failed files stay.
        var done = batch.Where(i => i.Status == ItemStatus.Done && i.Output != null && Converter.FileSize(i.Output) > 0).ToList();
        foreach (var item in done)
        {
            var ok = await Task.Run(() => Trash.Move(item.Source));
            if (ok)
            {
                item.OriginalTrashed = true;
                TrashedCount++;
            }
            else item.Message = $"Original couldn't be moved to {Trash.Name}";
        }
    }

    private void Apply(ConvItem item, Outcome outcome)
    {
        switch (outcome)
        {
            case Success s:
                item.Status = ItemStatus.Done;
                item.NewSize = s.Size;
                item.Output = s.Output;
                item.Message = s.Note;
                BytesIn += item.OriginalSize;
                BytesOut += s.Size;
                DoneCount++;
                AnimateCounter();
                break;
            case Skipped k:
                item.Status = ItemStatus.Skipped;
                item.Message = k.Message;
                SkippedCount++;
                break;
            case Failure { Message: "Cancelled" }:
                item.Status = ItemStatus.Pending;
                BatchTotal--;
                break;
            case Failure f:
                item.Status = ItemStatus.Failed;
                item.Message = f.Message;
                FailedCount++;
                break;
        }
        RefreshCounts();
    }

    /// Eases the displayed totals toward the new values so the number visibly counts up.
    private void AnimateCounter()
    {
        _fromSaved = DisplayedSaved;
        _fromPct = DisplayedPercent;
        _toSaved = Saved;
        _toPct = BytesIn > 0 ? (double)Saved / BytesIn * 100 : 0;
        _clock.Restart();
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var t = Math.Min(1, _clock.Elapsed.TotalSeconds / 0.6);
        var k = 1 - Math.Pow(1 - t, 3);
        DisplayedSaved = _fromSaved + (_toSaved - _fromSaved) * k;
        DisplayedPercent = _fromPct + (_toPct - _fromPct) * k;
        if (t >= 1) _timer.Stop();
    }

    // MARK: Settings

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JXL Converter", "settings.json");

    private record SettingsData(bool Lossless, double Quality, double Effort, bool KeepMetadata, bool Overwrite, string OutputFolder,
                                bool SkipLarger = true, bool FolderRecursive = true, FileKind[]? FolderExcludedKinds = null);

    private static readonly HashSet<string> SettingNames = new()
        { nameof(Lossless), nameof(Quality), nameof(Effort), nameof(KeepMetadata), nameof(Overwrite), nameof(OutputFolder), nameof(SkipLarger) };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName != null && SettingNames.Contains(e.PropertyName)) SaveSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(SettingsPath));
            if (s == null) return;
            _loading = true;
            Lossless = s.Lossless;
            Quality = Math.Clamp(s.Quality, 50, 99);
            Effort = Math.Clamp(s.Effort, 1, 9);
            KeepMetadata = s.KeepMetadata;
            Overwrite = s.Overwrite;
            OutputFolder = Directory.Exists(s.OutputFolder) ? s.OutputFolder : "";
            SkipLarger = s.SkipLarger;
            FolderRecursive = s.FolderRecursive;
            FolderExcludedKinds = new HashSet<FileKind>(s.FolderExcludedKinds ?? Array.Empty<FileKind>());
        }
        catch { }
        finally { _loading = false; }
    }

    public void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new SettingsData(Lossless, Quality, Effort, KeepMetadata, Overwrite, OutputFolder, SkipLarger,
                                 FolderRecursive, FolderExcludedKinds.ToArray())));
        }
        catch { }
    }
}

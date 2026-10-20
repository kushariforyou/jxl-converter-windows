using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace JxlConverter;

/// Asks whether to include subfolders and which file types to pick up before adding folders.
/// Closes with the matching files, or null if cancelled.
public partial class FolderImportDialog : Window
{
    private readonly IReadOnlyList<string> _folders;
    private readonly MainViewModel _vm;
    private readonly Dictionary<FileKind, CheckBox> _boxes = new();
    private List<FoundFile>? _found;
    private int _scanId;

    public FolderImportDialog() : this(Array.Empty<string>(), new MainViewModel()) { }

    public FolderImportDialog(IReadOnlyList<string> folders, MainViewModel vm)
    {
        InitializeComponent();
        _folders = folders;
        _vm = vm;

        TitleText.Text = folders.Count == 1 ? $"Add “{Path.GetFileName(folders[0].TrimEnd(Path.DirectorySeparatorChar))}”"
                                            : $"Add {folders.Count} folders";
        PathText.Text = string.Join(", ", folders);
        ToolTip.SetTip(PathText, string.Join("\n", folders));

        RecursiveBox.IsChecked = vm.FolderRecursive;
        RecursiveBox.IsCheckedChanged += (_, _) => OptionsChanged();

        foreach (var kind in FileKinds.All)
        {
            var box = new CheckBox
            {
                Content = FileKinds.Title(kind),
                IsChecked = !vm.FolderExcludedKinds.Contains(kind),
                Margin = new Avalonia.Thickness(0, 0, 0, 2),
            };
            ToolTip.SetTip(box, string.Join(", ", FileKinds.Extensions(kind)));
            box.IsCheckedChanged += (_, _) => OptionsChanged();
            _boxes[kind] = box;
            KindsGrid.Children.Add(box);
        }
        OptionsChanged();
    }

    private bool Recursive => RecursiveBox.IsChecked == true;

    private void OptionsChanged()
    {
        RecursiveHint.Text = Recursive ? "Adds images from every folder inside, too." : "Only adds images directly inside the folder.";
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        var id = ++_scanId;
        _found = null;
        AddButton.IsEnabled = false;
        AddButton.Content = "Add";
        CountText.Text = "Scanning…";

        var extensions = new HashSet<string>(
            _boxes.Where(b => b.Value.IsChecked == true).SelectMany(b => FileKinds.Extensions(b.Key)),
            StringComparer.OrdinalIgnoreCase);
        var recursive = Recursive;
        var folders = _folders.ToList();
        var found = await Task.Run(() => folders.SelectMany(f => Converter.ScanFolder(f, recursive, extensions)).ToList());
        if (id != _scanId) return; // options changed while scanning

        _found = found;
        var n = found.Count;
        CountText.Text = n == 0 ? "No matching images" : $"{n} image{(n == 1 ? "" : "s")} found";
        AddButton.Content = n == 0 ? "Add" : $"Add {n} Image{(n == 1 ? "" : "s")}";
        AddButton.IsEnabled = n > 0;
    }

    private void SetAll(bool on)
    {
        foreach (var box in _boxes.Values) box.IsChecked = on;
    }

    private void All_Click(object? sender, RoutedEventArgs e) => SetAll(true);
    private void None_Click(object? sender, RoutedEventArgs e) => SetAll(false);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (_found is not { Count: > 0 }) return;
        _vm.FolderRecursive = Recursive;
        _vm.FolderExcludedKinds = _boxes.Where(b => b.Value.IsChecked != true).Select(b => b.Key).ToHashSet();
        _vm.SaveSettings();
        Close(_found);
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace JxlConverter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow() : this([]) { }

    /// Files dropped onto the .exe (or passed on the command line) start in the list.
    public MainWindow(string[] startupPaths)
    {
        InitializeComponent();
        DataContext = _vm;
        Opened += async (_, _) => await AddPathsAsync(startupPaths);

        DropArea.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropArea.AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropHighlight.IsVisible = false);
        DropArea.AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += OnKeyDown;
    }

    private ConvItem[] Selected => Grid.SelectedItems.OfType<ConvItem>().ToArray();

    /// Files are added straight away; folders first ask about subfolders and file types.
    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        if (_vm.IsRunning) return;
        var list = paths.ToList();
        _vm.AddFound(list.Where(File.Exists).Select(p => new FoundFile(p, "")));
        var folders = list.Where(Directory.Exists).ToList();
        if (folders.Count == 0) return;
        var found = await new FolderImportDialog(folders, _vm).ShowDialog<List<FoundFile>?>(this);
        if (found != null) _vm.AddFound(found);
    }

    // MARK: Drag & drop

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var ok = !_vm.IsRunning && e.Data.Contains(DataFormats.Files);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropHighlight.IsVisible = ok;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropHighlight.IsVisible = false;
        var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>();
        if (paths != null) _ = AddPathsAsync(paths);
    }

    // MARK: Keyboard

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.O)
        {
            AddFolder_Click(null, e);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O)
        {
            AddFiles_Click(null, e);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Enter)
        {
            Convert_Click(null, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Grid.IsKeyboardFocusWithin)
        {
            _vm.Remove(Selected);
            e.Handled = true;
        }
    }

    // MARK: Toolbar

    private async void AddFiles_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.IsRunning) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose images to convert to JPEG XL",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = FileKinds.All.SelectMany(FileKinds.Extensions).Select(x => "*" + x).ToArray(),
                },
            },
        });
        await AddPathsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.IsRunning) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose folders of images to convert",
            AllowMultiple = true,
        });
        await AddPathsAsync(folders.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private void Reset_Click(object? sender, RoutedEventArgs e) => _vm.Requeue();
    private void Clear_Click(object? sender, RoutedEventArgs e) => _vm.Clear();
    private void CheckTools_Click(object? sender, RoutedEventArgs e) => _vm.RefreshTools();

    // MARK: List actions

    private void Reveal_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } item) Tools.Reveal(item.Output ?? item.Source);
    }

    private void Grid_DoubleTapped(object? sender, TappedEventArgs e) => Reveal_Click(sender, e);

    private void Remove_Click(object? sender, RoutedEventArgs e) => _vm.Remove(Selected);

    // MARK: Output folder

    private void OutputNextTo_Click(object? sender, RoutedEventArgs e) => _vm.OutputFolder = "";

    private async void OutputCustom_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.HasOutputFolder) return;
        await PickOutputFolder();
        // Keep the radio buttons in sync if the picker was cancelled.
        if (sender is RadioButton rb) rb.IsChecked = _vm.HasOutputFolder;
    }

    private async void ChangeOutput_Click(object? sender, RoutedEventArgs e) => await PickOutputFolder();

    private async System.Threading.Tasks.Task PickOutputFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to save .jxl files",
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _vm.OutputFolder = path;
    }

    // MARK: Convert

    private async void Convert_Click(object? sender, RoutedEventArgs e) => await _vm.StartAsync();
    private void Stop_Click(object? sender, RoutedEventArgs e) => _vm.Stop();
}

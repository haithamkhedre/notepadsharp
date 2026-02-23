using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using NotepadSharp.App.Dialogs;
using NotepadSharp.App.Services;
using NotepadSharp.App.ViewModels;
using NotepadSharp.Core;

namespace NotepadSharp.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly TextDocumentFileService _fileService = new();
    private readonly AppStateStore _stateStore = new();
    private readonly RecoveryStore _recoveryStore = new();
    private readonly RecoveryManager _recoveryManager;
    private AppState _state;
    private bool _allowClose;
    private ScrollViewer? _editorScrollViewer;
    private readonly TranslateTransform _lineNumbersTransform = new(0, 0);
    private const int DefaultColumnGuide = 100;
    private readonly Stack<ClosedTabSnapshot> _closedTabs = new();

    private sealed record ClosedTabSnapshot(
        string? FilePath,
        string Text,
        string EncodingWebName,
        bool HasBom,
        LineEnding PreferredLineEnding,
        bool WordWrap,
        bool WasDirty,
        DateTimeOffset? FileLastWriteTimeUtc);

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _recoveryManager = new RecoveryManager(_recoveryStore);

        if (EditorTextBox is not null)
        {
            EditorTextBox.PropertyChanged += EditorTextBoxOnPropertyChanged;
            EditorTextBox.TextChanged += (_, __) =>
            {
                UpdateCaretStatus();
                UpdateLineNumbers();
            };
            UpdateCaretStatus();
        }

        if (LineNumbersTextBlock is not null)
        {
            LineNumbersTextBlock.RenderTransform = _lineNumbersTransform;
        }

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedDocument))
            {
                ApplyWordWrap();
                UpdateColumnGuide();
                UpdateLineNumbers();
                UpdateCaretStatus();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.EditorFontSize))
            {
                UpdateColumnGuide();
            }
        };

        _state = _stateStore.Load();
        _viewModel.SetRecentFiles(_state.RecentFiles);
        RefreshOpenRecentMenu();
        _viewModel.RecentFiles.CollectionChanged += (_, __) => RefreshOpenRecentMenu();

        Opened += async (_, __) =>
        {
            await MaybeRecoverAsync();
            await ReopenLastSessionAsync();
            _recoveryManager.Start(() => _viewModel.Documents);

            AttachEditorScrollSync();
            ApplyWordWrap();
            UpdateLineNumbers();
            UpdateColumnGuide();
        };

        Closing += OnWindowClosing;
    }

    private void OnToggleWordWrapClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.WordWrap = !_viewModel.SelectedDocument.WordWrap;
        ApplyWordWrap();
    }

    private void ApplyWordWrap()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var wrap = _viewModel.SelectedDocument?.WordWrap ?? false;
        EditorTextBox.TextWrapping = wrap ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;

        if (ColumnGuide is not null)
        {
            ColumnGuide.IsVisible = !wrap;
        }

        UpdateColumnGuide();
    }

    private void AttachEditorScrollSync()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        if (_editorScrollViewer is not null)
        {
            return;
        }

        _editorScrollViewer = EditorTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_editorScrollViewer is null)
        {
            return;
        }

        _editorScrollViewer.ScrollChanged += (_, __) => SyncGutterToScroll();
        SyncGutterToScroll();
    }

    private void SyncGutterToScroll()
    {
        if (_editorScrollViewer is null)
        {
            return;
        }

        _lineNumbersTransform.Y = -_editorScrollViewer.Offset.Y;
    }

    private void UpdateLineNumbers()
    {
        if (LineNumbersTextBlock is null || EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var lineCount = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineCount++;
            }
        }

        // Render 1..N.
        var sb = new StringBuilder(lineCount * 4);
        for (var ln = 1; ln <= lineCount; ln++)
        {
            sb.Append(ln);
            if (ln != lineCount)
            {
                sb.Append('\n');
            }
        }

        LineNumbersTextBlock.Text = sb.ToString();
    }

    private void UpdateColumnGuide()
    {
        if (ColumnGuide is null || EditorTextBox is null)
        {
            return;
        }

        if (!ColumnGuide.IsVisible)
        {
            return;
        }

        // Approximate monospace-ish character width. This is a guide, not an exact ruler.
        var charWidth = EditorTextBox.FontSize * 0.6;
        var left = EditorTextBox.Padding.Left + (DefaultColumnGuide * charWidth);
        ColumnGuide.Margin = new Thickness(left, 0, 0, 0);
    }

    private void EditorTextBoxOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty)
        {
            UpdateCaretStatus();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var ctrlOrCmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (e.Key == Key.F1 || (ctrlOrCmd && e.Key == Key.OemQuestion))
        {
            _ = ShowKeyboardShortcutsAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_viewModel.IsFindReplaceVisible)
            {
                OnHideFindReplaceClick(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (ctrlOrCmd && e.Key == Key.N)
        {
            _viewModel.NewDocument();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.O)
        {
            OnOpenClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.S)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                OnSaveAsClick(this, new RoutedEventArgs());
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                OnSaveAllClick(this, new RoutedEventArgs());
            }
            else
            {
                OnSaveClick(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.W)
        {
            OnCloseTabClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.T)
        {
            OnReopenClosedTabClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Meta) && e.Key == Key.Q)
        {
            // macOS quit.
            Close();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.F)
        {
            ShowFind(replace: false);
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.H)
        {
            ShowFind(replace: true);
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            var forward = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            FindNext(forward);
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.G)
        {
            _ = ShowGoToLineAsync();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                Redo();
            }
            else
            {
                Undo();
            }

            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.D)
        {
            DuplicateLineOrSelection();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.K)
        {
            DeleteCurrentLine();
            e.Handled = true;
        }
        else if (ctrlOrCmd && (e.Key == Key.Add || e.Key == Key.OemPlus))
        {
            ZoomBy(+1);
            e.Handled = true;
        }
        else if (ctrlOrCmd && (e.Key == Key.Subtract || e.Key == Key.OemMinus))
        {
            ZoomBy(-1);
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.D0)
        {
            ZoomReset();
            e.Handled = true;
        }
    }

    private void OnSaveAllClick(object? sender, RoutedEventArgs e)
        => _ = SaveAllAsync();

    private async Task SaveAllAsync()
    {
        foreach (var doc in _viewModel.Documents.ToList())
        {
            if (!doc.IsDirty)
            {
                continue;
            }

            var before = doc.IsDirty;
            await SaveDocumentAsync(doc);
            if (before && doc.IsDirty)
            {
                // Cancelled.
                break;
            }
        }
    }

    private void OnCloseAllTabsClick(object? sender, RoutedEventArgs e)
        => _ = CloseAllTabsAsync();

    private async Task CloseAllTabsAsync()
    {
        foreach (var doc in _viewModel.Documents.ToList())
        {
            var closed = await CloseDocumentAsync(doc);
            if (!closed)
            {
                return;
            }
        }

        if (_viewModel.Documents.Count == 0)
        {
            _viewModel.NewDocument();
        }
    }

    private async void OnReopenClosedTabClick(object? sender, RoutedEventArgs e)
        => await ReopenClosedTabAsync();

    private async Task ReopenClosedTabAsync()
    {
        if (_closedTabs.Count == 0)
        {
            return;
        }

        var snap = _closedTabs.Pop();
        if (!string.IsNullOrWhiteSpace(snap.FilePath) && File.Exists(snap.FilePath) && !snap.WasDirty)
        {
            await OpenFilePathAsync(snap.FilePath);
            var opened = _viewModel.SelectedDocument;
            if (opened is not null)
            {
                opened.WordWrap = snap.WordWrap;
                opened.SetFileLastWriteTimeUtc(snap.FileLastWriteTimeUtc);
                ApplyWordWrap();
            }

            return;
        }

        var doc = TextDocument.CreateNew();
        doc.FilePath = snap.FilePath;
        try
        {
            doc.Encoding = Encoding.GetEncoding(snap.EncodingWebName);
        }
        catch
        {
            // Ignore.
        }

        doc.HasBom = snap.HasBom;
        doc.PreferredLineEnding = snap.PreferredLineEnding;
        doc.WordWrap = snap.WordWrap;
        doc.SetFileLastWriteTimeUtc(snap.FileLastWriteTimeUtc);
        doc.Text = snap.Text;

        if (!snap.WasDirty)
        {
            doc.MarkSaved();
        }

        ReplaceInitialEmptyDocumentIfNeeded();
        _viewModel.Documents.Add(doc);
        _viewModel.SelectedDocument = doc;
        ApplyWordWrap();
    }

    private void OnDuplicateClick(object? sender, RoutedEventArgs e)
        => DuplicateLineOrSelection();

    private void OnDeleteLineClick(object? sender, RoutedEventArgs e)
        => DeleteCurrentLine();

    private void OnUppercaseClick(object? sender, RoutedEventArgs e)
        => TransformSelection(static s => s.ToUpperInvariant());

    private void OnLowercaseClick(object? sender, RoutedEventArgs e)
        => TransformSelection(static s => s.ToLowerInvariant());

    private void TransformSelection(Func<string, string> transform)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        if (EditorTextBox.SelectionEnd <= EditorTextBox.SelectionStart)
        {
            return;
        }

        var start = EditorTextBox.SelectionStart;
        var end = EditorTextBox.SelectionEnd;
        var text = EditorTextBox.Text ?? string.Empty;
        var selected = EditorTextBox.SelectedText ?? string.Empty;

        var replacement = transform(selected);
        var newText = text.Substring(0, start) + replacement + text.Substring(end);
        EditorTextBox.Text = newText;
        EditorTextBox.SelectionStart = start;
        EditorTextBox.SelectionEnd = start + replacement.Length;
    }

    private void DuplicateLineOrSelection()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var selStart = EditorTextBox.SelectionStart;
        var selEnd = EditorTextBox.SelectionEnd;

        if (selEnd > selStart)
        {
            var selected = EditorTextBox.SelectedText ?? string.Empty;
            var insertAt = selEnd;
            var newText = text.Substring(0, insertAt) + selected + text.Substring(insertAt);
            EditorTextBox.Text = newText;
            SelectMatch(insertAt, selected.Length);
            return;
        }

        var caret = Math.Clamp(EditorTextBox.CaretIndex, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }
        else
        {
            lineEnd += 1; // include newline
        }

        var line = text.Substring(lineStart, lineEnd - lineStart);
        var insertPos = lineEnd;
        var text2 = text.Substring(0, insertPos) + line + text.Substring(insertPos);
        EditorTextBox.Text = text2;
        EditorTextBox.CaretIndex = insertPos + line.Length;
    }

    private void DeleteCurrentLine()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var caret = Math.Clamp(EditorTextBox.CaretIndex, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }
        else
        {
            lineEnd += 1;
        }

        var newText = text.Substring(0, lineStart) + text.Substring(lineEnd);
        EditorTextBox.Text = newText;
        EditorTextBox.CaretIndex = Math.Min(lineStart, newText.Length);
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
        => ZoomBy(+1);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
        => ZoomBy(-1);

    private void OnZoomResetClick(object? sender, RoutedEventArgs e)
        => ZoomReset();

    private void ZoomBy(int delta)
    {
        var size = _viewModel.EditorFontSize;
        size += delta;
        if (size < 8) size = 8;
        if (size > 48) size = 48;
        _viewModel.EditorFontSize = size;
    }

    private void ZoomReset()
        => _viewModel.EditorFontSize = 14;

    private async void OnKeyboardShortcutsClick(object? sender, RoutedEventArgs e)
        => await ShowKeyboardShortcutsAsync();

    private void OnCutClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Cut();

    private void OnCopyClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Copy();

    private void OnPasteClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Paste();

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.SelectAll();

    private async Task ShowKeyboardShortcutsAsync()
    {
        var dialog = new KeyboardShortcutsDialog();
        await dialog.ShowDialog(this);
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e)
        => Undo();

    private void OnRedoClick(object? sender, RoutedEventArgs e)
        => Redo();

    private void Undo()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Undo();
        UpdateCaretStatus();
    }

    private void Redo()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Redo();
        UpdateCaretStatus();
    }

    private void OnNewClick(object? sender, RoutedEventArgs e)
        => _viewModel.NewDocument();

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var provider = StorageProvider;
        if (provider is null)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open",
            AllowMultiple = false,
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        await OpenFileAsync(file);
    }

    private async Task OpenFilePathAsync(string filePath)
    {
        var existing = _viewModel.Documents.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.FilePath) &&
            string.Equals(Path.GetFullPath(d.FilePath!), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _viewModel.SelectedDocument = existing;
            return;
        }

        await using var input = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var doc = await _fileService.LoadAsync(input, filePath: filePath);
        StampFileWriteTimeIfPossible(doc);

        ReplaceInitialEmptyDocumentIfNeeded();
        _viewModel.Documents.Add(doc);
        _viewModel.SelectedDocument = doc;

        _viewModel.AddRecentFile(filePath);
        PersistState();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var doc = _viewModel.SelectedDocument;
        if (doc is null)
        {
            return;
        }

        await SaveDocumentAsync(doc);
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var doc = _viewModel.SelectedDocument;
        if (doc is null)
        {
            return;
        }

        await SaveAsAsync(doc);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
        => Close();

    private async void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        var doc = _viewModel.SelectedDocument;
        if (doc is null)
        {
            return;
        }

        var closed = await CloseDocumentAsync(doc);
        if (!closed)
        {
            return;
        }

        if (_viewModel.Documents.Count == 0)
        {
            _viewModel.NewDocument();
        }
    }

    private async void OnCloseTabButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.DataContext is not TextDocument doc)
        {
            return;
        }

        e.Handled = true;

        var closed = await CloseDocumentAsync(doc);
        if (!closed)
        {
            return;
        }

        if (_viewModel.Documents.Count == 0)
        {
            _viewModel.NewDocument();
        }
    }

    private async Task OpenFileAsync(IStorageFile file)
    {
        await using var input = await file.OpenReadAsync();
        var path = file.Path?.LocalPath;
        var doc = await _fileService.LoadAsync(input, filePath: path);

        StampFileWriteTimeIfPossible(doc);

        ReplaceInitialEmptyDocumentIfNeeded();

        _viewModel.Documents.Add(doc);
        _viewModel.SelectedDocument = doc;

        _viewModel.AddRecentFile(path);
        PersistState();
    }

    private async Task SaveAsAsync(TextDocument doc)
    {
        var provider = StorageProvider;
        if (provider is null)
        {
            return;
        }

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = string.IsNullOrWhiteSpace(doc.FilePath) ? "Untitled.txt" : Path.GetFileName(doc.FilePath),
        });

        if (file is null)
        {
            return;
        }

        var localPath = file.Path?.LocalPath;
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            doc.FilePath = localPath;
            await _fileService.SaveToFileAsync(doc, localPath);
        }
        else
        {
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek)
            {
                output.SetLength(0);
            }

            await _fileService.SaveAsync(doc, output);
        }

        _viewModel.AddRecentFile(doc.FilePath);
        PersistState();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            _recoveryManager.Dispose();
            return;
        }

        // We'll close manually after async prompts.
        e.Cancel = true;

        foreach (var doc in _viewModel.Documents.ToList())
        {
            if (!doc.IsDirty)
            {
                continue;
            }

            var closed = await CloseDocumentAsync(doc);
            if (!closed)
            {
                return;
            }
        }

        _allowClose = true;

        _recoveryManager.Dispose();

        PersistState();
        Close();
    }

    private async Task<bool> CloseDocumentAsync(TextDocument doc)
    {
        if (doc.IsDirty)
        {
            var choice = await PromptUnsavedChangesAsync(doc);
            switch (choice)
            {
                case UnsavedChangesChoice.Cancel:
                    return false;
                case UnsavedChangesChoice.DontSave:
                    break;
                case UnsavedChangesChoice.Save:
                    // Save might open a Save As picker.
                    var before = doc.IsDirty;
                    await SaveDocumentAsync(doc);
                    if (before && doc.IsDirty)
                    {
                        // Save was cancelled or failed.
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }

        var index = _viewModel.Documents.IndexOf(doc);

        _closedTabs.Push(new ClosedTabSnapshot(
            FilePath: doc.FilePath,
            Text: doc.Text ?? string.Empty,
            EncodingWebName: doc.Encoding.WebName,
            HasBom: doc.HasBom,
            PreferredLineEnding: doc.PreferredLineEnding,
            WordWrap: doc.WordWrap,
            WasDirty: doc.IsDirty,
            FileLastWriteTimeUtc: doc.FileLastWriteTimeUtc));

        _viewModel.Documents.Remove(doc);

        _recoveryManager.OnDocumentClosed(doc);

        if (ReferenceEquals(_viewModel.SelectedDocument, doc))
        {
            _viewModel.SelectedDocument = _viewModel.Documents.Count == 0
                ? null
                : _viewModel.Documents[Math.Clamp(index, 0, _viewModel.Documents.Count - 1)];
        }

        return true;
    }

    private async Task SaveDocumentAsync(TextDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.FilePath) && File.Exists(doc.FilePath))
        {
            var choice = await CheckForExternalFileChangeAsync(doc);
            if (choice == FileChangedOnDiskChoice.Cancel)
            {
                return;
            }

            if (choice == FileChangedOnDiskChoice.Reload)
            {
                await ReloadFromDiskAsync(doc);
                return;
            }

            await _fileService.SaveToFileAsync(doc, doc.FilePath);
            _recoveryManager.OnDocumentSaved(doc);
            _viewModel.AddRecentFile(doc.FilePath);
            PersistState();
            return;
        }

        await SaveAsAsync(doc);

        if (!doc.IsDirty)
        {
            _recoveryManager.OnDocumentSaved(doc);
        }
    }

    private async Task MaybeRecoverAsync()
    {
        var files = _recoveryStore.ListSnapshotFiles();
        if (files.Count == 0)
        {
            return;
        }

        var dialog = new RecoveryDialog();
        var restore = await dialog.ShowDialog<bool>(this);

        if (!restore)
        {
            foreach (var file in files)
            {
                _recoveryStore.DeleteFile(file);
            }

            return;
        }

        foreach (var file in files)
        {
            var snap = await _recoveryStore.LoadAsync(file);
            if (snap is null)
            {
                continue;
            }

            var doc = TextDocument.CreateNew();
            if (!string.IsNullOrWhiteSpace(snap.FilePath))
            {
                doc.FilePath = snap.FilePath;
            }

            try
            {
                doc.Encoding = System.Text.Encoding.GetEncoding(snap.EncodingWebName);
            }
            catch
            {
                // Fallback.
            }

            doc.HasBom = snap.HasBom;
            doc.PreferredLineEnding = snap.PreferredLineEnding;
            doc.SetFileLastWriteTimeUtc(snap.FileLastWriteTimeUtc);
            doc.Text = snap.Text ?? string.Empty;

            ReplaceInitialEmptyDocumentIfNeeded();
            _viewModel.Documents.Add(doc);
            _viewModel.SelectedDocument = doc;
        }
    }

    private async Task ReloadFromDiskAsync(TextDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            return;
        }

        await using var input = File.Open(doc.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await _fileService.ReloadAsync(doc, input, filePath: doc.FilePath);
        StampFileWriteTimeIfPossible(doc);
    }

    private async Task<FileChangedOnDiskChoice> CheckForExternalFileChangeAsync(TextDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            return FileChangedOnDiskChoice.Overwrite;
        }

        if (doc.FileLastWriteTimeUtc is null)
        {
            StampFileWriteTimeIfPossible(doc);
            return FileChangedOnDiskChoice.Overwrite;
        }

        var current = File.GetLastWriteTimeUtc(doc.FilePath);
        if (doc.FileLastWriteTimeUtc.Value.UtcDateTime == current)
        {
            return FileChangedOnDiskChoice.Overwrite;
        }

        var dialog = new FileChangedOnDiskDialog();
        return await dialog.ShowDialog<FileChangedOnDiskChoice>(this);
    }

    private static void StampFileWriteTimeIfPossible(TextDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            return;
        }

        var utc = DateTime.SpecifyKind(File.GetLastWriteTimeUtc(doc.FilePath), DateTimeKind.Utc);
        doc.SetFileLastWriteTimeUtc(new DateTimeOffset(utc));
    }

    private async Task<UnsavedChangesChoice> PromptUnsavedChangesAsync()
    {
        return await PromptUnsavedChangesAsync(_viewModel.SelectedDocument);
    }

    private async Task<UnsavedChangesChoice> PromptUnsavedChangesAsync(TextDocument? doc)
    {
        var name = doc is null ? "this document" : doc.DisplayName.TrimEnd('*');
        var dialog = new UnsavedChangesDialog($"Save changes to {name}?");
        return await dialog.ShowDialog<UnsavedChangesChoice>(this);
    }

    private void PersistState()
    {
        _state.RecentFiles = _viewModel.RecentFiles.ToList();
        _state.LastSessionFiles = _viewModel.GetSessionFilePaths().ToList();
        _stateStore.Save(_state);
    }

    private void ReplaceInitialEmptyDocumentIfNeeded()
    {
        if (_viewModel.Documents.Count != 1)
        {
            return;
        }

        var first = _viewModel.Documents[0];
        if (!string.IsNullOrWhiteSpace(first.FilePath))
        {
            return;
        }

        if (first.IsDirty)
        {
            return;
        }

        if (!string.IsNullOrEmpty(first.Text))
        {
            return;
        }

        _viewModel.Documents.Clear();
        _viewModel.SelectedDocument = null;
    }

    private async Task ReopenLastSessionAsync()
    {
        if (_state.LastSessionFiles is null || _state.LastSessionFiles.Count == 0)
        {
            return;
        }

        // Only reopen if we haven't already opened something.
        if (_viewModel.Documents.Any(d => !string.IsNullOrWhiteSpace(d.FilePath)))
        {
            return;
        }

        foreach (var file in _state.LastSessionFiles.ToList())
        {
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                await OpenFilePathAsync(file);
            }
            catch
            {
                // Ignore broken session entries.
            }
        }
    }

    private void RefreshOpenRecentMenu()
    {
        if (OpenRecentMenuItem is null)
        {
            return;
        }

        var items = new List<object>();
        foreach (var path in _viewModel.RecentFiles)
        {
            var p = path;
            var item = new MenuItem { Header = p };
            item.Click += async (_, __) =>
            {
                try
                {
                    if (File.Exists(p))
                    {
                        await OpenFilePathAsync(p);
                    }
                }
                catch
                {
                    // Ignore.
                }
            };
            items.Add(item);
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
        }

        items.Add(new Separator());

        var clear = new MenuItem { Header = "Clear Recent Files", IsEnabled = _viewModel.RecentFiles.Count > 0 };
        clear.Click += (_, __) =>
        {
            _viewModel.RecentFiles.Clear();
            PersistState();
            RefreshOpenRecentMenu();
        };
        items.Add(clear);

        OpenRecentMenuItem.ItemsSource = items;
    }

    private void OnShowFindClick(object? sender, RoutedEventArgs e)
        => ShowFind(replace: false);

    private void OnShowReplaceClick(object? sender, RoutedEventArgs e)
        => ShowFind(replace: true);

    private async void OnGoToLineClick(object? sender, RoutedEventArgs e)
        => await ShowGoToLineAsync();

    private void OnHideFindReplaceClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsFindReplaceVisible = false;
        _viewModel.IsReplaceVisible = false;
        EditorTextBox?.Focus();
    }

    private void ShowFind(bool replace)
    {
        _viewModel.IsFindReplaceVisible = true;
        _viewModel.IsReplaceVisible = replace;

        // Pre-fill find with current selection if any.
        if (EditorTextBox is not null && EditorTextBox.SelectionEnd > EditorTextBox.SelectionStart)
        {
            var selected = EditorTextBox.SelectedText;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _viewModel.FindText = selected;
            }
        }

        (FindTextBox as Control)?.Focus();
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
        => FindNext(forward: true);

    private void OnFindPrevClick(object? sender, RoutedEventArgs e)
        => FindNext(forward: false);

    private void FindNext(bool forward)
    {
        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query) || EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var startIndex = GetSearchStartIndex(forward);
        var match = FindMatch(text, query, startIndex, forward, _viewModel.MatchCase, _viewModel.WholeWord, _viewModel.UseRegex, _viewModel.WrapAround);
        if (match is null)
        {
            return;
        }

        SelectMatch(match.Value.index, match.Value.length);
    }

    private int GetSearchStartIndex(bool forward)
    {
        if (EditorTextBox is null)
        {
            return 0;
        }

        if (forward)
        {
            var start = Math.Max(EditorTextBox.SelectionStart, 0);
            if (EditorTextBox.SelectionEnd > EditorTextBox.SelectionStart)
            {
                start = EditorTextBox.SelectionEnd;
            }

            return start;
        }

        var s = Math.Max(EditorTextBox.SelectionStart, 0);
        if (s > 0)
        {
            s--;
        }

        return s;
    }

    private static (int index, int length)? FindMatch(
        string text,
        string query,
        int startIndex,
        bool forward,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        bool wrapAround)
    {
        if (useRegex)
        {
            var regex = TryCreateRegex(query, matchCase, wholeWord);
            if (regex is null)
            {
                return null;
            }

            return forward
                ? FindNextRegex(text, regex, startIndex, wrapAround)
                : FindPrevRegex(text, regex, startIndex, wrapAround);
        }

        return forward
            ? FindNextPlain(text, query, startIndex, matchCase, wholeWord, wrapAround)
            : FindPrevPlain(text, query, startIndex, matchCase, wholeWord, wrapAround);
    }

    private static Regex? TryCreateRegex(string pattern, bool matchCase, bool wholeWord)
    {
        try
        {
            if (wholeWord)
            {
                pattern = $"(?<!\\w)(?:{pattern})(?!\\w)";
            }

            var options = RegexOptions.Compiled;
            if (!matchCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            return new Regex(pattern, options);
        }
        catch
        {
            return null;
        }
    }

    private static (int index, int length)? FindNextRegex(string text, Regex regex, int startIndex, bool wrapAround)
    {
        startIndex = Math.Clamp(startIndex, 0, text.Length);

        var m = regex.Match(text, startIndex);
        if (m.Success)
        {
            return (m.Index, m.Length);
        }

        if (!wrapAround)
        {
            return null;
        }

        m = regex.Match(text, 0);
        return m.Success ? (m.Index, m.Length) : null;
    }

    private static (int index, int length)? FindPrevRegex(string text, Regex regex, int startIndex, bool wrapAround)
    {
        startIndex = Math.Clamp(startIndex, 0, Math.Max(0, text.Length - 1));

        Match? last = null;
        foreach (Match m in regex.Matches(text))
        {
            if (!m.Success)
            {
                continue;
            }

            if (m.Index > startIndex)
            {
                break;
            }

            last = m;
        }

        if (last is not null)
        {
            return (last.Index, last.Length);
        }

        if (!wrapAround)
        {
            return null;
        }

        // Wrap: take the last match in the document.
        Match? final = null;
        foreach (Match m in regex.Matches(text))
        {
            if (m.Success)
            {
                final = m;
            }
        }

        return final is null ? null : (final.Index, final.Length);
    }

    private static (int index, int length)? FindNextPlain(string text, string query, int startIndex, bool matchCase, bool wholeWord, bool wrapAround)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        startIndex = Math.Clamp(startIndex, 0, text.Length);

        var idx = startIndex;
        while (idx <= text.Length)
        {
            var next = text.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, next, query.Length))
            {
                return (next, query.Length);
            }

            idx = next + 1;
        }

        if (!wrapAround)
        {
            return null;
        }

        idx = 0;
        while (idx < startIndex)
        {
            var next = text.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, next, query.Length))
            {
                return (next, query.Length);
            }

            idx = next + 1;
        }

        return null;
    }

    private static (int index, int length)? FindPrevPlain(string text, string query, int startIndex, bool matchCase, bool wholeWord, bool wrapAround)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (text.Length == 0)
        {
            return null;
        }

        startIndex = Math.Clamp(startIndex, 0, text.Length - 1);

        var idx = startIndex;
        while (idx >= 0)
        {
            var prev = text.LastIndexOf(query, idx, comparison);
            if (prev < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, prev, query.Length))
            {
                return (prev, query.Length);
            }

            idx = prev - 1;
        }

        if (!wrapAround)
        {
            return null;
        }

        idx = text.Length - 1;
        while (idx > startIndex)
        {
            var prev = text.LastIndexOf(query, idx, comparison);
            if (prev < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, prev, query.Length))
            {
                return (prev, query.Length);
            }

            idx = prev - 1;
        }

        return null;
    }

    private static bool IsWholeWordAt(string text, int index, int length)
    {
        var leftOk = index == 0 || !IsWordChar(text[index - 1]);
        var rightIndex = index + length;
        var rightOk = rightIndex >= text.Length || !IsWordChar(text[rightIndex]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private void SelectMatch(int index, int length)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Focus();
        EditorTextBox.SelectionStart = index;
        EditorTextBox.SelectionEnd = index + length;
        EditorTextBox.CaretIndex = index + length;
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
        => ReplaceOnce();

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
        => ReplaceAll();

    private void OnSetLineEndingLfClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.Lf;
    }

    private void OnSetLineEndingCrLfClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.CrLf;
    }

    private void OnSetLineEndingCrClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.Cr;
    }

    private void OnSetEncodingUtf8Click(object? sender, RoutedEventArgs e)
        => SetEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), hasBom: false);

    private void OnSetEncodingUtf8BomClick(object? sender, RoutedEventArgs e)
        => SetEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), hasBom: true);

    private void OnSetEncodingUtf16LeClick(object? sender, RoutedEventArgs e)
        => SetEncoding(Encoding.Unicode, hasBom: true);

    private void OnSetEncodingUtf16BeClick(object? sender, RoutedEventArgs e)
        => SetEncoding(Encoding.BigEndianUnicode, hasBom: true);

    private void SetEncoding(Encoding encoding, bool hasBom)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.Encoding = encoding;
        _viewModel.SelectedDocument.HasBom = hasBom;
    }

    private void ReplaceOnce()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var selection = EditorTextBox.SelectedText ?? string.Empty;
        var replacementRaw = _viewModel.ReplaceText ?? string.Empty;

        if (EditorTextBox.SelectionEnd > EditorTextBox.SelectionStart && selection.Length > 0)
        {
            var start = EditorTextBox.SelectionStart;
            var end = EditorTextBox.SelectionEnd;
            var text = EditorTextBox.Text ?? string.Empty;

            var replacement = GetReplacementIfSelectionMatches(text, query, replacementRaw, start, end - start);
            if (replacement is not null)
            {
                var newText = text.Substring(0, start) + replacement + text.Substring(end);
                EditorTextBox.Text = newText;
                SelectMatch(start, replacement.Length);
                FindNext(forward: true);
                return;
            }
        }

        FindNext(forward: true);
    }

    private string? GetReplacementIfSelectionMatches(string text, string query, string replacementRaw, int selectionStart, int selectionLength)
    {
        if (_viewModel.UseRegex)
        {
            var regex = TryCreateRegex(query, _viewModel.MatchCase, _viewModel.WholeWord);
            if (regex is null)
            {
                return null;
            }

            var m = regex.Match(text, Math.Clamp(selectionStart, 0, text.Length));
            if (!m.Success || m.Index != selectionStart || m.Length != selectionLength)
            {
                return null;
            }

            try
            {
                return m.Result(replacementRaw);
            }
            catch
            {
                return replacementRaw;
            }
        }

        if (selectionStart < 0 || selectionStart + selectionLength > text.Length)
        {
            return null;
        }

        var selected = text.Substring(selectionStart, selectionLength);
        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!string.Equals(selected, query, comparison))
        {
            return null;
        }

        if (_viewModel.WholeWord && !IsWholeWordAt(text, selectionStart, selectionLength))
        {
            return null;
        }

        return replacementRaw;
    }

    private void ReplaceAll()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var replacement = _viewModel.ReplaceText ?? string.Empty;
        var text = EditorTextBox.Text ?? string.Empty;

        if (_viewModel.UseRegex)
        {
            var regex = TryCreateRegex(query, _viewModel.MatchCase, _viewModel.WholeWord);
            if (regex is null)
            {
                return;
            }

            try
            {
                EditorTextBox.Text = regex.Replace(text, replacement);
            }
            catch
            {
                // Ignore invalid replacement.
            }

            return;
        }

        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var idx = 0;
        var result = new System.Text.StringBuilder(text.Length);
        while (idx < text.Length)
        {
            var next = text.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                result.Append(text, idx, text.Length - idx);
                break;
            }

            if (_viewModel.WholeWord && !IsWholeWordAt(text, next, query.Length))
            {
                result.Append(text, idx, (next - idx) + 1);
                idx = next + 1;
                continue;
            }

            result.Append(text, idx, next - idx);
            result.Append(replacement);
            idx = next + query.Length;
        }

        EditorTextBox.Text = result.ToString();
    }

    private async Task ShowGoToLineAsync()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var currentLine = GetCaretLineNumber(EditorTextBox.Text ?? string.Empty, EditorTextBox.CaretIndex);
        var dialog = new GoToLineDialog(currentLine);
        var line = await dialog.ShowDialog<int?>(this);
        if (line is null)
        {
            return;
        }

        GoToLine(EditorTextBox, line.Value);
    }

    private static int GetCaretLineNumber(string text, int caretIndex)
    {
        if (caretIndex < 0)
        {
            caretIndex = 0;
        }

        if (caretIndex > text.Length)
        {
            caretIndex = text.Length;
        }

        var line = 1;
        for (var i = 0; i < caretIndex && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static void GoToLine(TextBox editor, int lineNumber)
    {
        if (lineNumber <= 0)
        {
            return;
        }

        var text = editor.Text ?? string.Empty;
        // Our Core normalizes to \n; editor text follows that.
        var targetLine = 1;
        var index = 0;

        while (targetLine < lineNumber && index < text.Length)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0)
            {
                // Requested line past end; clamp to last line start.
                break;
            }

            index = next + 1;
            targetLine++;
        }

        editor.Focus();
        editor.SelectionStart = index;
        editor.SelectionEnd = index;
        editor.CaretIndex = index;
    }

    private void UpdateCaretStatus()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = EditorTextBox.CaretIndex;
        var (line, col) = GetLineColumn(text, caret);
        var selectionLength = Math.Max(0, EditorTextBox.SelectionEnd - EditorTextBox.SelectionStart);
        _viewModel.SetCaretPosition(line, col, selectionLength);
    }

    private static (int line, int column) GetLineColumn(string text, int caretIndex)
    {
        if (caretIndex < 0) caretIndex = 0;
        if (caretIndex > text.Length) caretIndex = text.Length;

        var line = 1;
        var col = 1;

        for (var i = 0; i < caretIndex; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }

        return (line, col);
    }
}
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
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
    private AppState _state;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        if (EditorTextBox is not null)
        {
            EditorTextBox.PropertyChanged += EditorTextBoxOnPropertyChanged;
            EditorTextBox.TextChanged += (_, __) => UpdateCaretStatus();
            UpdateCaretStatus();
        }

        _state = _stateStore.Load();
        _viewModel.SetRecentFiles(_state.RecentFiles);
        RefreshOpenRecentMenu();
        _viewModel.RecentFiles.CollectionChanged += (_, __) => RefreshOpenRecentMenu();

        Opened += async (_, __) => await ReopenLastSessionAsync();

        Closing += OnWindowClosing;
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
    }

    private async void OnKeyboardShortcutsClick(object? sender, RoutedEventArgs e)
        => await ShowKeyboardShortcutsAsync();

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

        if (!string.IsNullOrWhiteSpace(doc.FilePath) && File.Exists(doc.FilePath))
        {
            await using var output = File.Open(doc.FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await _fileService.SaveAsync(doc, output);
            _viewModel.AddRecentFile(doc.FilePath);
            PersistState();
            return;
        }

        await SaveAsAsync(doc);
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

    private async Task OpenFileAsync(IStorageFile file)
    {
        await using var input = await file.OpenReadAsync();
        var path = file.Path?.LocalPath;
        var doc = await _fileService.LoadAsync(input, filePath: path);

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

        await using var output = await file.OpenWriteAsync();
        if (output.CanSeek)
        {
            output.SetLength(0);
        }

        await _fileService.SaveAsync(doc, output);
        doc.FilePath = file.Path?.LocalPath;

        _viewModel.AddRecentFile(doc.FilePath);
        PersistState();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
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
        _viewModel.Documents.Remove(doc);

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
            await using var output = File.Open(doc.FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await _fileService.SaveAsync(doc, output);
            _viewModel.AddRecentFile(doc.FilePath);
            PersistState();
            return;
        }

        await SaveAsAsync(doc);
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

        var items = new List<MenuItem>();
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

        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int start;
        if (forward)
        {
            start = Math.Max(EditorTextBox.SelectionStart, 0);
            if (EditorTextBox.SelectionEnd > EditorTextBox.SelectionStart)
            {
                start = EditorTextBox.SelectionEnd;
            }

            var idx = text.IndexOf(query, start, comparison);
            if (idx < 0)
            {
                idx = text.IndexOf(query, 0, comparison);
            }

            if (idx >= 0)
            {
                SelectMatch(idx, query.Length);
            }
        }
        else
        {
            start = Math.Max(EditorTextBox.SelectionStart, 0);
            if (text.Length == 0)
            {
                return;
            }

            if (start >= text.Length)
            {
                start = text.Length - 1;
            }

            var idx = text.LastIndexOf(query, start, comparison);
            if (idx < 0)
            {
                idx = text.LastIndexOf(query, text.Length - 1, comparison);
            }

            if (idx >= 0)
            {
                SelectMatch(idx, query.Length);
            }
        }
    }

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
        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (selection.Length > 0 && string.Equals(selection, query, comparison))
        {
            var start = EditorTextBox.SelectionStart;
            var end = EditorTextBox.SelectionEnd;

            var text = EditorTextBox.Text ?? string.Empty;
            var replacement = _viewModel.ReplaceText ?? string.Empty;
            var newText = text.Substring(0, start) + replacement + text.Substring(end);
            EditorTextBox.Text = newText;

            SelectMatch(start, replacement.Length);
            FindNext(forward: true);
            return;
        }

        FindNext(forward: true);
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
        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Simple loop to support case-insensitive replacement.
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
        _viewModel.SetCaretPosition(line, col);
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NotepadSharp.App.Dialogs;
using NotepadSharp.App.Services;
using NotepadSharp.Core;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private async Task OpenFilePathAsync(string filePath, bool deferUiRefresh = false, bool prioritizeLatest = true)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch
        {
            return;
        }

        var openTimer = Stopwatch.StartNew();
        var loadedFromDisk = false;
        CancellationToken cancellationToken = CancellationToken.None;
        CancellationTokenSource? requestCts = null;
        var requestVersion = 0L;

        if (prioritizeLatest)
        {
            if (!_inFlightInteractiveOpenPaths.Add(fullPath))
            {
                return;
            }

            _interactiveOpenFileCts?.Cancel();
            _interactiveOpenFileCts?.Dispose();
            requestCts = new CancellationTokenSource();
            _interactiveOpenFileCts = requestCts;
            cancellationToken = requestCts.Token;
            requestVersion = Interlocked.Increment(ref _interactiveOpenFileVersion);
        }

        try
        {
            var existing = _viewModel.Documents.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.FilePath) &&
                string.Equals(Path.GetFullPath(d.FilePath!), fullPath, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _viewModel.SelectedDocument = existing;
                return;
            }

            await using var input = new FileStream(
                fullPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite,
                    Options = FileOptions.SequentialScan,
                    BufferSize = 128 * 1024,
                });
            var doc = await _fileService.LoadAsync(input, filePath: fullPath, cancellationToken: cancellationToken);
            loadedFromDisk = true;

            if (prioritizeLatest
                && (cancellationToken.IsCancellationRequested
                    || requestVersion != Volatile.Read(ref _interactiveOpenFileVersion)))
            {
                return;
            }

            existing = _viewModel.Documents.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.FilePath) &&
                string.Equals(Path.GetFullPath(d.FilePath!), fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _viewModel.SelectedDocument = existing;
                return;
            }

            StampFileWriteTimeIfPossible(doc);

            ReplaceInitialEmptyDocumentIfNeeded();
            _viewModel.Documents.Add(doc);
            _viewModel.SelectedDocument = doc;

            _viewModel.AddRecentFile(fullPath);
            var shouldRefreshExplorer = false;
            if (string.IsNullOrWhiteSpace(_workspaceRoot))
            {
                _workspaceRoot = NormalizeWorkspaceRoot(Path.GetDirectoryName(fullPath));
                shouldRefreshExplorer = true;
            }

            if (!deferUiRefresh)
            {
                if (shouldRefreshExplorer)
                {
                    RefreshExplorer();
                }

                PersistState();
            }
        }
        catch (OperationCanceledException) when (prioritizeLatest)
        {
            // Ignore stale interactive open requests when a newer click supersedes them.
        }
        finally
        {
            if (loadedFromDisk)
            {
                openTimer.Stop();
                RecordOpenPerf(openTimer.Elapsed.TotalMilliseconds);
            }

            if (prioritizeLatest)
            {
                _inFlightInteractiveOpenPaths.Remove(fullPath);

                if (requestCts is not null && ReferenceEquals(_interactiveOpenFileCts, requestCts))
                {
                    _interactiveOpenFileCts = null;
                }

                requestCts?.Dispose();
            }
        }
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

        var tabDoc = button.FindAncestorOfType<TabItem>()?.DataContext as TextDocument;
        var doc = button.Tag as TextDocument
                  ?? button.DataContext as TextDocument
                  ?? tabDoc
                  ?? _viewModel.SelectedDocument;
        if (doc is null)
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

    private void OnDocumentTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DocumentTabs?.SelectedItem is not TextDocument doc)
        {
            return;
        }

        if (!ReferenceEquals(_viewModel.SelectedDocument, doc))
        {
            _viewModel.SelectedDocument = doc;
        }

        UpdateTabOverflowControls();
    }

    private async Task OpenFileAsync(IStorageFile file)
    {
        var path = file.Path?.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenFilePathAsync(path);
            return;
        }

        var openTimer = Stopwatch.StartNew();
        await using var input = await file.OpenReadAsync();
        var doc = await _fileService.LoadAsync(input, filePath: null);

        StampFileWriteTimeIfPossible(doc);

        ReplaceInitialEmptyDocumentIfNeeded();

        _viewModel.Documents.Add(doc);
        _viewModel.SelectedDocument = doc;

        _viewModel.AddRecentFile(path);
        var shouldRefreshExplorer = false;
        if (string.IsNullOrWhiteSpace(_workspaceRoot) && !string.IsNullOrWhiteSpace(path))
        {
            _workspaceRoot = NormalizeWorkspaceRoot(Path.GetDirectoryName(path));
            shouldRefreshExplorer = true;
        }

        if (shouldRefreshExplorer)
        {
            RefreshExplorer();
        }

        PersistState();
        openTimer.Stop();
        RecordOpenPerf(openTimer.Elapsed.TotalMilliseconds);
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
        if (ReferenceEquals(doc, _viewModel.SelectedDocument))
        {
            InvalidateGitDiffGutterCache();
            UpdateGitDiffGutter();
        }

        PersistState();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _primaryEditorRefreshDebounceCts?.Cancel();
        _primaryEditorRefreshDebounceCts?.Dispose();
        _primaryEditorRefreshDebounceCts = null;
        _splitEditorRefreshDebounceCts?.Cancel();
        _splitEditorRefreshDebounceCts?.Dispose();
        _splitEditorRefreshDebounceCts = null;
        _selectedDocumentRefreshDebounceCts?.Cancel();
        _selectedDocumentRefreshDebounceCts?.Dispose();
        _selectedDocumentRefreshDebounceCts = null;
        _interactiveOpenFileCts?.Cancel();
        _interactiveOpenFileCts?.Dispose();
        _interactiveOpenFileCts = null;
        _inFlightInteractiveOpenPaths.Clear();
        _fullFoldingRebuildCts?.Cancel();
        _fullFoldingRebuildCts?.Dispose();
        _fullFoldingRebuildCts = null;
        _fullFoldingRebuildDocId = Guid.Empty;
        _fullFoldingRebuildVersion = -1;
        _scrollViewportFoldingCts?.Cancel();
        _scrollViewportFoldingCts?.Dispose();
        _scrollViewportFoldingCts = null;

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

        if (ReferenceEquals(_splitDocument, doc))
        {
            _splitDocument = _viewModel.Documents.FirstOrDefault();
            SyncSplitEditorFromDocument();
            RefreshSplitEditorTitle();
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
            if (ReferenceEquals(doc, _viewModel.SelectedDocument))
            {
                InvalidateGitDiffGutterCache();
                UpdateGitDiffGutter();
            }

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

        var snapshots = new List<(string filePath, RecoverySnapshot snapshot)>(files.Count);
        foreach (var file in files)
        {
            var snap = await _recoveryStore.LoadAsync(file);
            if (snap is not null)
            {
                snapshots.Add((file, snap));
            }
        }

        if (snapshots.Count == 0)
        {
            foreach (var file in files)
            {
                _recoveryStore.DeleteFile(file);
            }

            return;
        }

        var candidates = snapshots
            .OrderByDescending(s => s.snapshot.TimestampUtc)
            .Select(s => new RecoveryCandidate(
                FilePath: s.snapshot.FilePath,
                TimestampUtc: s.snapshot.TimestampUtc,
                LineCount: CountLines(s.snapshot.Text)))
            .ToList();

        var dialog = new RecoveryDialog(candidates);
        var choice = await dialog.ShowDialog<RecoveryChoice>(this);

        if (choice == RecoveryChoice.Cancel)
        {
            return;
        }

        if (choice == RecoveryChoice.Discard)
        {
            foreach (var file in files)
            {
                _recoveryStore.DeleteFile(file);
            }

            return;
        }

        foreach (var entry in snapshots.OrderBy(s => s.snapshot.TimestampUtc))
        {
            var snap = entry.snapshot;

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
            _recoveryStore.DeleteFile(entry.filePath);
        }
    }

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
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

    private async Task<string?> PromptTextAsync(string title, string prompt, string? initialValue = null)
    {
        var dialog = new TextInputDialog(title, prompt, initialValue);
        return await dialog.ShowDialog<string?>(this);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message);
        var result = await dialog.ShowDialog<bool>(this);
        return result;
    }

    private void PersistState()
    {
        if (_state is null)
        {
            return;
        }

        _state.RecentFiles = _viewModel.RecentFiles.ToList();
        _state.LastSessionFiles = _viewModel.GetSessionFilePaths().ToList();
        _state.Theme = _themeMode;
        _state.LanguageMode = _languageMode;
        _state.WorkspaceRoot = _workspaceRoot;
        _state.SidebarSection = _sidebarSection;
        _state.SidebarAutoHide = _isSidebarAutoHide;
        _state.SidebarExpanded = _isSidebarExpanded;
        _state.ShowMiniMap = _isMiniMapEnabled;
        _state.SplitViewEnabled = _isSplitViewEnabled;
        _state.SplitCompareMode = _splitCompareMode;
        _state.FoldingEnabled = _isFoldingEnabled;
        _state.ShowAllCharacters = _showAllCharacters;
        _state.ColumnGuideEnabled = _isColumnGuideEnabled;
        _state.ColumnGuideColumn = _columnGuideColumn;
        _state.SidebarWidth = _sidebarWidth;
        _state.TerminalVisible = _isTerminalVisible;
        _state.TerminalHeight = _terminalHeight;
        _state.ShowTabBar = _showTabBar;
        _state.AutoHideTabBar = _autoHideTabBar;
        _state.EditorFontSize = _viewModel.EditorFontSize;
        _state.EditorFontFamily = _editorFontFamily;
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

        var openedAny = false;
        foreach (var file in _state.LastSessionFiles.ToList())
        {
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                await OpenFilePathAsync(file, deferUiRefresh: true, prioritizeLatest: false);
                openedAny = true;
            }
            catch
            {
                // Ignore broken session entries.
            }
        }

        if (openedAny)
        {
            RefreshExplorer();
            PersistState();
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
}

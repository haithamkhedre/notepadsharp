using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Material.Icons;
using NotepadSharp.App.Dialogs;
using NotepadSharp.App.Services;
using NotepadSharp.Core;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private static string? NormalizeWorkspaceRoot(string? rawRoot)
    {
        if (string.IsNullOrWhiteSpace(rawRoot))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(rawRoot);
            return Directory.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureWorkspaceRoot()
    {
        if (!string.IsNullOrWhiteSpace(_workspaceRoot) && Directory.Exists(_workspaceRoot))
        {
            UpdateWorkspaceRootLabel();
            EnsureWorkspaceWatcher();
            return;
        }

        var inferredRoot = NormalizeWorkspaceRoot(InferWorkspaceRootFromContext());
        if (!string.Equals(_workspaceRoot, inferredRoot, StringComparison.OrdinalIgnoreCase))
        {
            _workspaceRoot = inferredRoot;
            InvalidateGitRepositoryRootCache();
            ConfigureWorkspaceWatcher();
        }

        UpdateWorkspaceRootLabel();
        UpdateTerminalCwd();
    }

    private string? InferWorkspaceRootFromContext()
    {
        var selectedPath = _viewModel.SelectedDocument?.FilePath;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            return Path.GetDirectoryName(selectedPath);
        }

        var firstPath = _viewModel.Documents
            .Select(d => d.FilePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        if (!string.IsNullOrWhiteSpace(firstPath))
        {
            return Path.GetDirectoryName(firstPath);
        }

        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch
        {
            return null;
        }
    }

    private void UpdateWorkspaceRootLabel()
    {
        if (WorkspaceRootTextBlock is null)
        {
            return;
        }

        WorkspaceRootTextBlock.Text = string.IsNullOrWhiteSpace(_workspaceRoot)
            ? "No workspace loaded."
            : _workspaceRoot!;
    }

    private async void OnOpenWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        var provider = StorageProvider;
        if (provider is null)
        {
            return;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Workspace Folder",
            AllowMultiple = false,
        });

        var folder = folders.FirstOrDefault();
        var local = folder?.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(local))
        {
            return;
        }

        SetWorkspaceRoot(local, persist: true);
    }

    private void OnRefreshWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        EnsureWorkspaceRoot();
        RefreshExplorer();
    }

    private void SetWorkspaceRoot(string? root, bool persist)
    {
        var normalized = NormalizeWorkspaceRoot(root);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var workspaceChanged = !string.Equals(_workspaceRoot, normalized, StringComparison.OrdinalIgnoreCase);
        if (workspaceChanged)
        {
            InvalidateGitRepositoryRootCache();
            InvalidateGitStatusCache();
        }

        _workspaceRoot = normalized;
        ConfigureWorkspaceWatcher();
        UpdateWorkspaceRootLabel();
        RefreshExplorer();
        if (workspaceChanged)
        {
            ResetTerminalSessionForContextChange("workspace changed");
        }
        UpdateTerminalCwd();

        if (persist)
        {
            PersistState();
        }
    }

    private void EnsureWorkspaceWatcher()
    {
        if (_workspaceWatcher is not null
            && string.Equals(_workspaceWatcher.Path, _workspaceRoot, StringComparison.OrdinalIgnoreCase)
            && _workspaceWatcher.EnableRaisingEvents)
        {
            return;
        }

        ConfigureWorkspaceWatcher();
    }

    private void ConfigureWorkspaceWatcher()
    {
        StopWorkspaceWatcher();
        if (string.IsNullOrWhiteSpace(_workspaceRoot) || !Directory.Exists(_workspaceRoot))
        {
            return;
        }

        try
        {
            _workspaceWatcher = new FileSystemWatcher(_workspaceRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.CreationTime
                    | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            _workspaceWatcher.Changed += OnWorkspaceWatcherChanged;
            _workspaceWatcher.Created += OnWorkspaceWatcherChanged;
            _workspaceWatcher.Deleted += OnWorkspaceWatcherChanged;
            _workspaceWatcher.Renamed += OnWorkspaceWatcherRenamed;
            _workspaceWatcher.Error += OnWorkspaceWatcherError;
        }
        catch
        {
            StopWorkspaceWatcher();
        }
    }

    private void StopWorkspaceWatcher()
    {
        _workspaceRefreshCts?.Cancel();
        _workspaceRefreshCts?.Dispose();
        _workspaceRefreshCts = null;

        if (_workspaceWatcher is null)
        {
            return;
        }

        _workspaceWatcher.EnableRaisingEvents = false;
        _workspaceWatcher.Changed -= OnWorkspaceWatcherChanged;
        _workspaceWatcher.Created -= OnWorkspaceWatcherChanged;
        _workspaceWatcher.Deleted -= OnWorkspaceWatcherChanged;
        _workspaceWatcher.Renamed -= OnWorkspaceWatcherRenamed;
        _workspaceWatcher.Error -= OnWorkspaceWatcherError;
        _workspaceWatcher.Dispose();
        _workspaceWatcher = null;
    }

    private void OnWorkspaceWatcherChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleWorkspaceRefreshFromWatcher(e.FullPath, recreateWatcher: false);
    }

    private void OnWorkspaceWatcherRenamed(object sender, RenamedEventArgs e)
    {
        RemapOpenDocumentPathsAfterRename(e.OldFullPath, e.FullPath, Directory.Exists(e.FullPath));
        ScheduleWorkspaceRefreshFromWatcher(e.OldFullPath, recreateWatcher: false);
        ScheduleWorkspaceRefreshFromWatcher(e.FullPath, recreateWatcher: false);
    }

    private void OnWorkspaceWatcherError(object sender, ErrorEventArgs e)
    {
        ScheduleWorkspaceRefreshFromWatcher(_workspaceRoot, recreateWatcher: true);
    }

    private void ScheduleWorkspaceRefreshFromWatcher(string? changedPath, bool recreateWatcher)
    {
        if (WorkspaceWatcherLogic.ShouldIgnorePath(_workspaceRoot ?? string.Empty, changedPath))
        {
            return;
        }

        _workspaceRefreshCts?.Cancel();
        _workspaceRefreshCts?.Dispose();

        var cts = new CancellationTokenSource();
        _workspaceRefreshCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    if (recreateWatcher)
                    {
                        ConfigureWorkspaceWatcher();
                    }

                    RefreshExplorer();
                    UpdateGitPanel(forceRefresh: true);
                    await RefreshOpenDocumentsFromDiskIfNeededAsync();
                    UpdateGitDiffGutter();
                });
            }
            catch (OperationCanceledException)
            {
                // Ignore superseded watcher refresh work.
            }
            finally
            {
                if (ReferenceEquals(_workspaceRefreshCts, cts))
                {
                    _workspaceRefreshCts = null;
                }

                cts.Dispose();
            }
        });
    }

    private void RefreshExplorer()
    {
        if (ExplorerTreeView is null)
        {
            return;
        }

        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            ExplorerTreeView.ItemsSource = Array.Empty<ExplorerTreeNode>();
            return;
        }

        const int maxNodes = 8000;
        var nodeBudget = maxNodes;
        var fileCount = 0;
        _gitStatusByPath.Clear();
        foreach (var kvp in GetGitStatusByPath())
        {
            _gitStatusByPath[kvp.Key] = kvp.Value;
        }

        _explorerRootNodes.Clear();
        var nodes = BuildExplorerChildNodes(_workspaceRoot!, ref nodeBudget, ref fileCount);
        _explorerRootNodes.AddRange(nodes);
        AttachExplorerNodeObservers(_explorerRootNodes);

        ExplorerTreeView.ItemsSource = _explorerRootNodes.ToList();
        if (WorkspaceRootTextBlock is not null)
        {
            WorkspaceRootTextBlock.Text = $"{_workspaceRoot}  ({fileCount} root files, lazy tree)";
        }

        UpdateGitPanel();
    }

    private List<ExplorerTreeNode> BuildExplorerChildNodes(string directoryPath, ref int nodeBudget, ref int fileCount)
    {
        var nodes = new List<ExplorerTreeNode>();
        if (nodeBudget <= 0)
        {
            return nodes;
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(directoryPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            directories = Array.Empty<string>();
        }

        foreach (var dir in directories)
        {
            if (nodeBudget <= 0)
            {
                break;
            }

            var name = Path.GetFileName(dir);
            if (IgnoredWorkspaceDirectories.Contains(name))
            {
                continue;
            }

            nodeBudget--;
            var node = new ExplorerTreeNode
            {
                Name = name,
                FullPath = dir,
                IsDirectory = true,
                IconKind = MaterialIconKind.FolderOutline,
                GitBadge = GetDirectoryGitBadge(dir),
                IsChildrenLoaded = false,
            };

            if (HasVisibleExplorerChildren(dir))
            {
                node.Children.Add(ExplorerTreeNode.CreatePlaceholder(dir));
            }
            else
            {
                node.IsChildrenLoaded = true;
            }

            nodes.Add(node);
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directoryPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            files = Array.Empty<string>();
        }

        foreach (var file in files)
        {
            if (nodeBudget <= 0)
            {
                break;
            }

            if (!IsExplorerVisibleFile(file))
            {
                continue;
            }

            nodeBudget--;
            fileCount++;
            nodes.Add(new ExplorerTreeNode
            {
                Name = Path.GetFileName(file),
                FullPath = file,
                IsDirectory = false,
                IconKind = GetFileIconKind(file),
                GitBadge = GetGitBadgeForPath(file),
            });
        }

        return nodes;
    }

    private static bool HasVisibleExplorerChildren(string directoryPath)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directoryPath))
            {
                var name = Path.GetFileName(dir);
                if (!IgnoredWorkspaceDirectories.Contains(name))
                {
                    return true;
                }
            }

            foreach (var file in Directory.EnumerateFiles(directoryPath))
            {
                if (IsExplorerVisibleFile(file))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private void AttachExplorerNodeObservers(IEnumerable<ExplorerTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            AttachExplorerNodeObserver(node);
        }
    }

    private void AttachExplorerNodeObserver(ExplorerTreeNode node)
    {
        node.PropertyChanged -= OnExplorerNodePropertyChanged;
        node.PropertyChanged += OnExplorerNodePropertyChanged;
        foreach (var child in node.Children)
        {
            AttachExplorerNodeObserver(child);
        }
    }

    private async void OnExplorerNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ExplorerTreeNode.IsExpanded), StringComparison.Ordinal))
        {
            return;
        }

        if (sender is not ExplorerTreeNode node || !node.IsDirectory || !node.IsExpanded)
        {
            return;
        }

        await EnsureExplorerChildrenLoadedAsync(node);
    }

    private async Task EnsureExplorerChildrenLoadedAsync(ExplorerTreeNode node)
    {
        if (!node.IsDirectory || node.IsChildrenLoaded || node.IsLoadingChildren)
        {
            return;
        }

        node.IsLoadingChildren = true;
        try
        {
            var remainingBudget = 2500;
            var loadedFileCount = 0;
            var children = await Task.Run(() =>
                    BuildExplorerChildNodes(node.FullPath, ref remainingBudget, ref loadedFileCount))
                .ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                node.Children.Clear();
                foreach (var child in children)
                {
                    node.Children.Add(child);
                    AttachExplorerNodeObserver(child);
                }

                node.IsChildrenLoaded = true;
            });
        }
        catch
        {
            // Ignore load failures for inaccessible directories.
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => node.IsLoadingChildren = false);
        }
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var dir in directories)
            {
                var name = Path.GetFileName(dir);
                if (IgnoredWorkspaceDirectories.Contains(name))
                {
                    continue;
                }

                stack.Push(dir);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsSearchableFile(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsSearchableFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext))
        {
            return SearchableExtensions.Contains(ext);
        }

        var fileName = Path.GetFileName(filePath);
        return fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(".env", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplorerVisibleFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string ToRelativePath(string root, string fullPath)
    {
        try
        {
            return Path.GetRelativePath(root, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }

    private async void OnExplorerTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        var item = GetTreeNodeFromEventSource<ExplorerTreeNode>(e) ?? ExplorerTreeView?.SelectedItem as ExplorerTreeNode;
        await OpenExplorerTreeItemAsync(item);
    }

    private async void OnExplorerTreeTapped(object? sender, TappedEventArgs e)
    {
        var item = GetTreeNodeFromEventSource<ExplorerTreeNode>(e) ?? ExplorerTreeView?.SelectedItem as ExplorerTreeNode;
        await OpenExplorerTreeItemAsync(item);
    }

    private async void OnExplorerTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = ExplorerTreeView?.SelectedItem as ExplorerTreeNode;
        await OpenExplorerTreeItemAsync(item);
    }

    private async Task OpenExplorerTreeItemAsync(ExplorerTreeNode? item)
    {
        if (item is null || item.IsDirectory || item.IsPlaceholder)
        {
            return;
        }

        await OpenTreeFileAsync(item.FullPath);
    }

    private ExplorerTreeNode? GetSelectedExplorerNode()
    {
        var selected = ExplorerTreeView?.SelectedItem as ExplorerTreeNode;
        return selected is { IsPlaceholder: false } ? selected : null;
    }

    private string? GetExplorerTargetDirectory()
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            return null;
        }

        var selected = GetSelectedExplorerNode();
        if (selected is null)
        {
            return _workspaceRoot;
        }

        if (selected.IsDirectory)
        {
            return selected.FullPath;
        }

        return Path.GetDirectoryName(selected.FullPath) ?? _workspaceRoot;
    }

    private async void OnExplorerNewFileClick(object? sender, RoutedEventArgs e)
    {
        var directory = GetExplorerTargetDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var fileName = await PromptTextAsync("New File", "Enter file name:");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var fullPath = Path.Combine(directory, fileName);
        try
        {
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (!File.Exists(fullPath))
            {
                await File.WriteAllTextAsync(fullPath, string.Empty);
            }

            RefreshExplorer();
            await OpenFilePathAsync(fullPath);
        }
        catch
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Could not create file.";
            }
        }
    }

    private async void OnExplorerNewFolderClick(object? sender, RoutedEventArgs e)
    {
        var directory = GetExplorerTargetDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var folderName = await PromptTextAsync("New Folder", "Enter folder name:");
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var fullPath = Path.Combine(directory, folderName);
        try
        {
            Directory.CreateDirectory(fullPath);
            RefreshExplorer();
        }
        catch
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Could not create folder.";
            }
        }
    }

    private async void OnExplorerRenameClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedExplorerNode();
        if (selected is null)
        {
            return;
        }

        var nextName = await PromptTextAsync("Rename", $"Rename '{selected.Name}' to:", selected.Name);
        if (string.IsNullOrWhiteSpace(nextName) || string.Equals(nextName, selected.Name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var parent = Path.GetDirectoryName(selected.FullPath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                return;
            }

            var nextPath = Path.Combine(parent, nextName);
            if (selected.IsDirectory)
            {
                Directory.Move(selected.FullPath, nextPath);
            }
            else
            {
                File.Move(selected.FullPath, nextPath);
            }

            RemapOpenDocumentPathsAfterRename(selected.FullPath, nextPath, selected.IsDirectory);

            RefreshExplorer();
            UpdateGitPanel();
            PersistState();
        }
        catch
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Rename failed.";
            }
        }
    }

    private void RemapOpenDocumentPathsAfterRename(string? oldPath, string? newPath, bool oldPathIsDirectory)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            return;
        }

        var updatedSelectedDocument = false;
        var updatedSplitDocument = false;
        foreach (var doc in _viewModel.Documents)
        {
            var remappedPath = OpenDocumentPathRenameLogic.TryRemapPath(doc.FilePath, oldPath, newPath, oldPathIsDirectory);
            if (string.IsNullOrWhiteSpace(remappedPath)
                || string.Equals(remappedPath, doc.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            doc.FilePath = remappedPath;
            doc.SetMissingOnDisk(false);
            _viewModel.AddRecentFile(remappedPath);
            updatedSelectedDocument |= ReferenceEquals(doc, _viewModel.SelectedDocument);
            updatedSplitDocument |= ReferenceEquals(doc, _splitDocument);
        }

        if (updatedSelectedDocument)
        {
            UpdateSettingsControls();
            InvalidateGitDiffGutterCache();
            UpdateGitDiffGutter();
        }

        if (updatedSplitDocument)
        {
            RefreshSplitEditorTitle();
        }
    }

    private async void OnExplorerDeleteClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedExplorerNode();
        if (selected is null)
        {
            return;
        }

        var ok = await ConfirmAsync("Delete", $"Delete '{selected.Name}'?");
        if (!ok)
        {
            return;
        }

        try
        {
            if (selected.IsDirectory)
            {
                Directory.Delete(selected.FullPath, recursive: true);
                foreach (var doc in _viewModel.Documents.ToList())
                {
                    if (string.IsNullOrWhiteSpace(doc.FilePath))
                    {
                        continue;
                    }

                    if (Path.GetFullPath(doc.FilePath!).StartsWith(Path.GetFullPath(selected.FullPath), StringComparison.OrdinalIgnoreCase))
                    {
                        _viewModel.Documents.Remove(doc);
                    }
                }
            }
            else
            {
                File.Delete(selected.FullPath);
                var doc = _viewModel.Documents.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(d.FilePath)
                    && string.Equals(Path.GetFullPath(d.FilePath!), Path.GetFullPath(selected.FullPath), StringComparison.OrdinalIgnoreCase));
                if (doc is not null)
                {
                    _viewModel.Documents.Remove(doc);
                }
            }

            if (_viewModel.Documents.Count == 0)
            {
                _viewModel.NewDocument();
            }

            RefreshExplorer();
            UpdateGitPanel();
        }
        catch
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Delete failed.";
            }
        }
    }

    private void OnExplorerTreeDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = HasDroppedPaths(e);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnExplorerTreeDrop(object? sender, DragEventArgs e)
    {
        var directory = GetExplorerTargetDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var files = GetDroppedPaths(e);
        if (files.Count == 0)
        {
            return;
        }

        foreach (var source in files)
        {
            try
            {
                if (File.Exists(source))
                {
                    var target = Path.Combine(directory, Path.GetFileName(source));
                    File.Copy(source, target, overwrite: true);
                }
                else if (Directory.Exists(source))
                {
                    var target = Path.Combine(directory, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));
                    CopyDirectory(source, target);
                }
            }
            catch
            {
                // Ignore bad drops.
            }
        }

        RefreshExplorer();
        UpdateGitPanel();
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, dest);
        }
    }

    private string? GetGitRepositoryRoot()
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            return null;
        }

        const int cacheTtlMs = 3000;
        if (!string.IsNullOrWhiteSpace(_cachedGitRepoRoot)
            && string.Equals(_cachedGitRepoRootWorkspace, _workspaceRoot, StringComparison.OrdinalIgnoreCase)
            && DateTimeOffset.UtcNow - _cachedGitRepoRootAtUtc < TimeSpan.FromMilliseconds(cacheTtlMs)
            && Directory.Exists(_cachedGitRepoRoot))
        {
            return _cachedGitRepoRoot;
        }

        var result = RunGit(_workspaceRoot!, "rev-parse --show-toplevel", timeoutMs: 1500);
        _cachedGitRepoRootWorkspace = _workspaceRoot;
        _cachedGitRepoRootAtUtc = DateTimeOffset.UtcNow;
        if (result.exitCode != 0)
        {
            _cachedGitRepoRoot = null;
            return null;
        }

        var root = result.stdout.Trim();
        _cachedGitRepoRoot = Directory.Exists(root) ? root : null;
        return _cachedGitRepoRoot;
    }

    private void InvalidateGitRepositoryRootCache()
    {
        _cachedGitRepoRoot = null;
        _cachedGitRepoRootWorkspace = null;
        _cachedGitRepoRootAtUtc = DateTimeOffset.MinValue;
        InvalidateGitStatusCache();
    }

    private void InvalidateGitStatusCache()
    {
        _cachedGitStatusRepoRoot = null;
        _cachedGitStatusText = null;
        _cachedGitStatusAtUtc = DateTimeOffset.MinValue;
    }

    private string? GetGitStatusPorcelain(string repoRoot, bool forceRefresh = false)
    {
        const int cacheTtlMs = 900;
        if (!forceRefresh
            && string.Equals(_cachedGitStatusRepoRoot, repoRoot, StringComparison.OrdinalIgnoreCase)
            && _cachedGitStatusText is not null
            && DateTimeOffset.UtcNow - _cachedGitStatusAtUtc < TimeSpan.FromMilliseconds(cacheTtlMs))
        {
            return _cachedGitStatusText;
        }

        var status = RunGit(repoRoot, "status --porcelain=v1 --untracked-files=all", timeoutMs: 3000);
        if (status.exitCode != 0)
        {
            _cachedGitStatusRepoRoot = repoRoot;
            _cachedGitStatusText = null;
            _cachedGitStatusAtUtc = DateTimeOffset.UtcNow;
            return null;
        }

        var normalized = status.stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        _cachedGitStatusRepoRoot = repoRoot;
        _cachedGitStatusText = normalized;
        _cachedGitStatusAtUtc = DateTimeOffset.UtcNow;
        return normalized;
    }

    private Dictionary<string, string> GetGitStatusByPath()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return result;
        }

        var statusText = GetGitStatusPorcelain(repoRoot);
        if (statusText is null)
        {
            return result;
        }

        foreach (var line in statusText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var code = line.Substring(0, 2).Trim();
            var pathPart = line.Substring(3).Trim();
            if (pathPart.Contains("->", StringComparison.Ordinal))
            {
                var parts = pathPart.Split("->", StringSplitOptions.TrimEntries);
                pathPart = parts.LastOrDefault() ?? pathPart;
            }

            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, pathPart));
            result[fullPath] = GitStatusLogic.MapGitBadge(code);
        }

        return result;
    }

    private string? GetGitBadgeForPath(string fullPath)
        => _gitStatusByPath.TryGetValue(Path.GetFullPath(fullPath), out var badge) ? badge : null;

    private string? GetDirectoryGitBadge(string directoryPath)
    {
        var prefix = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var kvp in _gitStatusByPath)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private static MaterialIconKind GetFileIconKind(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" or ".txt" => MaterialIconKind.FileDocumentOutline,
            ".json" or ".yaml" or ".yml" or ".xml" => MaterialIconKind.CodeJson,
            ".cs" or ".js" or ".ts" or ".py" or ".sql" or ".css" or ".html" => MaterialIconKind.FileCodeOutline,
            _ => MaterialIconKind.FileDocumentOutline,
        };
    }

    private void UpdateGitPanel(bool forceRefresh = false)
    {
        if (GitChangesTreeView is null || GitSummaryTextBlock is null)
        {
            return;
        }

        InvalidateGitDiffGutterCache();
        CancelGitDiffPreview();
        var expansionState = CaptureGitTreeExpansionState(_gitChangeTreeRootNodes);

        _gitChanges.Clear();
        _gitChangeTreeRootNodes.Clear();
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            GitChangesTreeView.ItemsSource = Array.Empty<GitChangeTreeNode>();
            SetGitBranchState("Branch: unavailable");
            UpdateGitOperationUi(null);
            GitSummaryTextBlock.Text = "No git repository detected.";
            SetGitDiffPreviewState("Diff Preview", "No git repository detected.");
            return;
        }

        var operationState = GetGitOperationState(repoRoot);
        var statusText = GetGitStatusPorcelain(repoRoot, forceRefresh: forceRefresh);
        if (statusText is null)
        {
            GitChangesTreeView.ItemsSource = Array.Empty<GitChangeTreeNode>();
            SetGitBranchState(GetGitBranchDisplay(repoRoot));
            UpdateGitOperationUi(operationState);
            GitSummaryTextBlock.Text = "Failed to read git status.";
            SetGitDiffPreviewState("Diff Preview", "Failed to load diff preview because git status could not be read.");
            return;
        }

        var conflictEntries = new List<GitChangeEntryModel>();
        var stagedEntries = new List<GitChangeEntryModel>();
        var unstagedEntries = new List<GitChangeEntryModel>();
        foreach (var line in statusText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var xy = line.Substring(0, 2);
            var pathPart = line.Substring(3).Trim();
            if (pathPart.Contains("->", StringComparison.Ordinal))
            {
                var parts = pathPart.Split("->", StringSplitOptions.TrimEntries);
                pathPart = parts.LastOrDefault() ?? pathPart;
            }

            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, pathPart));
            var relative = ToRelativePath(repoRoot, fullPath);
            if (GitStatusLogic.IsConflictXy(xy))
            {
                conflictEntries.Add(new GitChangeEntryModel
                {
                    Code = xy.Trim(),
                    RelativePath = relative,
                    FullPath = fullPath,
                });

                _gitChanges.Add(new GitChangeEntryModel
                {
                    Code = xy.Trim(),
                    RelativePath = relative,
                    FullPath = fullPath,
                });

                continue;
            }

            var stagedCode = GitStatusLogic.GetStagedCode(xy);
            var unstagedCode = GitStatusLogic.GetUnstagedCode(xy);
            if (stagedCode is not null)
            {
                stagedEntries.Add(new GitChangeEntryModel
                {
                    Code = stagedCode,
                    RelativePath = relative,
                    FullPath = fullPath,
                });
            }

            if (unstagedCode is not null)
            {
                unstagedEntries.Add(new GitChangeEntryModel
                {
                    Code = unstagedCode,
                    RelativePath = relative,
                    FullPath = fullPath,
                });
            }

            _gitChanges.Add(new GitChangeEntryModel
            {
                Code = stagedCode ?? unstagedCode ?? "--",
                RelativePath = relative,
                FullPath = fullPath,
            });
        }

        _gitChangeTreeRootNodes.AddRange(GitStatusLogic.BuildGitChangeTree(repoRoot, conflictEntries, stagedEntries, unstagedEntries));
        RestoreGitTreeExpansionState(_gitChangeTreeRootNodes, expansionState);
        GitChangesTreeView.ItemsSource = _gitChangeTreeRootNodes.ToList();
        GitChangesTreeView.SelectedItem = null;
        SetGitBranchState(GetGitBranchDisplay(repoRoot));
        UpdateGitOperationUi(operationState);
        GitSummaryTextBlock.Text = _gitChanges.Count == 0
            ? "Working tree clean."
            : conflictEntries.Count > 0
                ? $"{_gitChanges.Count} files changed | {conflictEntries.Count} conflicts | {stagedEntries.Count} staged | {unstagedEntries.Count} unstaged"
                : $"{_gitChanges.Count} files changed | {stagedEntries.Count} staged | {unstagedEntries.Count} unstaged";
        SetGitDiffPreviewState("Diff Preview", GetGitDiffPreviewEmptyStateText());
        UpdateGitOpenButtonState(null);
    }

    private void OnGitRefreshClick(object? sender, RoutedEventArgs e)
    {
        RefreshExplorer();
        UpdateGitPanel(forceRefresh: true);
        UpdateGitDiffGutter();
    }


    private void OnGitExpandAllClick(object? sender, RoutedEventArgs e)
    {
        SetGitTreeExpansion(_gitChangeTreeRootNodes, expanded: true);
    }

    private void OnGitCollapseAllClick(object? sender, RoutedEventArgs e)
    {
        SetGitTreeExpansion(_gitChangeTreeRootNodes, expanded: false);
    }

    private void OnGitStageAllClick(object? sender, RoutedEventArgs e)
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        _ = RunGit(repoRoot, "add -A", timeoutMs: 5000);
        InvalidateGitStatusCache();
        OnGitRefreshClick(sender, e);
    }

    private void OnGitUnstageAllClick(object? sender, RoutedEventArgs e)
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        _ = RunGit(repoRoot, "reset", timeoutMs: 5000);
        InvalidateGitStatusCache();
        OnGitRefreshClick(sender, e);
    }

    private void OnGitStageFileFromContextClick(object? sender, RoutedEventArgs e)
    {
        var item = GetGitTreeNodeFromContext(sender);
        if (item is null || !item.CanStage)
        {
            return;
        }

        ExecuteGitFileCommand(
            item,
            new[] { "add", "--", item.RelativePath },
            failurePrefix: "Stage failed");
    }

    private async void OnGitAcceptOursFromContextClick(object? sender, RoutedEventArgs e)
    {
        var item = GetGitTreeNodeFromContext(sender);
        await AcceptGitConflictSideAsync(item, useOurs: true);
    }

    private async void OnGitAcceptTheirsFromContextClick(object? sender, RoutedEventArgs e)
    {
        var item = GetGitTreeNodeFromContext(sender);
        await AcceptGitConflictSideAsync(item, useOurs: false);
    }

    private void OnGitUnstageFileFromContextClick(object? sender, RoutedEventArgs e)
    {
        var item = GetGitTreeNodeFromContext(sender);
        if (item is null || !item.CanUnstage)
        {
            return;
        }

        ExecuteGitFileCommand(
            item,
            new[] { "restore", "--staged", "--", item.RelativePath },
            failurePrefix: "Unstage failed");
    }

    private async void OnGitDiscardFileFromContextClick(object? sender, RoutedEventArgs e)
    {
        var item = GetGitTreeNodeFromContext(sender);
        await DiscardGitTreeItemAsync(item);
    }

    private async void OnGitCommitClick(object? sender, RoutedEventArgs e)
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        var message = await PromptTextAsync("Commit", "Commit message:");
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var safeMessage = message.Replace("\"", "'", StringComparison.Ordinal);
        var result = RunGit(repoRoot, $"commit -m \"{safeMessage}\"", timeoutMs: 10000);
        InvalidateGitStatusCache();
        if (GitSummaryTextBlock is not null)
        {
            GitSummaryTextBlock.Text = result.exitCode == 0
                ? "Committed successfully."
                : "Commit failed (check staged changes/message).";
        }

        OnGitRefreshClick(sender, e);
    }

    private async void OnGitSwitchBranchClick(object? sender, RoutedEventArgs e)
        => await ShowGitBranchSwitcherAsync();

    private async void OnGitCreateBranchClick(object? sender, RoutedEventArgs e)
        => await CreateGitBranchAsync();

    private async void OnGitRenameBranchClick(object? sender, RoutedEventArgs e)
        => await RenameGitBranchAsync();

    private async void OnGitDeleteBranchClick(object? sender, RoutedEventArgs e)
        => await DeleteGitBranchAsync();

    private async void OnGitStashPushClick(object? sender, RoutedEventArgs e)
        => await StashGitChangesAsync();

    private async void OnGitStashApplyClick(object? sender, RoutedEventArgs e)
        => await ApplyGitStashAsync();

    private async void OnGitStashPopClick(object? sender, RoutedEventArgs e)
        => await PopGitStashAsync();

    private async void OnGitStashDropClick(object? sender, RoutedEventArgs e)
        => await DropGitStashAsync();

    private async void OnGitAbortOperationClick(object? sender, RoutedEventArgs e)
        => await AbortGitRepositoryOperationAsync();

    private async void OnGitContinueOperationClick(object? sender, RoutedEventArgs e)
        => await ContinueGitRepositoryOperationAsync();

    private async void OnGitChangeDoubleTapped(object? sender, TappedEventArgs e)
    {
        var item = GetTreeNodeFromEventSource<GitChangeTreeNode>(e) ?? GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        await OpenGitTreeDiffAsync(item);
    }

    private async void OnGitChangesKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
        {
            return;
        }

        e.Handled = true;
        await OpenGitTreeDiffAsync(GitChangesTreeView?.SelectedItem as GitChangeTreeNode);
    }

    private async void OnGitChangesTapped(object? sender, TappedEventArgs e)
    {
        var item = GetTreeNodeFromEventSource<GitChangeTreeNode>(e);
        if (!CanOpenGitTreeItem(item))
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            item = GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        }

        await OpenGitTreeDiffAsync(item);
    }

    private void OnGitChangesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        ScheduleGitDiffPreview(item);
    }

    private async void OnGitOpenFileFromContextClick(object? sender, RoutedEventArgs e)
    {
        var item = (sender as MenuItem)?.DataContext as GitChangeTreeNode
                   ?? GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        await OpenGitTreeItemAsync(item);
    }

    private async void OnGitOpenDiffFromContextClick(object? sender, RoutedEventArgs e)
    {
        var item = (sender as MenuItem)?.DataContext as GitChangeTreeNode
                   ?? GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        await OpenGitTreeDiffAsync(item);
    }

    private async void OnGitOpenFileButtonClick(object? sender, RoutedEventArgs e)
        => await OpenGitTreeItemAsync(GitChangesTreeView?.SelectedItem as GitChangeTreeNode);

    private async void OnGitOpenDiffButtonClick(object? sender, RoutedEventArgs e)
        => await OpenGitTreeDiffAsync(GitChangesTreeView?.SelectedItem as GitChangeTreeNode);

    private void OnGitCloseDiffButtonClick(object? sender, RoutedEventArgs e)
        => DeactivateGitDiffCompareSession();

    private async Task OpenGitTreeItemAsync(GitChangeTreeNode? item)
    {
        if (!CanOpenGitTreeItem(item))
        {
            return;
        }

        if (_isGitDiffCompareActive)
        {
            DeactivateGitDiffCompareSession();
        }

        await OpenTreeFileAsync(item!.FullPath);
    }

    private async Task OpenGitTreeDiffAsync(GitChangeTreeNode? item)
    {
        if (!CanOpenGitTreeDiffItem(item))
        {
            return;
        }

        CancelGitDiffPreview();

        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitDiffPreviewState("Diff Viewer", "No git repository detected.");
            return;
        }

        var relativePath = NormalizeGitPreviewPath(item!.RelativePath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            SetGitDiffPreviewState("Diff Viewer", "This Git item does not point to a file diff.");
            return;
        }

        SetGitDiffPreviewState($"Diff Viewer: {relativePath}", "Loading Git compare...");

        if (File.Exists(item.FullPath))
        {
            await OpenTreeFileAsync(item.FullPath);
        }

        var plan = GitDiffCompareLogic.BuildPlan(item.Section, item.Status);
        if (!TryLoadGitCompareSource(plan.Primary, repoRoot, relativePath, item.FullPath, out var primaryText, out var primaryError))
        {
            SetGitDiffPreviewState("Diff Viewer", primaryError);
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = "Git diff viewer could not load the base side.";
            }

            return;
        }

        if (!TryLoadGitCompareSource(plan.Secondary, repoRoot, relativePath, item.FullPath, out var secondaryText, out var secondaryError))
        {
            SetGitDiffPreviewState("Diff Viewer", secondaryError);
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = "Git diff viewer could not load the comparison side.";
            }

            return;
        }

        var compareLineMap = BuildGitCompareLineMap(item, repoRoot, relativePath, primaryText, secondaryText);
        ActivateGitDiffCompareSession(
            item.FullPath,
            $"{plan.Primary.Title}: {relativePath}",
            primaryText,
            $"{plan.Secondary.Title}: {relativePath}",
            secondaryText,
            compareLineMap);

        SetGitDiffPreviewState(
            $"Diff Viewer: {relativePath}",
            $"Read-only Git compare\n\nLeft: {plan.Primary.Title}\nRight: {plan.Secondary.Title}\n\nUse the split compare controls above the editor to navigate changes.");
        if (GitSummaryTextBlock is not null)
        {
            GitSummaryTextBlock.Text = $"Opened read-only git diff for '{item.Name}'.";
        }

        UpdateGitOpenButtonState(item);
    }

    private GitChangeTreeNode? GetGitTreeNodeFromContext(object? sender)
        => (sender as MenuItem)?.DataContext as GitChangeTreeNode
           ?? GitChangesTreeView?.SelectedItem as GitChangeTreeNode;

    private async Task PreviewGitTreeItemAsync(GitChangeTreeNode? item)
    {
        if (item is null)
        {
            SetGitDiffPreviewState("Diff Preview", GetGitDiffPreviewEmptyStateText());
            return;
        }

        if (item.IsPlaceholder)
        {
            SetGitDiffPreviewState(
                BuildGitDiffPreviewTitle(item),
                item.Section switch
                {
                    GitChangeSection.Staged => "No staged changes.",
                    GitChangeSection.Conflicts => "No merge conflicts.",
                    _ => "Working tree clean.",
                });
            return;
        }

        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitDiffPreviewState("Diff Preview", "No git repository detected.");
            return;
        }

        var title = BuildGitDiffPreviewTitle(item);
        SetGitDiffPreviewState(title, "Loading diff preview...");

        var cts = new CancellationTokenSource();
        _gitDiffPreviewCts = cts;

        try
        {
            var preview = await Task.Run(() => BuildGitDiffPreviewText(item, repoRoot, cts.Token), cts.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_gitDiffPreviewCts, cts))
                {
                    SetGitDiffPreviewState(title, preview);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore stale preview loads when selection changes quickly.
        }
        finally
        {
            if (ReferenceEquals(_gitDiffPreviewCts, cts))
            {
                _gitDiffPreviewCts = null;
            }

            cts.Dispose();
        }
    }

    private void ScheduleGitDiffPreview(GitChangeTreeNode? item)
    {
        CancelGitDiffPreview();
        UpdateGitOpenButtonState(item);

        if (item is null || item.IsPlaceholder)
        {
            _ = PreviewGitTreeItemAsync(item);
            return;
        }

        SetGitDiffPreviewState(BuildGitDiffPreviewTitle(item), "Loading diff preview...");

        var cts = new CancellationTokenSource();
        _gitDiffPreviewDebounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(90, cts.Token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    var selected = GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
                    if (!ReferenceEquals(selected, item))
                    {
                        return;
                    }

                    await PreviewGitTreeItemAsync(item);
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when selection changes quickly.
            }
            finally
            {
                if (ReferenceEquals(_gitDiffPreviewDebounceCts, cts))
                {
                    _gitDiffPreviewDebounceCts = null;
                }

                cts.Dispose();
            }
        });
    }

    private string BuildGitDiffPreviewText(GitChangeTreeNode item, string repoRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!item.IsDirectory
            && item.Section == GitChangeSection.Unstaged
            && item.Status?.Contains('?', StringComparison.Ordinal) == true)
        {
            return BuildUntrackedGitPreview(item);
        }

        var result = RunGit(
            repoRoot,
            GitDiffPreviewLogic.BuildDiffArguments(item.Section, item.RelativePath),
            timeoutMs: 5000,
            cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.exitCode != 0)
        {
            var detail = FirstLine(result.stderr);
            return string.IsNullOrWhiteSpace(detail)
                ? "Failed to load diff preview."
                : $"Failed to load diff preview.\n{detail}";
        }

        var preview = GitDiffPreviewLogic.TrimPreview(result.stdout);
        if (!string.IsNullOrWhiteSpace(preview))
        {
            return preview;
        }

        var untrackedSummary = BuildUntrackedSummaryPreview(item);
        return string.IsNullOrWhiteSpace(untrackedSummary)
            ? "No textual diff available for this selection."
            : untrackedSummary;
    }

    private string BuildUntrackedGitPreview(GitChangeTreeNode item)
    {
        if (!File.Exists(item.FullPath))
        {
            return $"Untracked file '{item.RelativePath}' no longer exists on disk.";
        }

        if (!IsSearchableFile(item.FullPath))
        {
            return $"Untracked file: {item.RelativePath}\nNo text preview is available for this file type.";
        }

        var text = File.ReadAllText(item.FullPath);
        return GitDiffPreviewLogic.BuildUntrackedPreview(item.RelativePath, text);
    }

    private string? BuildUntrackedSummaryPreview(GitChangeTreeNode item)
    {
        if (item.Section != GitChangeSection.Unstaged)
        {
            return null;
        }

        var normalizedPrefix = NormalizeGitPreviewPath(item.RelativePath);
        var paths = _gitChanges
            .Where(change => change.Status.Contains('?', StringComparison.Ordinal))
            .Select(change => NormalizeGitPreviewPath(change.RelativePath))
            .Where(path => string.IsNullOrWhiteSpace(normalizedPrefix)
                || string.Equals(path, normalizedPrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();

        if (paths.Count == 0)
        {
            return null;
        }

        return GitDiffPreviewLogic.TrimPreview(
            "Untracked files in this scope:\n\n" + string.Join('\n', paths));
    }

    private void CancelGitDiffPreview()
    {
        _gitDiffPreviewDebounceCts?.Cancel();
        _gitDiffPreviewDebounceCts?.Dispose();
        _gitDiffPreviewDebounceCts = null;
        _gitDiffPreviewCts?.Cancel();
        _gitDiffPreviewCts?.Dispose();
        _gitDiffPreviewCts = null;
    }

    private void SetGitDiffPreviewState(string title, string text)
    {
        if (GitDiffPreviewTitleTextBlock is not null)
        {
            GitDiffPreviewTitleTextBlock.Text = title;
        }

        if (GitDiffPreviewTextBox is not null)
        {
            GitDiffPreviewTextBox.Text = text;
            GitDiffPreviewTextBox.CaretIndex = 0;
        }
    }

    private void UpdateGitOpenButtonState(GitChangeTreeNode? item)
    {
        if (GitOpenFileButton is not null)
        {
            GitOpenFileButton.IsEnabled = CanOpenGitTreeItem(item);
        }

        if (GitOpenDiffButton is not null)
        {
            GitOpenDiffButton.IsEnabled = CanOpenGitTreeDiffItem(item);
        }

        if (GitCloseDiffButton is not null)
        {
            GitCloseDiffButton.IsVisible = _isGitDiffCompareActive;
        }
    }

    private static bool CanOpenGitTreeItem(GitChangeTreeNode? item)
        => item is { IsDirectory: false, IsPlaceholder: false }
            && !string.IsNullOrWhiteSpace(item.FullPath);

    private static bool CanOpenGitTreeDiffItem(GitChangeTreeNode? item)
        => CanOpenGitTreeItem(item)
            && !string.IsNullOrWhiteSpace(item?.RelativePath);

    private static string GetGitDiffPreviewEmptyStateText()
        => "Select a change to preview its diff. Click or press Enter to open the Git compare, or use Open File to open the working file.";

    private static string BuildGitDiffPreviewTitle(GitChangeTreeNode item)
    {
        var scope = string.IsNullOrWhiteSpace(item.RelativePath) ? item.Name : item.RelativePath;
        var section = item.Section switch
        {
            GitChangeSection.Conflicts => "Conflicts",
            GitChangeSection.Staged => "Staged",
            GitChangeSection.Unstaged => "Working Tree",
            _ => "Diff",
        };

        return $"{section}: {scope}";
    }

    private static string NormalizeGitPreviewPath(string? relativePath)
        => (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');

    private GitDiffCompareLineMap? BuildGitCompareLineMap(
        GitChangeTreeNode item,
        string repoRoot,
        string relativePath,
        string primaryText,
        string secondaryText)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var patchResult = RunGit(
            repoRoot,
            GitDiffCompareHighlightLogic.BuildPatchArguments(item.Section, relativePath),
            timeoutMs: 5000);
        if (patchResult.exitCode != 0)
        {
            return null;
        }

        var primaryLineCount = CountTextLines(primaryText);
        var secondaryLineCount = CountTextLines(secondaryText);
        return GitDiffCompareHighlightLogic.BuildLineMap(
            item.Section,
            item.Status,
            patchResult.stdout,
            primaryLineCount,
            secondaryLineCount);
    }

    private static int CountTextLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return Math.Max(1, normalized.Count(static ch => ch == '\n') + 1);
    }

    private bool TryLoadGitCompareSource(
        GitDiffCompareSource source,
        string repoRoot,
        string relativePath,
        string fullPath,
        out string text,
        out string error)
    {
        text = string.Empty;
        error = string.Empty;

        if (GitDiffCompareLogic.ShouldUseEmptySource(source))
        {
            return true;
        }

        if (source.Kind == GitDiffCompareSourceKind.WorkingTree)
        {
            if (!File.Exists(fullPath))
            {
                error = $"Failed to load {source.Title.ToLowerInvariant()}.\nThe file no longer exists on disk.";
                return false;
            }

            if (!IsSearchableFile(fullPath))
            {
                error = $"Failed to load {source.Title.ToLowerInvariant()}.\nBinary and non-text files are not supported in the split diff viewer yet.";
                return false;
            }

            text = File.ReadAllText(fullPath);
            return true;
        }

        if (source.Kind == GitDiffCompareSourceKind.GitObject && !string.IsNullOrWhiteSpace(source.RevisionPrefix))
        {
            var objectExpression = GitDiffCompareLogic.BuildObjectExpression(source.RevisionPrefix, relativePath);
            var result = RunGit(repoRoot, new[] { "show", objectExpression }, timeoutMs: 5000);
            if (result.exitCode == 0)
            {
                text = result.stdout;
                return true;
            }

            var detail = FirstLine(result.stderr);
            error = string.IsNullOrWhiteSpace(detail)
                ? $"Failed to load {source.Title.ToLowerInvariant()}."
                : $"Failed to load {source.Title.ToLowerInvariant()}.\n{detail}";
            return false;
        }

        error = $"Failed to load {source.Title.ToLowerInvariant()}.";
        return false;
    }

    private void SetGitBranchState(string text)
    {
        if (GitBranchTextBlock is not null)
        {
            GitBranchTextBlock.Text = text;
        }
    }

    private void UpdateGitOperationUi(GitRepositoryOperationState? operationState)
    {
        if (GitOperationPanel is null
            || GitOperationTextBlock is null
            || GitContinueOperationButton is null
            || GitAbortOperationButton is null)
        {
            return;
        }

        var hasOperation = operationState is not null;
        GitOperationPanel.IsVisible = hasOperation;
        GitContinueOperationButton.IsVisible = hasOperation && operationState?.ContinueArguments is not null;
        GitAbortOperationButton.IsVisible = hasOperation;
        if (!hasOperation)
        {
            GitOperationTextBlock.Text = string.Empty;
            GitContinueOperationButton.Content = "Continue";
            GitAbortOperationButton.Content = "Abort";
            return;
        }

        GitOperationTextBlock.Text = operationState!.StatusText;
        GitContinueOperationButton.Content = operationState.ContinueButtonLabel ?? "Continue";
        GitAbortOperationButton.Content = operationState.AbortButtonLabel;
    }

    private async Task ShowGitBranchSwitcherAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            return;
        }

        var currentBranch = GetGitCurrentBranchName(repoRoot);
        var branchChoices = GitBranchLogic.BuildSwitchChoices(
            GetLocalGitBranches(repoRoot),
            GetRemoteGitBranches(repoRoot),
            currentBranch);
        if (branchChoices.Count == 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = "No branches available.";
            }

            return;
        }

        var items = branchChoices
            .Select(choice => new PaletteItem(
                GitBranchLogic.BuildBranchSelectionId(choice),
                GitBranchLogic.BuildBranchOptionTitle(choice),
                GitBranchLogic.BuildBranchOptionDescription(choice)))
            .ToList();

        var dialog = new SelectionPaletteDialog("Switch Branch", "Search local or remote branches...", items);
        var selected = await dialog.ShowDialog<string?>(this);
        if (!GitBranchLogic.TryParseBranchSelection(selected, out var isRemote, out var branchName))
        {
            return;
        }

        if (!isRemote && string.Equals(branchName, currentBranch, StringComparison.OrdinalIgnoreCase))
        {
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = $"Already on '{branchName}'.";
            }

            return;
        }

        var result = isRemote
            ? RunGit(
                repoRoot,
                new[]
                {
                    "switch",
                    "--track",
                    "-c",
                    GitBranchLogic.GetRemoteTrackingBranchName(branchName) ?? branchName,
                    branchName,
                },
                timeoutMs: 10000)
            : RunGit(repoRoot, new[] { "switch", branchName }, timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Branch switch failed."
                    : $"Branch switch failed: {detail}";
            }

            return;
        }

        await RefreshAfterGitBranchChangeAsync(branchName, created: false);
    }

    private async Task CreateGitBranchAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            return;
        }

        var currentBranch = GetGitCurrentBranchName(repoRoot);
        var proposedName = await PromptTextAsync(
            "Create Branch",
            "Enter new branch name:",
            string.IsNullOrWhiteSpace(currentBranch) ? null : $"{currentBranch}/");
        var branchName = GitBranchLogic.NormalizeBranchName(proposedName);
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        var result = RunGit(repoRoot, new[] { "switch", "-c", branchName }, timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Create branch failed."
                    : $"Create branch failed: {detail}";
            }

            return;
        }

        await RefreshAfterGitBranchChangeAsync(branchName, created: true);
    }

    private async Task RenameGitBranchAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            return;
        }

        var currentBranch = GetGitCurrentBranchName(repoRoot);
        if (string.IsNullOrWhiteSpace(currentBranch))
        {
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = "Cannot rename a detached HEAD.";
            }

            return;
        }

        var proposedName = await PromptTextAsync(
            "Rename Branch",
            $"Rename '{currentBranch}' to:",
            currentBranch);
        var branchName = GitBranchLogic.NormalizeBranchName(proposedName);
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        if (string.Equals(branchName, currentBranch, StringComparison.OrdinalIgnoreCase))
        {
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = $"Branch already named '{currentBranch}'.";
            }

            return;
        }

        var result = RunGit(repoRoot, GitBranchLogic.BuildRenameArguments(branchName), timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Rename branch failed."
                    : $"Rename branch failed: {detail}";
            }

            return;
        }

        RefreshAfterGitMetadataChange($"Renamed branch '{currentBranch}' to '{branchName}'.");
    }

    private async Task DeleteGitBranchAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            return;
        }

        var currentBranch = GetGitCurrentBranchName(repoRoot);
        var deleteChoices = GitBranchLogic.BuildDeleteChoices(GetLocalGitBranches(repoRoot), currentBranch);
        if (deleteChoices.Count == 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = "No local branches available to delete.";
            }

            return;
        }

        var items = deleteChoices
            .Select(choice => new PaletteItem(
                GitBranchLogic.BuildBranchSelectionId(choice),
                choice.Name,
                "Delete local branch"))
            .ToList();

        var dialog = new SelectionPaletteDialog("Delete Branch", "Search local branches...", items);
        var selected = await dialog.ShowDialog<string?>(this);
        if (!GitBranchLogic.TryParseBranchSelection(selected, out var isRemote, out var branchName) || isRemote)
        {
            return;
        }

        var confirm = await ConfirmAsync("Delete Branch", $"Delete local branch '{branchName}'?");
        if (!confirm)
        {
            return;
        }

        var result = RunGit(repoRoot, GitBranchLogic.BuildDeleteArguments(branchName, force: false), timeoutMs: 10000);
        if (result.exitCode != 0 && GitBranchLogic.ShouldOfferForceDelete(result.stderr))
        {
            var forceDelete = await ConfirmAsync(
                "Force Delete Branch",
                $"'{branchName}' is not fully merged. Force delete it?");
            if (!forceDelete)
            {
                return;
            }

            result = RunGit(repoRoot, GitBranchLogic.BuildDeleteArguments(branchName, force: true), timeoutMs: 10000);
        }

        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Delete branch failed."
                    : $"Delete branch failed: {detail}";
            }

            return;
        }

        RefreshAfterGitMetadataChange($"Deleted branch '{branchName}'.");
    }

    private async Task RefreshAfterGitBranchChangeAsync(string branchName, bool created)
    {
        var message = created
            ? $"Created and switched to '{branchName}'."
            : $"Switched to '{branchName}'.";
        await RefreshAfterGitWorktreeChangeAsync(message);
    }

    private string? GetGitCurrentBranchName(string repoRoot)
    {
        var branch = RunGit(repoRoot, new[] { "branch", "--show-current" }, timeoutMs: 1500);
        if (branch.exitCode != 0)
        {
            return null;
        }

        return GitBranchLogic.NormalizeBranchName(branch.stdout);
    }

    private string GetGitBranchDisplay(string repoRoot)
    {
        var currentBranch = GetGitCurrentBranchName(repoRoot);
        if (!string.IsNullOrWhiteSpace(currentBranch))
        {
            var tracking = RunGit(repoRoot, new[] { "rev-list", "--left-right", "--count", "@{upstream}...HEAD" }, timeoutMs: 1500);
            return tracking.exitCode == 0
                   && GitBranchLogic.TryParseAheadBehindCounts(tracking.stdout, out var ahead, out var behind)
                ? GitBranchLogic.FormatBranchLabel(currentBranch, ahead: ahead, behind: behind)
                : GitBranchLogic.FormatBranchLabel(currentBranch);
        }

        var detached = RunGit(repoRoot, new[] { "rev-parse", "--short", "HEAD" }, timeoutMs: 1500);
        var detachedCommit = detached.exitCode == 0 ? detached.stdout : null;
        return GitBranchLogic.FormatBranchLabel(null, detachedCommit);
    }

    private GitRepositoryOperationState? GetGitOperationState(string repoRoot)
    {
        var gitDirectory = GetGitDirectoryPath(repoRoot);
        return string.IsNullOrWhiteSpace(gitDirectory)
            ? null
            : GitRepositoryOperationLogic.DetectState(gitDirectory);
    }

    private string? GetGitDirectoryPath(string repoRoot)
    {
        var gitDirectory = RunGit(repoRoot, new[] { "rev-parse", "--git-dir" }, timeoutMs: 1500);
        if (gitDirectory.exitCode != 0)
        {
            return null;
        }

        var gitDirectoryPath = GitBranchLogic.NormalizeBranchName(gitDirectory.stdout);
        if (string.IsNullOrWhiteSpace(gitDirectoryPath))
        {
            return null;
        }

        return Path.IsPathRooted(gitDirectoryPath)
            ? gitDirectoryPath
            : Path.GetFullPath(Path.Combine(repoRoot, gitDirectoryPath));
    }

    private IReadOnlyList<string> GetLocalGitBranches(string repoRoot)
    {
        var branches = RunGit(
            repoRoot,
            new[] { "for-each-ref", "--format=%(refname:short)", "refs/heads" },
            timeoutMs: 2500);

        return branches.exitCode == 0
            ? GitBranchLogic.ParseBranchNames(branches.stdout)
            : Array.Empty<string>();
    }

    private IReadOnlyList<string> GetRemoteGitBranches(string repoRoot)
    {
        var branches = RunGit(
            repoRoot,
            new[] { "for-each-ref", "--format=%(refname:short)", "refs/remotes" },
            timeoutMs: 2500);

        return branches.exitCode == 0
            ? GitBranchLogic.ParseBranchNames(branches.stdout)
            : Array.Empty<string>();
    }

    private async Task StashGitChangesAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            return;
        }

        var message = await PromptTextAsync("Stash Changes", "Optional stash message:");
        var result = RunGit(repoRoot, GitStashLogic.BuildPushArguments(message), timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Stash failed."
                    : $"Stash failed: {detail}";
            }

            return;
        }

        await RefreshAfterGitWorktreeChangeAsync("Changes stashed.");
    }

    private async Task ApplyGitStashAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            return;
        }

        var stashReference = await SelectGitStashReferenceAsync(
            repoRoot,
            "Apply Stash",
            "Search saved stashes...");
        if (string.IsNullOrWhiteSpace(stashReference))
        {
            return;
        }

        var result = RunGit(repoRoot, GitStashLogic.BuildApplyArguments(stashReference), timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            if (await TryHandleGitStashConflictAsync(
                    result,
                    $"Applied {stashReference} with conflicts. Resolve conflicts manually."))
            {
                return;
            }

            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Apply stash failed."
                    : $"Apply stash failed: {detail}";
            }

            return;
        }

        await RefreshAfterGitWorktreeChangeAsync($"Applied {stashReference}.");
    }

    private async Task PopGitStashAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            return;
        }

        var stashReference = await SelectGitStashReferenceAsync(
            repoRoot,
            "Pop Stash",
            "Search saved stashes to restore and drop...");
        if (string.IsNullOrWhiteSpace(stashReference))
        {
            return;
        }

        var result = RunGit(repoRoot, GitStashLogic.BuildPopArguments(stashReference), timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            if (await TryHandleGitStashConflictAsync(
                    result,
                    $"Popped {stashReference} with conflicts. Resolve conflicts manually."))
            {
                return;
            }

            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Pop stash failed."
                    : $"Pop stash failed: {detail}";
            }

            return;
        }

        await RefreshAfterGitWorktreeChangeAsync($"Popped {stashReference}.");
    }

    private async Task DropGitStashAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            return;
        }

        var stashReference = await SelectGitStashReferenceAsync(
            repoRoot,
            "Drop Stash",
            "Search saved stashes to delete...");
        if (string.IsNullOrWhiteSpace(stashReference))
        {
            return;
        }

        var confirm = await ConfirmAsync("Drop Stash", $"Delete saved stash '{stashReference}'?");
        if (!confirm)
        {
            return;
        }

        var result = RunGit(repoRoot, GitStashLogic.BuildDropArguments(stashReference), timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Drop stash failed."
                    : $"Drop stash failed: {detail}";
            }

            return;
        }

        RefreshAfterGitMetadataChange($"Dropped {stashReference}.");
    }

    private async Task AbortGitRepositoryOperationAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            UpdateGitOperationUi(null);
            return;
        }

        var operationState = GetGitOperationState(repoRoot);
        if (operationState is null)
        {
            UpdateGitOperationUi(null);
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = "No repository operation is in progress.";
            }

            return;
        }

        var confirm = await ConfirmAsync(
            operationState.AbortButtonLabel,
            $"{operationState.StatusText}\n\nProceed with {operationState.AbortButtonLabel.ToLowerInvariant()}?");
        if (!confirm)
        {
            return;
        }

        var result = RunGit(repoRoot, operationState.AbortArguments, timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? $"{operationState.AbortButtonLabel} failed."
                    : $"{operationState.AbortButtonLabel} failed: {detail}";
            }

            return;
        }

        await RefreshAfterGitWorktreeChangeAsync($"{operationState.DisplayName} aborted.");
    }

    private async Task ContinueGitRepositoryOperationAsync()
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            SetGitBranchState("Branch: unavailable");
            UpdateGitOperationUi(null);
            return;
        }

        var operationState = GetGitOperationState(repoRoot);
        if (operationState is null)
        {
            UpdateGitOperationUi(null);
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = "No repository operation is in progress.";
            }

            return;
        }

        if (operationState.ContinueArguments is null)
        {
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = $"{operationState.DisplayName} must be completed from Git directly.";
            }

            return;
        }

        var result = RunGit(repoRoot, operationState.ContinueArguments, timeoutMs: 10000);
        if (result.exitCode != 0)
        {
            UpdateGitPanel(forceRefresh: true);
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? $"{operationState.ContinueButtonLabel} failed."
                    : $"{operationState.ContinueButtonLabel} failed: {detail}";
            }

            return;
        }

        await RefreshAfterGitWorktreeChangeAsync($"{operationState.DisplayName} continued.");
    }

    private async Task AcceptGitConflictSideAsync(GitChangeTreeNode? item, bool useOurs)
    {
        if (item is null || !item.CanAcceptConflictSide)
        {
            return;
        }

        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(item.RelativePath))
        {
            return;
        }

        var sideLabel = useOurs ? "ours" : "theirs";
        var confirm = await ConfirmAsync(
            useOurs ? "Accept Ours" : "Accept Theirs",
            $"Replace conflict markers in '{item.Name}' with the {sideLabel} version?");
        if (!confirm)
        {
            return;
        }

        var result = RunGit(
            repoRoot,
            new[] { "checkout", useOurs ? "--ours" : "--theirs", "--", item.RelativePath },
            timeoutMs: 8000);
        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? "Conflict resolution command failed."
                    : $"Conflict resolution failed: {detail}";
            }

            return;
        }

        await RefreshAfterGitWorktreeChangeAsync(
            $"Accepted {sideLabel} for '{item.Name}'. Stage the file when the resolution is ready.");
    }

    private IReadOnlyList<GitStashEntry> GetGitStashEntries(string repoRoot)
    {
        var stashes = RunGit(repoRoot, new[] { "stash", "list" }, timeoutMs: 2500);
        return stashes.exitCode == 0
            ? GitStashLogic.ParseEntries(stashes.stdout)
            : Array.Empty<GitStashEntry>();
    }

    private async Task<string?> SelectGitStashReferenceAsync(string repoRoot, string title, string placeholder)
    {
        var stashes = GetGitStashEntries(repoRoot);
        if (stashes.Count == 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                GitSummaryTextBlock.Text = "No stashes available.";
            }

            return null;
        }

        var items = stashes
            .Select(stash => new PaletteItem(
                GitStashLogic.BuildSelectionId(stash.Reference),
                stash.Reference,
                stash.Summary))
            .ToList();

        var dialog = new SelectionPaletteDialog(title, placeholder, items);
        var selected = await dialog.ShowDialog<string?>(this);
        return GitStashLogic.TryParseSelection(selected, out var stashReference)
            ? stashReference
            : null;
    }

    private async Task<bool> TryHandleGitStashConflictAsync(
        (int exitCode, string stdout, string stderr) result,
        string summaryMessage)
    {
        if (!GitStashLogic.IndicatesConflictOrPartialApply(result.stdout, result.stderr))
        {
            return false;
        }

        await RefreshAfterGitWorktreeChangeAsync(summaryMessage);
        return true;
    }

    private async Task RefreshAfterGitWorktreeChangeAsync(string summaryMessage)
    {
        InvalidateGitStatusCache();
        RefreshExplorer();
        await RefreshOpenDocumentsAfterGitWorktreeChangeAsync();
        UpdateGitPanel(forceRefresh: true);
        UpdateGitDiffGutter();
        UpdateTerminalCwd();

        if (GitSummaryTextBlock is not null)
        {
            GitSummaryTextBlock.Text = summaryMessage;
        }
    }

    private void RefreshAfterGitMetadataChange(string summaryMessage)
    {
        InvalidateGitStatusCache();
        UpdateGitPanel(forceRefresh: true);
        UpdateGitDiffGutter();
        UpdateTerminalCwd();

        if (GitSummaryTextBlock is not null)
        {
            GitSummaryTextBlock.Text = summaryMessage;
        }
    }

    private bool ExecuteGitFileCommand(GitChangeTreeNode item, IReadOnlyList<string> arguments, string failurePrefix)
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(item.RelativePath))
        {
            return false;
        }

        var result = RunGit(repoRoot, arguments, timeoutMs: 5000);
        if (result.exitCode != 0)
        {
            if (GitSummaryTextBlock is not null)
            {
                var detail = FirstLine(result.stderr);
                GitSummaryTextBlock.Text = string.IsNullOrWhiteSpace(detail)
                    ? failurePrefix
                    : $"{failurePrefix}: {detail}";
            }

            return false;
        }

        InvalidateGitStatusCache();
        OnGitRefreshClick(this, new RoutedEventArgs());
        return true;
    }

    private async Task DiscardGitTreeItemAsync(GitChangeTreeNode? item)
    {
        if (item is null || !item.CanDiscard)
        {
            return;
        }

        if (item.DiscardAction == GitDiscardActionKind.DeleteUntracked)
        {
            var confirmDelete = await ConfirmAsync("Delete Untracked File", $"Delete untracked file '{item.Name}'?");
            if (!confirmDelete)
            {
                return;
            }

            try
            {
                if (File.Exists(item.FullPath))
                {
                    File.Delete(item.FullPath);
                }

                RemoveOpenDocumentForDeletedPath(item.FullPath);
                InvalidateGitStatusCache();
                OnGitRefreshClick(this, new RoutedEventArgs());
            }
            catch
            {
                if (GitSummaryTextBlock is not null)
                {
                    GitSummaryTextBlock.Text = "Delete untracked file failed.";
                }
            }

            return;
        }

        var confirmDiscard = await ConfirmAsync("Discard Changes", $"Discard unstaged changes in '{item.Name}'?");
        if (!confirmDiscard)
        {
            return;
        }

        var success = ExecuteGitFileCommand(
            item,
            new[] { "restore", "--", item.RelativePath },
            failurePrefix: "Discard failed");

        if (!success)
        {
            return;
        }

        await ReloadOpenDocumentForGitPathAsync(item.FullPath);
    }

    private async Task ReloadOpenDocumentForGitPathAsync(string fullPath)
    {
        var doc = FindOpenDocumentByPath(fullPath);
        if (doc is null || string.IsNullOrWhiteSpace(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            return;
        }

        await ReloadFromDiskAsync(doc);
    }

    private TextDocument? FindOpenDocumentByPath(string fullPath)
    {
        foreach (var doc in _viewModel.Documents)
        {
            if (!string.IsNullOrWhiteSpace(doc.FilePath) && PathsEqual(doc.FilePath!, fullPath))
            {
                return doc;
            }
        }

        return null;
    }

    private void RemoveOpenDocumentForDeletedPath(string fullPath)
    {
        var removedSelected = false;
        var removedSplit = false;

        foreach (var doc in _viewModel.Documents.ToList())
        {
            if (string.IsNullOrWhiteSpace(doc.FilePath) || !PathsEqual(doc.FilePath!, fullPath))
            {
                continue;
            }

            removedSelected |= ReferenceEquals(doc, _viewModel.SelectedDocument);
            removedSplit |= ReferenceEquals(doc, _splitDocument);
            _viewModel.Documents.Remove(doc);
        }

        if (_viewModel.Documents.Count == 0)
        {
            _viewModel.NewDocument();
            return;
        }

        if (removedSelected || _viewModel.SelectedDocument is null || !_viewModel.Documents.Contains(_viewModel.SelectedDocument))
        {
            _viewModel.SelectedDocument = _viewModel.Documents[0];
        }

        if (removedSplit)
        {
            _splitDocument = _viewModel.Documents.FirstOrDefault();
            SyncSplitEditorFromDocument();
            RefreshSplitEditorTitle();
        }
    }

    private static Dictionary<string, bool> CaptureGitTreeExpansionState(IEnumerable<GitChangeTreeNode> nodes)
    {
        var state = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            CaptureGitTreeExpansionState(node, state);
        }

        return state;
    }

    private static void CaptureGitTreeExpansionState(GitChangeTreeNode node, IDictionary<string, bool> state)
    {
        if (node.IsDirectory)
        {
            state[GetGitTreeExpansionKey(node)] = node.IsExpanded;
        }

        foreach (var child in node.Children)
        {
            CaptureGitTreeExpansionState(child, state);
        }
    }

    private static void RestoreGitTreeExpansionState(IEnumerable<GitChangeTreeNode> nodes, IReadOnlyDictionary<string, bool> state)
    {
        foreach (var node in nodes)
        {
            RestoreGitTreeExpansionState(node, state);
        }
    }

    private static void RestoreGitTreeExpansionState(GitChangeTreeNode node, IReadOnlyDictionary<string, bool> state)
    {
        if (node.IsDirectory && state.TryGetValue(GetGitTreeExpansionKey(node), out var isExpanded))
        {
            node.IsExpanded = isExpanded;
        }

        foreach (var child in node.Children)
        {
            RestoreGitTreeExpansionState(child, state);
        }
    }

    private static void SetGitTreeExpansion(IEnumerable<GitChangeTreeNode> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            if (!node.IsDirectory)
            {
                continue;
            }

            node.IsExpanded = expanded;
            SetGitTreeExpansion(node.Children, expanded);
        }
    }

    private static string GetGitTreeExpansionKey(GitChangeTreeNode node)
        => $"{node.Name}|{node.FullPath}";
}

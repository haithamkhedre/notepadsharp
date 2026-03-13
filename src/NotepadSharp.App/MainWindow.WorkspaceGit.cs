using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Material.Icons;
using NotepadSharp.App.Services;

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
            return;
        }

        var inferredRoot = NormalizeWorkspaceRoot(InferWorkspaceRootFromContext());
        if (!string.Equals(_workspaceRoot, inferredRoot, StringComparison.OrdinalIgnoreCase))
        {
            _workspaceRoot = inferredRoot;
            InvalidateGitRepositoryRootCache();
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

        if (!string.Equals(_workspaceRoot, normalized, StringComparison.OrdinalIgnoreCase))
        {
            InvalidateGitRepositoryRootCache();
            InvalidateGitStatusCache();
        }

        _workspaceRoot = normalized;
        UpdateWorkspaceRootLabel();
        RefreshExplorer();
        UpdateTerminalCwd();

        if (persist)
        {
            PersistState();
        }
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
                var openDoc = _viewModel.Documents.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(d.FilePath)
                    && string.Equals(Path.GetFullPath(d.FilePath!), Path.GetFullPath(selected.FullPath), StringComparison.OrdinalIgnoreCase));
                if (openDoc is not null)
                {
                    openDoc.FilePath = nextPath;
                    _viewModel.AddRecentFile(nextPath);
                }
            }

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
        var expansionState = CaptureGitTreeExpansionState(_gitChangeTreeRootNodes);

        _gitChanges.Clear();
        _gitChangeTreeRootNodes.Clear();
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            GitChangesTreeView.ItemsSource = Array.Empty<GitChangeTreeNode>();
            GitSummaryTextBlock.Text = "No git repository detected.";
            return;
        }

        var statusText = GetGitStatusPorcelain(repoRoot, forceRefresh: forceRefresh);
        if (statusText is null)
        {
            GitChangesTreeView.ItemsSource = Array.Empty<GitChangeTreeNode>();
            GitSummaryTextBlock.Text = "Failed to read git status.";
            return;
        }

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

        _gitChangeTreeRootNodes.AddRange(GitStatusLogic.BuildGitChangeTree(repoRoot, stagedEntries, unstagedEntries));
        RestoreGitTreeExpansionState(_gitChangeTreeRootNodes, expansionState);
        GitChangesTreeView.ItemsSource = _gitChangeTreeRootNodes.ToList();
        GitChangesTreeView.SelectedItem = null;
        GitSummaryTextBlock.Text = _gitChanges.Count == 0
            ? "Working tree clean."
            : $"{_gitChanges.Count} files changed | {stagedEntries.Count} staged | {unstagedEntries.Count} unstaged";
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

    private async void OnGitChangeDoubleTapped(object? sender, TappedEventArgs e)
    {
        var item = GetTreeNodeFromEventSource<GitChangeTreeNode>(e) ?? GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        await OpenGitTreeItemAsync(item);
    }

    private async void OnGitChangesTapped(object? sender, TappedEventArgs e)
    {
        var item = GetTreeNodeFromEventSource<GitChangeTreeNode>(e) ?? GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        await OpenGitTreeItemAsync(item);
    }

    private async void OnGitChangesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        await OpenGitTreeItemAsync(item);
    }

    private async void OnGitOpenFileFromContextClick(object? sender, RoutedEventArgs e)
    {
        var item = (sender as MenuItem)?.DataContext as GitChangeTreeNode
                   ?? GitChangesTreeView?.SelectedItem as GitChangeTreeNode;
        await OpenGitTreeItemAsync(item);
    }

    private async Task OpenGitTreeItemAsync(GitChangeTreeNode? item)
    {
        if (item is null || item.IsDirectory)
        {
            return;
        }

        await OpenTreeFileAsync(item.FullPath);
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Material.Icons;

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

        _workspaceRoot = NormalizeWorkspaceRoot(InferWorkspaceRootFromContext());
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
        foreach (var kvp in GetGitStatusByPath(_workspaceRoot!))
        {
            _gitStatusByPath[kvp.Key] = kvp.Value;
        }

        _explorerRootNodes.Clear();
        var nodes = BuildExplorerNodes(_workspaceRoot!, ref nodeBudget, ref fileCount);
        _explorerRootNodes.AddRange(nodes);

        ExplorerTreeView.ItemsSource = _explorerRootNodes.ToList();
        if (WorkspaceRootTextBlock is not null)
        {
            WorkspaceRootTextBlock.Text = $"{_workspaceRoot}  ({fileCount} files)";
        }

        UpdateGitPanel();
    }

    private List<ExplorerTreeNode> BuildExplorerNodes(string directoryPath, ref int nodeBudget, ref int fileCount)
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
            };

            var children = BuildExplorerNodes(dir, ref nodeBudget, ref fileCount);
            node.Children.AddRange(children);
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
        if (item is null || item.IsDirectory)
        {
            return;
        }

        await OpenTreeFileAsync(item.FullPath);
    }

    private ExplorerTreeNode? GetSelectedExplorerNode()
        => ExplorerTreeView?.SelectedItem as ExplorerTreeNode;

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

        var result = RunGit(_workspaceRoot!, "rev-parse --show-toplevel", timeoutMs: 1500);
        if (result.exitCode != 0)
        {
            return null;
        }

        var root = result.stdout.Trim();
        return Directory.Exists(root) ? root : null;
    }

    private Dictionary<string, string> GetGitStatusByPath(string workspaceRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return result;
        }

        var status = RunGit(repoRoot, "status --porcelain=v1 --untracked-files=all", timeoutMs: 3000);
        if (status.exitCode != 0)
        {
            return result;
        }

        foreach (var line in status.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
            result[fullPath] = MapGitBadge(code);
        }

        return result;
    }

    private static string MapGitBadge(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        if (code.Contains('?', StringComparison.Ordinal))
        {
            return "?";
        }

        if (code.Contains('A', StringComparison.Ordinal))
        {
            return "A";
        }

        if (code.Contains('D', StringComparison.Ordinal))
        {
            return "D";
        }

        if (code.Contains('R', StringComparison.Ordinal))
        {
            return "R";
        }

        if (code.Contains('U', StringComparison.Ordinal))
        {
            return "U";
        }

        if (code.Contains('M', StringComparison.Ordinal))
        {
            return "M";
        }

        return code.Trim();
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

    private void UpdateGitPanel()
    {
        if (GitChangesTreeView is null || GitSummaryTextBlock is null)
        {
            return;
        }

        _gitChanges.Clear();
        _gitChangeTreeRootNodes.Clear();
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            GitChangesTreeView.ItemsSource = Array.Empty<GitChangeTreeNode>();
            GitSummaryTextBlock.Text = "No git repository detected.";
            return;
        }

        var result = RunGit(repoRoot, "status --porcelain=v1 --untracked-files=all", timeoutMs: 3000);
        if (result.exitCode != 0)
        {
            GitChangesTreeView.ItemsSource = Array.Empty<GitChangeTreeNode>();
            GitSummaryTextBlock.Text = "Failed to read git status.";
            return;
        }

        var stagedEntries = new List<GitChangeEntryModel>();
        var unstagedEntries = new List<GitChangeEntryModel>();
        foreach (var line in result.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
            var stagedCode = GetStagedCode(xy);
            var unstagedCode = GetUnstagedCode(xy);
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

        _gitChangeTreeRootNodes.AddRange(BuildGitChangeTree(repoRoot, stagedEntries, unstagedEntries));
        GitChangesTreeView.ItemsSource = _gitChangeTreeRootNodes.ToList();
        GitChangesTreeView.SelectedItem = null;
        GitSummaryTextBlock.Text = _gitChanges.Count == 0
            ? "Working tree clean."
            : $"{_gitChanges.Count} files changed | {stagedEntries.Count} staged | {unstagedEntries.Count} unstaged";
    }

    private static List<GitChangeTreeNode> BuildGitChangeTree(
        string repoRoot,
        IReadOnlyList<GitChangeEntryModel> stagedEntries,
        IReadOnlyList<GitChangeEntryModel> unstagedEntries)
    {
        var stagedRoot = new GitChangeTreeNode
        {
            Name = $"Staged ({stagedEntries.Count})",
            FullPath = repoRoot,
            IsDirectory = true,
            IsExpanded = true,
            IconKind = MaterialIconKind.SourceBranch,
        };
        stagedRoot.Children.AddRange(BuildGitSectionNodes(repoRoot, stagedEntries));
        if (stagedRoot.Children.Count == 0)
        {
            stagedRoot.Children.Add(new GitChangeTreeNode
            {
                Name = "(no staged files)",
                FullPath = repoRoot,
                IsDirectory = false,
                IsExpanded = false,
                IconKind = MaterialIconKind.FileDocumentOutline,
            });
        }

        var unstagedRoot = new GitChangeTreeNode
        {
            Name = $"Changes ({unstagedEntries.Count})",
            FullPath = repoRoot,
            IsDirectory = true,
            IsExpanded = true,
            IconKind = MaterialIconKind.SourceBranch,
        };
        unstagedRoot.Children.AddRange(BuildGitSectionNodes(repoRoot, unstagedEntries));
        if (unstagedRoot.Children.Count == 0)
        {
            unstagedRoot.Children.Add(new GitChangeTreeNode
            {
                Name = "(working tree clean)",
                FullPath = repoRoot,
                IsDirectory = false,
                IsExpanded = false,
                IconKind = MaterialIconKind.FileDocumentOutline,
            });
        }

        return new List<GitChangeTreeNode> { stagedRoot, unstagedRoot };
    }

    private static List<GitChangeTreeNode> BuildGitSectionNodes(string repoRoot, IReadOnlyList<GitChangeEntryModel> changes)
    {
        var roots = new List<GitChangeTreeNode>();
        var directoryLookup = new Dictionary<string, GitChangeTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes.OrderBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedPath = (change.RelativePath ?? string.Empty).Replace("\\", "/", StringComparison.Ordinal);
            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var currentPath = string.Empty;
            var currentNodes = roots;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                currentPath = string.IsNullOrEmpty(currentPath)
                    ? segments[i]
                    : $"{currentPath}/{segments[i]}";

                if (!directoryLookup.TryGetValue(currentPath, out var dirNode))
                {
                    var fullPath = Path.GetFullPath(Path.Combine(repoRoot, currentPath.Replace('/', Path.DirectorySeparatorChar)));
                    dirNode = new GitChangeTreeNode
                    {
                        Name = segments[i],
                        FullPath = fullPath,
                        IsDirectory = true,
                        IsExpanded = false,
                        IconKind = MaterialIconKind.FolderOutline,
                    };
                    directoryLookup[currentPath] = dirNode;
                    currentNodes.Add(dirNode);
                }

                currentNodes = dirNode.Children;
            }

            var fileName = segments[^1];
            currentNodes.Add(new GitChangeTreeNode
            {
                Name = fileName,
                FullPath = change.FullPath,
                IsDirectory = false,
                IsExpanded = false,
                IconKind = GetFileIconKind(change.FullPath),
                Status = change.Status,
            });
        }

        SortGitTreeNodes(roots);
        return roots;
    }

    private static void SortGitTreeNodes(List<GitChangeTreeNode> nodes)
    {
        nodes.Sort((left, right) =>
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });

        foreach (var node in nodes.Where(n => n.IsDirectory))
        {
            SortGitTreeNodes(node.Children);
        }
    }

    private void OnGitRefreshClick(object? sender, RoutedEventArgs e)
    {
        RefreshExplorer();
        UpdateGitPanel();
        UpdateGitDiffGutter();
    }

    private static string? GetStagedCode(string xy)
    {
        if (string.IsNullOrEmpty(xy) || xy.Length < 2)
        {
            return null;
        }

        var x = xy[0];
        if (x is ' ' or '?' or '!')
        {
            return null;
        }

        return x.ToString();
    }

    private static string? GetUnstagedCode(string xy)
    {
        if (string.IsNullOrEmpty(xy) || xy.Length < 2)
        {
            return null;
        }

        if (xy[0] == '?' && xy[1] == '?')
        {
            return "?";
        }

        var y = xy[1];
        if (y is ' ' or '!')
        {
            return null;
        }

        return y == '?' ? "?" : y.ToString();
    }

    private void OnGitExpandAllClick(object? sender, RoutedEventArgs e)
    {
        if (GitChangesTreeView is null)
        {
            return;
        }

        // Expand in multiple passes so newly materialized nested items also expand.
        for (var pass = 0; pass < 8; pass++)
        {
            var changed = false;
            foreach (var item in GitChangesTreeView.GetVisualDescendants().OfType<TreeViewItem>())
            {
                if (!item.IsExpanded)
                {
                    item.IsExpanded = true;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private void OnGitCollapseAllClick(object? sender, RoutedEventArgs e)
    {
        if (GitChangesTreeView is null)
        {
            return;
        }

        foreach (var item in GitChangesTreeView.GetVisualDescendants().OfType<TreeViewItem>())
        {
            item.IsExpanded = false;
        }
    }

    private void OnGitStageAllClick(object? sender, RoutedEventArgs e)
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        _ = RunGit(repoRoot, "add -A", timeoutMs: 5000);
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

    private async Task OpenGitTreeItemAsync(GitChangeTreeNode? item)
    {
        if (item is null || item.IsDirectory)
        {
            return;
        }

        await OpenTreeFileAsync(item.FullPath);
    }
}

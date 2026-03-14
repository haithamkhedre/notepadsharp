using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Material.Icons;

namespace NotepadSharp.App.Services;

public enum GitChangeSection
{
    None,
    Conflicts,
    Staged,
    Unstaged,
}

public enum GitDiscardActionKind
{
    None,
    RestoreWorkingTree,
    DeleteUntracked,
}

public static class GitStatusLogic
{
    public static string MapGitBadge(string code)
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

    public static string? GetStagedCode(string xy)
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

    public static string? GetUnstagedCode(string xy)
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

    public static bool IsConflictXy(string xy)
    {
        if (string.IsNullOrWhiteSpace(xy) || xy.Length < 2)
        {
            return false;
        }

        return xy is "DD" or "AU" or "UD" or "UA" or "DU" or "AA" or "UU";
    }

    public static List<GitChangeTreeNode> BuildGitChangeTree(
        string repoRoot,
        IReadOnlyList<GitChangeEntryModel> conflictEntries,
        IReadOnlyList<GitChangeEntryModel> stagedEntries,
        IReadOnlyList<GitChangeEntryModel> unstagedEntries)
    {
        var roots = new List<GitChangeTreeNode>();
        if (conflictEntries.Count > 0)
        {
            var conflictRoot = new GitChangeTreeNode
            {
                Name = $"Conflicts ({conflictEntries.Count})",
                FullPath = repoRoot,
                RelativePath = string.Empty,
                IsDirectory = true,
                IsExpanded = true,
                Section = GitChangeSection.Conflicts,
                IconKind = Material.Icons.MaterialIconKind.AlertCircleOutline,
            };
            conflictRoot.Children.AddRange(BuildGitSectionNodes(repoRoot, conflictEntries, GitChangeSection.Conflicts));
            roots.Add(conflictRoot);
        }

        var stagedRoot = new GitChangeTreeNode
        {
            Name = $"Staged ({stagedEntries.Count})",
            FullPath = repoRoot,
            RelativePath = string.Empty,
            IsDirectory = true,
            IsExpanded = true,
            Section = GitChangeSection.Staged,
            IconKind = MaterialIconKind.SourceBranch,
        };
        stagedRoot.Children.AddRange(BuildGitSectionNodes(repoRoot, stagedEntries, GitChangeSection.Staged));
        if (stagedRoot.Children.Count == 0)
        {
            stagedRoot.Children.Add(CreatePlaceholderNode(repoRoot, GitChangeSection.Staged, "(no staged files)"));
        }

        var unstagedRoot = new GitChangeTreeNode
        {
            Name = $"Changes ({unstagedEntries.Count})",
            FullPath = repoRoot,
            RelativePath = string.Empty,
            IsDirectory = true,
            IsExpanded = true,
            Section = GitChangeSection.Unstaged,
            IconKind = MaterialIconKind.SourceBranch,
        };
        unstagedRoot.Children.AddRange(BuildGitSectionNodes(repoRoot, unstagedEntries, GitChangeSection.Unstaged));
        if (unstagedRoot.Children.Count == 0)
        {
            unstagedRoot.Children.Add(CreatePlaceholderNode(repoRoot, GitChangeSection.Unstaged, "(working tree clean)"));
        }

        roots.Add(stagedRoot);
        roots.Add(unstagedRoot);
        return roots;
    }

    public static GitDiscardActionKind GetDiscardAction(
        string? status,
        GitChangeSection section,
        bool isDirectory = false,
        bool isPlaceholder = false)
    {
        if (section != GitChangeSection.Unstaged || isDirectory || isPlaceholder || string.IsNullOrWhiteSpace(status))
        {
            return GitDiscardActionKind.None;
        }

        return status.Contains('?', StringComparison.Ordinal)
            ? GitDiscardActionKind.DeleteUntracked
            : GitDiscardActionKind.RestoreWorkingTree;
    }

    private static List<GitChangeTreeNode> BuildGitSectionNodes(
        string repoRoot,
        IReadOnlyList<GitChangeEntryModel> changes,
        GitChangeSection section)
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
                        RelativePath = currentPath,
                        IsDirectory = true,
                        IsExpanded = true,
                        Section = section,
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
                RelativePath = change.RelativePath ?? string.Empty,
                IsDirectory = false,
                IsExpanded = false,
                Section = section,
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

    private static GitChangeTreeNode CreatePlaceholderNode(string repoRoot, GitChangeSection section, string name)
    {
        return new GitChangeTreeNode
        {
            Name = name,
            FullPath = repoRoot,
            RelativePath = string.Empty,
            IsDirectory = false,
            IsExpanded = false,
            Section = section,
            IsPlaceholder = true,
            IconKind = MaterialIconKind.FileDocumentOutline,
        };
    }
}

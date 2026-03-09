using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Material.Icons;

namespace NotepadSharp.App.Services;

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

    public static List<GitChangeTreeNode> BuildGitChangeTree(
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
                        IsExpanded = true,
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
}

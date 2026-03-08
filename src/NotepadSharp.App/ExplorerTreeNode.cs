using System.Collections.Generic;
using Material.Icons;

namespace NotepadSharp.App;

public sealed class ExplorerTreeNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public MaterialIconKind IconKind { get; init; } = MaterialIconKind.FileDocumentOutline;
    public string? GitBadge { get; init; }
    public string DisplayName => Name;
    public bool HasGitBadge => !string.IsNullOrWhiteSpace(GitBadge);
    public List<ExplorerTreeNode> Children { get; } = new();
}

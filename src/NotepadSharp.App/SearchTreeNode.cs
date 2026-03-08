using System.Collections.Generic;

namespace NotepadSharp.App;

public sealed class SearchTreeNode
{
    public required string DisplayText { get; init; }
    public SearchResultLocation? Location { get; init; }
    public List<SearchTreeNode> Children { get; } = new();
}

public sealed class SearchResultLocation
{
    public required string FilePath { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required int Length { get; init; }
}


using System;
using Avalonia.Media;

namespace NotepadSharp.App;

public sealed class GitChangeEntryModel
{
    public required string Code { get; init; }
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }

    public string Status => string.IsNullOrWhiteSpace(Code) ? "--" : Code;

    public IBrush StatusBrush => Status switch
    {
        var s when s.Contains('U', StringComparison.Ordinal) => new SolidColorBrush(Color.Parse("#E06C75")),
        var s when s.Contains('D', StringComparison.Ordinal) => new SolidColorBrush(Color.Parse("#E06C75")),
        var s when s.Contains('A', StringComparison.Ordinal) || s.Contains('?', StringComparison.Ordinal) => new SolidColorBrush(Color.Parse("#98C379")),
        var s when s.Contains('M', StringComparison.Ordinal) || s.Contains('R', StringComparison.Ordinal)
            => new SolidColorBrush(Color.Parse("#E5C07B")),
        _ => new SolidColorBrush(Color.Parse("#AAB8C5")),
    };

    public override string ToString()
        => $"{Status,-2} {RelativePath}";
}

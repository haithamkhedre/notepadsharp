using System;
using System.Collections.Generic;

namespace NotepadSharp.App.Services;

public readonly record struct GitDiffMarkerPresentation(int LineNumber, char Kind, char Glyph);

public readonly record struct GitDiffMarkerSummary(int Added, int Modified, int Deleted)
{
    public bool HasChanges => Added > 0 || Modified > 0 || Deleted > 0;
}

public static class GitDiffMarkerPresentationLogic
{
    public static IReadOnlyList<GitDiffMarkerPresentation> GetVisibleMarkers(IReadOnlyList<char> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        var result = new List<GitDiffMarkerPresentation>();
        for (var index = 0; index < markers.Count; index++)
        {
            var kind = NormalizeKind(markers[index]);
            if (kind == ' ')
            {
                continue;
            }

            result.Add(new GitDiffMarkerPresentation(index + 1, kind, kind));
        }

        return result;
    }

    public static GitDiffMarkerSummary Summarize(IReadOnlyList<char> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        var added = 0;
        var modified = 0;
        var deleted = 0;
        foreach (var marker in markers)
        {
            switch (NormalizeKind(marker))
            {
                case '+':
                    added++;
                    break;
                case '~':
                    modified++;
                    break;
                case '-':
                    deleted++;
                    break;
            }
        }

        return new GitDiffMarkerSummary(added, modified, deleted);
    }

    public static string BuildTooltip(IReadOnlyList<char> markers)
    {
        var summary = Summarize(markers);
        if (!summary.HasChanges)
        {
            return "No Git line changes in this file.";
        }

        var parts = new List<string>(3);
        if (summary.Added > 0)
        {
            parts.Add($"{summary.Added} added");
        }

        if (summary.Modified > 0)
        {
            parts.Add($"{summary.Modified} modified");
        }

        if (summary.Deleted > 0)
        {
            parts.Add($"{summary.Deleted} deleted");
        }

        return $"Git line changes: {string.Join(" | ", parts)}.";
    }

    private static char NormalizeKind(char marker)
        => marker is '+' or '~' or '-' ? marker : ' ';
}

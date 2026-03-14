using System;
using System.Collections.Generic;
using System.Linq;

namespace NotepadSharp.App.Services;

public static class GitDiffNavigationLogic
{
    public static IReadOnlyList<int> GetChangeAnchorLines(IReadOnlyList<char> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);

        var lines = new List<int>();
        for (var index = 0; index < markers.Count; index++)
        {
            if (markers[index] == ' ')
            {
                continue;
            }

            if (index == 0 || markers[index - 1] == ' ')
            {
                lines.Add(index + 1);
            }
        }

        return lines;
    }

    public static int? GetTargetLine(IReadOnlyList<char> markers, int currentLine, bool forward)
    {
        var anchors = GetChangeAnchorLines(markers);
        if (anchors.Count == 0)
        {
            return null;
        }

        return forward
            ? anchors.FirstOrDefault(line => line > currentLine, anchors[0])
            : anchors.LastOrDefault(line => line < currentLine, anchors[^1]);
    }
}

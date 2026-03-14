using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NotepadSharp.App.Services;

public readonly record struct GitDiffCompareLineMap(
    IReadOnlyList<int> PrimaryLines,
    IReadOnlyList<int> SecondaryLines)
{
    public bool HasChanges => PrimaryLines.Count > 0 || SecondaryLines.Count > 0;
}

public static class GitDiffCompareHighlightLogic
{
    private static readonly Regex GitHunkHeaderRegex = new(
        @"^@@ -(?<oldStart>\d+)(,(?<oldCount>\d+))? \+(?<newStart>\d+)(,(?<newCount>\d+))? @@",
        RegexOptions.Compiled);

    public static IReadOnlyList<string> BuildPatchArguments(GitChangeSection section, string? relativePath)
    {
        var arguments = new List<string>
        {
            "diff",
            "--no-color",
            "--unified=0",
        };

        if (section == GitChangeSection.Staged)
        {
            arguments.Add("--cached");
        }

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            arguments.Add("--");
            arguments.Add(relativePath);
        }

        return arguments;
    }

    public static GitDiffCompareLineMap BuildLineMap(
        GitChangeSection section,
        string? status,
        string patchText,
        int primaryLineCount,
        int secondaryLineCount)
    {
        var normalizedStatus = status?.Trim() ?? string.Empty;
        if ((section == GitChangeSection.Unstaged && normalizedStatus.Contains("?", StringComparison.Ordinal))
            || (section == GitChangeSection.Staged && normalizedStatus.Contains("A", StringComparison.Ordinal)))
        {
            return new GitDiffCompareLineMap(Array.Empty<int>(), BuildWholeFileLineList(secondaryLineCount));
        }

        if (section != GitChangeSection.Conflicts && normalizedStatus.Contains("D", StringComparison.Ordinal))
        {
            return new GitDiffCompareLineMap(BuildWholeFileLineList(primaryLineCount), Array.Empty<int>());
        }

        return BuildLineMapFromPatch(patchText, primaryLineCount, secondaryLineCount);
    }

    public static GitDiffCompareLineMap BuildLineMapFromPatch(
        string patchText,
        int primaryLineCount,
        int secondaryLineCount)
    {
        if (string.IsNullOrWhiteSpace(patchText))
        {
            return new GitDiffCompareLineMap(Array.Empty<int>(), Array.Empty<int>());
        }

        var primaryLines = new HashSet<int>();
        var secondaryLines = new HashSet<int>();
        var inHunk = false;
        var currentOldLine = 1;
        var currentNewLine = 1;

        foreach (var rawLine in patchText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var hunkHeader = GitHunkHeaderRegex.Match(line);
            if (hunkHeader.Success)
            {
                inHunk = true;
                currentOldLine = ParseLineNumber(hunkHeader.Groups["oldStart"].Value);
                currentNewLine = ParseLineNumber(hunkHeader.Groups["newStart"].Value);
                continue;
            }

            if (!inHunk)
            {
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                var nextHeader = GitHunkHeaderRegex.Match(line);
                if (nextHeader.Success)
                {
                    currentOldLine = ParseLineNumber(nextHeader.Groups["oldStart"].Value);
                    currentNewLine = ParseLineNumber(nextHeader.Groups["newStart"].Value);
                }

                continue;
            }

            if (line.StartsWith("diff --", StringComparison.Ordinal))
            {
                inHunk = false;
                continue;
            }

            if (line.StartsWith("---", StringComparison.Ordinal)
                || line.StartsWith("+++", StringComparison.Ordinal)
                || line.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                TryAddLine(primaryLines, currentOldLine, primaryLineCount);
                currentOldLine++;
                continue;
            }

            if (line.StartsWith("+", StringComparison.Ordinal))
            {
                TryAddLine(secondaryLines, currentNewLine, secondaryLineCount);
                currentNewLine++;
                continue;
            }

            if (line.StartsWith(" ", StringComparison.Ordinal))
            {
                currentOldLine++;
                currentNewLine++;
            }
        }

        return new GitDiffCompareLineMap(
            primaryLines.OrderBy(static line => line).ToArray(),
            secondaryLines.OrderBy(static line => line).ToArray());
    }

    public static char[] BuildMarkers(int lineCount, IReadOnlyList<int> diffLines, char kind)
    {
        var markers = Enumerable.Repeat(' ', Math.Max(1, lineCount)).ToArray();
        if (kind is not ('+' or '~' or '-'))
        {
            return markers;
        }

        foreach (var line in diffLines)
        {
            if (line >= 1 && line <= markers.Length)
            {
                markers[line - 1] = kind;
            }
        }

        return markers;
    }

    private static int[] BuildWholeFileLineList(int lineCount)
        => Enumerable.Range(1, Math.Max(0, lineCount)).ToArray();

    private static int ParseLineNumber(string value)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 1;

    private static void TryAddLine(HashSet<int> lines, int lineNumber, int maxLineCount)
    {
        if (lineNumber < 1 || lineNumber > maxLineCount)
        {
            return;
        }

        lines.Add(lineNumber);
    }
}

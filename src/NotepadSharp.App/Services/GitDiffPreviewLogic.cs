using System;
using System.Collections.Generic;

namespace NotepadSharp.App.Services;

public static class GitDiffPreviewLogic
{
    public const int DefaultMaxPreviewChars = 24_000;

    public static IReadOnlyList<string> BuildDiffArguments(GitChangeSection section, string? relativePath)
    {
        var arguments = new List<string>
        {
            "diff",
            "--no-color",
            "--unified=3",
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

    public static string BuildUntrackedPreview(string relativePath, string contents, int maxChars = DefaultMaxPreviewChars)
    {
        var header = string.IsNullOrWhiteSpace(relativePath)
            ? "Untracked file. No base revision is available.\n\n"
            : $"Untracked file: {relativePath}\nNo base revision is available.\n\n";

        return TrimPreview(header + (contents ?? string.Empty), maxChars);
    }

    public static string TrimPreview(string text, int maxChars = DefaultMaxPreviewChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        var truncated = text[..maxChars].TrimEnd();
        return $"{truncated}\n\n[preview truncated]";
    }
}

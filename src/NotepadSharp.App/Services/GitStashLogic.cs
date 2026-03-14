using System;
using System.Collections.Generic;

namespace NotepadSharp.App.Services;

public sealed record GitStashEntry(string Reference, string Summary);

public static class GitStashLogic
{
    public static IReadOnlyList<GitStashEntry> ParseEntries(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<GitStashEntry>();
        }

        var entries = new List<GitStashEntry>();
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var reference = line[..separator].Trim();
            var summary = line[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            entries.Add(new GitStashEntry(reference, string.IsNullOrWhiteSpace(summary) ? reference : summary));
        }

        return entries;
    }

    public static IReadOnlyList<string> BuildPushArguments(string? message)
    {
        var arguments = new List<string>
        {
            "stash",
            "push",
            "--include-untracked",
        };

        if (!string.IsNullOrWhiteSpace(message))
        {
            arguments.Add("-m");
            arguments.Add(message.Trim());
        }

        return arguments;
    }

    public static IReadOnlyList<string> BuildApplyArguments(string stashReference)
        => new[] { "stash", "apply", stashReference };

    public static IReadOnlyList<string> BuildPopArguments(string stashReference)
        => new[] { "stash", "pop", stashReference };

    public static IReadOnlyList<string> BuildDropArguments(string stashReference)
        => new[] { "stash", "drop", stashReference };

    public static string BuildSelectionId(string stashReference)
        => $"stash:{stashReference}";

    public static bool TryParseSelection(string? selectionId, out string stashReference)
    {
        stashReference = string.Empty;
        if (string.IsNullOrWhiteSpace(selectionId))
        {
            return false;
        }

        const string prefix = "stash:";
        if (!selectionId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        stashReference = selectionId[prefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(stashReference);
    }

    public static bool IndicatesConflictOrPartialApply(string? stdout, string? stderr)
    {
        var combined = $"{stdout}\n{stderr}";
        return combined.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("merge conflict", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("Auto-merging", StringComparison.OrdinalIgnoreCase);
    }
}

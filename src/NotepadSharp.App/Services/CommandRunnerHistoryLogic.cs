using System;
using System.Collections.Generic;
using System.Linq;

namespace NotepadSharp.App.Services;

public static class CommandRunnerHistoryLogic
{
    public static IReadOnlyList<string> NormalizeHistory(IEnumerable<string>? entries, int maxEntries = 25)
    {
        var normalized = new List<string>();
        if (entries is null)
        {
            return normalized;
        }

        foreach (var entry in entries)
        {
            var command = entry?.Trim();
            if (string.IsNullOrWhiteSpace(command)
                || normalized.Any(existing => string.Equals(existing, command, StringComparison.Ordinal)))
            {
                continue;
            }

            normalized.Add(command);
            if (normalized.Count >= maxEntries)
            {
                break;
            }
        }

        return normalized;
    }

    public static IReadOnlyList<string> RecordCommand(IEnumerable<string>? entries, string? command, int maxEntries = 25)
    {
        var normalizedCommand = command?.Trim();
        var merged = new List<string>();
        if (!string.IsNullOrWhiteSpace(normalizedCommand))
        {
            merged.Add(normalizedCommand);
        }

        foreach (var entry in NormalizeHistory(entries, maxEntries))
        {
            if (string.Equals(entry, normalizedCommand, StringComparison.Ordinal))
            {
                continue;
            }

            merged.Add(entry);
            if (merged.Count >= maxEntries)
            {
                break;
            }
        }

        return merged;
    }
}

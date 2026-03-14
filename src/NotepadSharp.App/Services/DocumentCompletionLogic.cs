using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NotepadSharp.App.Services;

public static class DocumentCompletionLogic
{
    public static IReadOnlyList<string> GetSuggestions(
        string text,
        string language,
        IEnumerable<string> keywords,
        string prefix,
        int maxSuggestions = 150)
    {
        var suggestions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyword in keywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                suggestions.Add(keyword);
            }
        }

        foreach (var symbol in DocumentSymbolLogic.GetSymbols(text, language))
        {
            if (!string.IsNullOrWhiteSpace(symbol.Title))
            {
                suggestions.Add(symbol.Title.Trim());
            }
        }

        foreach (Match match in Regex.Matches(text ?? string.Empty, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
        {
            if (!match.Success || match.Length < 2)
            {
                continue;
            }

            suggestions.Add(match.Value);
            if (suggestions.Count > 3000)
            {
                break;
            }
        }

        return suggestions
            .Where(s => string.IsNullOrWhiteSpace(prefix) || s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Max(1, maxSuggestions))
            .ToList();
    }
}

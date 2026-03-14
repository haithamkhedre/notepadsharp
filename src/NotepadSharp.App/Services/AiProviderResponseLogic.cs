using System;
using System.Text.RegularExpressions;

namespace NotepadSharp.App.Services;

public static class AiProviderResponseLogic
{
    private static readonly Regex MarkdownCodeBlockRegex = new(
        "```[^\\n`]*\\n(?<code>[\\s\\S]*?)\\n```",
        RegexOptions.Compiled);

    public static string ExtractPrimaryText(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return string.Empty;
        }

        var trimmed = responseText.Trim();
        var match = MarkdownCodeBlockRegex.Match(trimmed);
        if (match.Success)
        {
            return match.Groups["code"].Value.TrimEnd();
        }

        return trimmed;
    }
}

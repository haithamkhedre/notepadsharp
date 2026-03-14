using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NotepadSharp.App.Services;

public sealed record SyntaxToolRunResult(bool Success, int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => SyntaxToolDiagnosticLogic.CombineOutput(StandardOutput, StandardError);
}

public sealed record SyntaxToolDiagnosticInfo(string Severity, int Line, int Column, string Message);

public static class SyntaxToolDiagnosticLogic
{
    private static readonly Regex PythonFileLineRegex = new(
        "File\\s+\"[^\"]+\",\\s+line\\s+(?<line>\\d+)",
        RegexOptions.Compiled);
    private static readonly Regex PythonInlineLineRegex = new(
        "\\([^,]+,\\s+line\\s+(?<line>\\d+)\\)",
        RegexOptions.Compiled);
    private static readonly Regex JavaScriptLocationRegex = new(
        "^.+:(?<line>\\d+)(?::(?<column>\\d+))?$",
        RegexOptions.Compiled);
    private static readonly Regex TypeScriptDiagnosticRegex = new(
        "^\\s*.+\\((?<line>\\d+),(?<column>\\d+)\\):\\s*(?<severity>error|warning)\\s*(?<code>TS\\d+)?\\s*:\\s*(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<SyntaxToolDiagnosticInfo> ParsePython(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<SyntaxToolDiagnosticInfo>();
        }

        var lines = SplitLines(output);
        var line = 1;
        var column = 1;
        var foundLocation = false;

        foreach (var rawLine in lines)
        {
            var lineMatch = PythonFileLineRegex.Match(rawLine);
            if (lineMatch.Success)
            {
                line = ParsePositiveInt(lineMatch.Groups["line"].Value, line);
                foundLocation = true;
            }

            var inlineMatch = PythonInlineLineRegex.Match(rawLine);
            if (inlineMatch.Success)
            {
                line = ParsePositiveInt(inlineMatch.Groups["line"].Value, line);
                foundLocation = true;
            }

            if (TryGetCaretColumn(rawLine, out var caretColumn))
            {
                column = caretColumn;
            }
        }

        var message = string.Empty;
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            var candidate = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(candidate)
                || string.Equals(candidate, "Traceback (most recent call last):", StringComparison.Ordinal)
                || PythonFileLineRegex.IsMatch(candidate)
                || TryGetCaretColumn(lines[index], out _))
            {
                continue;
            }

            message = candidate.StartsWith("Sorry: ", StringComparison.Ordinal)
                ? candidate["Sorry: ".Length..].Trim()
                : candidate;
            break;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            if (!foundLocation)
            {
                return Array.Empty<SyntaxToolDiagnosticInfo>();
            }

            message = "Python syntax error.";
        }

        return new[]
        {
            new SyntaxToolDiagnosticInfo("Error", line, column, message),
        };
    }

    public static IReadOnlyList<SyntaxToolDiagnosticInfo> ParseJavaScript(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<SyntaxToolDiagnosticInfo>();
        }

        var lines = SplitLines(output);
        var line = 1;
        var column = 1;
        var locationIndex = -1;

        for (var index = 0; index < lines.Count; index++)
        {
            var match = JavaScriptLocationRegex.Match(lines[index].TrimEnd());
            if (!match.Success)
            {
                continue;
            }

            line = ParsePositiveInt(match.Groups["line"].Value, line);
            column = ParsePositiveInt(match.Groups["column"].Value, column);
            locationIndex = index;
            break;
        }

        if (locationIndex >= 0 && column == 1)
        {
            for (var index = locationIndex + 1; index < lines.Count; index++)
            {
                if (TryGetCaretColumn(lines[index], out var caretColumn))
                {
                    column = caretColumn;
                    break;
                }
            }
        }

        var message = string.Empty;
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            var candidate = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(candidate)
                || TryGetCaretColumn(lines[index], out _)
                || candidate.StartsWith("at ", StringComparison.Ordinal)
                || candidate.StartsWith("Node.js", StringComparison.OrdinalIgnoreCase)
                || JavaScriptLocationRegex.IsMatch(candidate))
            {
                continue;
            }

            message = candidate;
            break;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            if (locationIndex < 0)
            {
                return Array.Empty<SyntaxToolDiagnosticInfo>();
            }

            message = "JavaScript syntax error.";
        }

        return new[]
        {
            new SyntaxToolDiagnosticInfo("Error", line, column, message),
        };
    }

    public static IReadOnlyList<SyntaxToolDiagnosticInfo> ParseTypeScript(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<SyntaxToolDiagnosticInfo>();
        }

        var diagnostics = new List<SyntaxToolDiagnosticInfo>();
        foreach (var rawLine in SplitLines(output))
        {
            var match = TypeScriptDiagnosticRegex.Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            var line = ParsePositiveInt(match.Groups["line"].Value, 1);
            var column = ParsePositiveInt(match.Groups["column"].Value, 1);
            var severity = string.Equals(match.Groups["severity"].Value, "warning", StringComparison.OrdinalIgnoreCase)
                ? "Warning"
                : "Error";
            var code = match.Groups["code"].Value.Trim();
            var message = match.Groups["message"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(code))
            {
                message = $"{code}: {message}";
            }

            diagnostics.Add(new SyntaxToolDiagnosticInfo(severity, line, column, message));
        }

        return diagnostics;
    }

    public static string CombineOutput(string standardOutput, string standardError)
    {
        var trimmedOutput = standardOutput?.Trim() ?? string.Empty;
        var trimmedError = standardError?.Trim() ?? string.Empty;
        if (trimmedOutput.Length == 0)
        {
            return trimmedError;
        }

        if (trimmedError.Length == 0)
        {
            return trimmedOutput;
        }

        return $"{trimmedOutput}{Environment.NewLine}{trimmedError}";
    }

    public static string SummarizeOutput(string standardOutput, string standardError)
    {
        foreach (var rawLine in SplitLines(CombineOutput(standardOutput, standardError)))
        {
            var line = rawLine.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return string.Empty;
    }

    private static List<string> SplitLines(string text)
        => new(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'));

    private static int ParsePositiveInt(string value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static bool TryGetCaretColumn(string line, out int column)
    {
        var caretIndex = line.IndexOf('^');
        if (caretIndex < 0)
        {
            column = 1;
            return false;
        }

        column = caretIndex + 1;
        return true;
    }
}

using System;
using System.Text.RegularExpressions;

namespace NotepadSharp.Core;

public static class TextSearchEngine
{
    public static (int index, int length)? FindMatchInRange(
        string fullText,
        string query,
        int startIndex,
        bool forward,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        bool wrapAround,
        int rangeStart,
        int rangeEnd)
    {
        fullText ??= string.Empty;
        query ??= string.Empty;

        rangeStart = Math.Clamp(rangeStart, 0, fullText.Length);
        rangeEnd = Math.Clamp(rangeEnd, 0, fullText.Length);
        if (rangeEnd <= rangeStart || query.Length == 0)
        {
            return null;
        }

        if (rangeStart == 0 && rangeEnd == fullText.Length)
        {
            return FindMatch(fullText, query, startIndex, forward, matchCase, wholeWord, useRegex, wrapAround);
        }

        var segment = fullText.Substring(rangeStart, rangeEnd - rangeStart);
        var segStartIndex = Math.Clamp(startIndex - rangeStart, 0, segment.Length);

        var match = FindMatch(segment, query, segStartIndex, forward, matchCase, wholeWord, useRegex, wrapAround);
        return match is null ? null : (match.Value.index + rangeStart, match.Value.length);
    }

    public static int CountMatchesInRange(
        string fullText,
        string query,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        int rangeStart,
        int rangeEnd)
    {
        fullText ??= string.Empty;
        query ??= string.Empty;

        if (query.Length == 0)
        {
            return 0;
        }

        rangeStart = Math.Clamp(rangeStart, 0, fullText.Length);
        rangeEnd = Math.Clamp(rangeEnd, 0, fullText.Length);
        if (rangeEnd <= rangeStart)
        {
            return 0;
        }

        var segment = fullText.Substring(rangeStart, rangeEnd - rangeStart);
        if (segment.Length == 0)
        {
            return 0;
        }

        if (useRegex)
        {
            var regex = TryCreateRegex(query, matchCase, wholeWord);
            if (regex is null)
            {
                return 0;
            }

            var count = 0;
            foreach (Match m in regex.Matches(segment))
            {
                if (m.Success && m.Length > 0)
                {
                    count++;
                }
            }

            return count;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var idx = 0;
        var total = 0;
        while (idx <= segment.Length)
        {
            var next = segment.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(segment, next, query.Length))
            {
                total++;
            }

            idx = next + 1;
        }

        return total;
    }

    public static Regex? TryCreateRegex(string pattern, bool matchCase, bool wholeWord)
    {
        pattern ??= string.Empty;

        try
        {
            if (wholeWord)
            {
                pattern = $"(?<!\\w)(?:{pattern})(?!\\w)";
            }

            var options = RegexOptions.Compiled;
            if (!matchCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            return new Regex(pattern, options);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsWholeWordAt(string text, int index, int length)
    {
        text ??= string.Empty;
        if (length <= 0 || index < 0 || index + length > text.Length)
        {
            return false;
        }

        var leftOk = index == 0 || !IsWordChar(text[index - 1]);
        var rightIndex = index + length;
        var rightOk = rightIndex >= text.Length || !IsWordChar(text[rightIndex]);
        return leftOk && rightOk;
    }

    private static (int index, int length)? FindMatch(
        string text,
        string query,
        int startIndex,
        bool forward,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        bool wrapAround)
    {
        if (useRegex)
        {
            var regex = TryCreateRegex(query, matchCase, wholeWord);
            if (regex is null)
            {
                return null;
            }

            return forward
                ? FindNextRegex(text, regex, startIndex, wrapAround)
                : FindPrevRegex(text, regex, startIndex, wrapAround);
        }

        return forward
            ? FindNextPlain(text, query, startIndex, matchCase, wholeWord, wrapAround)
            : FindPrevPlain(text, query, startIndex, matchCase, wholeWord, wrapAround);
    }

    private static (int index, int length)? FindNextRegex(string text, Regex regex, int startIndex, bool wrapAround)
    {
        startIndex = Math.Clamp(startIndex, 0, text.Length);

        var m = regex.Match(text, startIndex);
        if (m.Success)
        {
            return (m.Index, m.Length);
        }

        if (!wrapAround)
        {
            return null;
        }

        m = regex.Match(text, 0);
        return m.Success ? (m.Index, m.Length) : null;
    }

    private static (int index, int length)? FindPrevRegex(string text, Regex regex, int startIndex, bool wrapAround)
    {
        if (text.Length == 0)
        {
            return null;
        }

        startIndex = Math.Clamp(startIndex, 0, text.Length - 1);

        Match? last = null;
        foreach (Match m in regex.Matches(text))
        {
            if (!m.Success)
            {
                continue;
            }

            if (m.Index > startIndex)
            {
                break;
            }

            last = m;
        }

        if (last is not null)
        {
            return (last.Index, last.Length);
        }

        if (!wrapAround)
        {
            return null;
        }

        Match? final = null;
        foreach (Match m in regex.Matches(text))
        {
            if (m.Success)
            {
                final = m;
            }
        }

        return final is null ? null : (final.Index, final.Length);
    }

    private static (int index, int length)? FindNextPlain(string text, string query, int startIndex, bool matchCase, bool wholeWord, bool wrapAround)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        startIndex = Math.Clamp(startIndex, 0, text.Length);

        var idx = startIndex;
        while (idx <= text.Length)
        {
            var next = text.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, next, query.Length))
            {
                return (next, query.Length);
            }

            idx = next + 1;
        }

        if (!wrapAround)
        {
            return null;
        }

        idx = 0;
        while (idx < startIndex)
        {
            var next = text.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, next, query.Length))
            {
                return (next, query.Length);
            }

            idx = next + 1;
        }

        return null;
    }

    private static (int index, int length)? FindPrevPlain(string text, string query, int startIndex, bool matchCase, bool wholeWord, bool wrapAround)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (text.Length == 0)
        {
            return null;
        }

        startIndex = Math.Clamp(startIndex, 0, text.Length - 1);

        var idx = startIndex;
        while (idx >= 0)
        {
            var prev = text.LastIndexOf(query, idx, comparison);
            if (prev < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, prev, query.Length))
            {
                return (prev, query.Length);
            }

            idx = prev - 1;
        }

        if (!wrapAround)
        {
            return null;
        }

        idx = text.Length - 1;
        while (idx > startIndex)
        {
            var prev = text.LastIndexOf(query, idx, comparison);
            if (prev < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, prev, query.Length))
            {
                return (prev, query.Length);
            }

            idx = prev - 1;
        }

        return null;
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';
}

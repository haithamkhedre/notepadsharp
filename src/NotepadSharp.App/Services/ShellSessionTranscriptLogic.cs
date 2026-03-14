using System;
using System.Text;

namespace NotepadSharp.App.Services;

public sealed record ShellTranscriptAppendResult(
    string Text,
    int Cursor,
    string PendingControlSequence,
    bool InAlternateScreen,
    int? SavedCursor);

public static class ShellSessionTranscriptLogic
{
    private const string TruncatedPrefix = "[output truncated]\n";

    public static string NormalizeChunk(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                builder.Append('\n');
                index++;
                continue;
            }

            if (ch == '\0')
            {
                continue;
            }

            if (char.IsControl(ch) && ch is not ('\n' or '\r' or '\t' or '\b' or '\u001b'))
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    public static string AppendChunk(string? currentTranscript, string? nextChunk, int maxChars)
        => AppendChunk(
            currentTranscript,
            GetDefaultCursor(currentTranscript),
            string.Empty,
            currentInAlternateScreen: false,
            savedCursor: null,
            nextChunk,
            maxChars).Text;

    public static ShellTranscriptAppendResult AppendChunk(
        string? currentTranscript,
        int currentCursor,
        string? pendingControlSequence,
        bool currentInAlternateScreen,
        int? savedCursor,
        string? nextChunk,
        int maxChars)
    {
        maxChars = Math.Max(1, maxChars);

        var current = currentTranscript ?? string.Empty;
        if (current.StartsWith(TruncatedPrefix, StringComparison.Ordinal))
        {
            current = current[TruncatedPrefix.Length..];
        }

        var cursor = Math.Clamp(currentCursor, 0, current.Length);
        var combinedChunk = (pendingControlSequence ?? string.Empty) + NormalizeChunk(nextChunk);
        var rendered = RenderChunk(current, cursor, combinedChunk, currentInAlternateScreen, savedCursor);
        return TrimTranscript(
            rendered.Text,
            rendered.Cursor,
            rendered.PendingControlSequence,
            rendered.InAlternateScreen,
            rendered.SavedCursor,
            maxChars);
    }

    private static ShellTranscriptAppendResult RenderChunk(
        string currentTranscript,
        int currentCursor,
        string chunk,
        bool currentInAlternateScreen,
        int? currentSavedCursor)
    {
        var builder = new StringBuilder(currentTranscript ?? string.Empty);
        var cursor = Math.Clamp(currentCursor, 0, builder.Length);
        var inAlternateScreen = currentInAlternateScreen;
        var savedCursor = currentSavedCursor;

        for (var index = 0; index < chunk.Length; index++)
        {
            var ch = chunk[index];
            if (ch == '\u001b')
            {
                var escapeResult = TryConsumeEscapeSequence(chunk, index, builder, ref cursor, ref savedCursor, applyEffects: true);
                if (escapeResult.PendingControlSequence is not null)
                {
                    return new ShellTranscriptAppendResult(
                        builder.ToString(),
                        cursor,
                        escapeResult.PendingControlSequence,
                        inAlternateScreen,
                        savedCursor);
                }

                if (escapeResult.AlternateScreenMode is bool enteringAlternateScreen)
                {
                    inAlternateScreen = enteringAlternateScreen;
                    if (enteringAlternateScreen)
                    {
                        builder.Clear();
                        cursor = 0;
                        savedCursor = null;
                    }
                    else if (builder.Length > 0 && builder[^1] != '\n')
                    {
                        builder.Append('\n');
                        cursor = builder.Length;
                    }
                }

                index = escapeResult.NextIndex;
                continue;
            }

            switch (ch)
            {
                case '\r':
                    cursor = FindLineStart(builder, cursor);
                    break;
                case '\n':
                    cursor = FindLineEnd(builder, cursor);
                    if (cursor == builder.Length)
                    {
                        builder.Append('\n');
                        cursor = builder.Length;
                    }
                    else
                    {
                        cursor++;
                    }

                    break;
                case '\b':
                    cursor = Math.Max(FindLineStart(builder, cursor), cursor - 1);
                    break;
                default:
                    WriteCharacter(builder, ref cursor, ch);
                    break;
            }
        }

        return new ShellTranscriptAppendResult(builder.ToString(), cursor, string.Empty, inAlternateScreen, savedCursor);
    }

    private static EscapeConsumptionResult TryConsumeEscapeSequence(
        string text,
        int escapeStart,
        StringBuilder builder,
        ref int cursor,
        ref int? savedCursor,
        bool applyEffects)
    {
        if (escapeStart + 1 >= text.Length)
        {
            return new EscapeConsumptionResult(escapeStart, text[escapeStart..]);
        }

        var next = text[escapeStart + 1];
        if (next == '[')
        {
            var finalIndex = escapeStart + 2;
            while (finalIndex < text.Length && !IsCsiFinal(text[finalIndex]))
            {
                finalIndex++;
            }

            if (finalIndex >= text.Length)
            {
                return new EscapeConsumptionResult(escapeStart, text[escapeStart..]);
            }

            if (TryGetAlternateScreenMode(text[(escapeStart + 2)..finalIndex], text[finalIndex], out var enteringAlternateScreen))
            {
                return new EscapeConsumptionResult(finalIndex, null, enteringAlternateScreen);
            }

            if (applyEffects)
            {
                HandleCsiSequence(builder, ref cursor, ref savedCursor, text[(escapeStart + 2)..finalIndex], text[finalIndex]);
            }

            return new EscapeConsumptionResult(finalIndex, null);
        }

        if (next == ']')
        {
            var index = escapeStart + 2;
            while (index < text.Length)
            {
                if (text[index] == '\u0007')
                {
                    return new EscapeConsumptionResult(index, null);
                }

                if (text[index] == '\u001b')
                {
                    if (index + 1 >= text.Length)
                    {
                        return new EscapeConsumptionResult(escapeStart, text[escapeStart..]);
                    }

                    if (text[index + 1] == '\\')
                    {
                        return new EscapeConsumptionResult(index + 1, null);
                    }
                }

                index++;
            }

            return new EscapeConsumptionResult(escapeStart, text[escapeStart..]);
        }

        if (next == 'c')
        {
            if (applyEffects)
            {
                builder.Clear();
                cursor = 0;
                savedCursor = null;
            }

            return new EscapeConsumptionResult(escapeStart + 1, null);
        }

        if (applyEffects && next == '7')
        {
            savedCursor = cursor;
            return new EscapeConsumptionResult(escapeStart + 1, null);
        }

        if (applyEffects && next == '8')
        {
            if (savedCursor is int restoredCursor)
            {
                cursor = Math.Clamp(restoredCursor, 0, builder.Length);
            }

            return new EscapeConsumptionResult(escapeStart + 1, null);
        }

        return new EscapeConsumptionResult(escapeStart + 1, null);
    }

    private static void HandleCsiSequence(StringBuilder builder, ref int cursor, ref int? savedCursor, string parameters, char finalChar)
    {
        switch (finalChar)
        {
            case 'm':
                return;
            case 'K':
                HandleEraseInLine(builder, ref cursor, parameters);
                return;
            case 'J':
                HandleEraseInDisplay(builder, ref cursor, parameters);
                return;
            case 'H':
            case 'f':
                HandleCursorPosition(builder, ref cursor, parameters);
                return;
            case 'G':
                HandleCursorColumn(builder, ref cursor, parameters);
                return;
            case 'A':
                MoveCursorVertically(builder, ref cursor, -ParseParameter(parameters, 1), moveToLineStart: false);
                return;
            case 'B':
                MoveCursorVertically(builder, ref cursor, ParseParameter(parameters, 1), moveToLineStart: false);
                return;
            case 'C':
                MoveCursorHorizontally(builder, ref cursor, ParseParameter(parameters, 1));
                return;
            case 'D':
                MoveCursorHorizontally(builder, ref cursor, -ParseParameter(parameters, 1));
                return;
            case 'E':
                MoveCursorVertically(builder, ref cursor, ParseParameter(parameters, 1), moveToLineStart: true);
                return;
            case 'F':
                MoveCursorVertically(builder, ref cursor, -ParseParameter(parameters, 1), moveToLineStart: true);
                return;
            case 's':
                savedCursor = cursor;
                return;
            case 'u':
                if (savedCursor is int restoredCursor)
                {
                    cursor = Math.Clamp(restoredCursor, 0, builder.Length);
                }

                return;
            default:
                return;
        }
    }

    private static void HandleEraseInLine(StringBuilder builder, ref int cursor, string parameters)
    {
        var mode = ParseParameter(parameters, 0);
        var lineStart = FindLineStart(builder, cursor);
        var lineEnd = FindLineEnd(builder, cursor);

        switch (mode)
        {
            case 1:
                var removeToCursor = Math.Clamp(cursor - lineStart, 0, lineEnd - lineStart);
                builder.Remove(lineStart, removeToCursor);
                cursor = lineStart;
                break;
            case 2:
                builder.Remove(lineStart, lineEnd - lineStart);
                cursor = lineStart;
                break;
            default:
                builder.Remove(cursor, Math.Max(0, lineEnd - cursor));
                break;
        }
    }

    private static void HandleEraseInDisplay(StringBuilder builder, ref int cursor, string parameters)
    {
        var mode = ParseParameter(parameters, 0);
        switch (mode)
        {
            case 1:
                builder.Remove(0, Math.Min(cursor, builder.Length));
                cursor = 0;
                break;
            case 2:
            case 3:
                builder.Clear();
                cursor = 0;
                break;
            default:
                builder.Remove(cursor, Math.Max(0, builder.Length - cursor));
                break;
        }
    }

    private static void HandleCursorPosition(StringBuilder builder, ref int cursor, string parameters)
    {
        var parts = parameters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var row = Math.Max(1, parts.Length > 0 ? ParseParameter(parts[0], 1) : 1);
        var column = Math.Max(1, parts.Length > 1 ? ParseParameter(parts[1], 1) : 1);
        var lineStart = FindLineStartForRow(builder, row);
        EnsureColumn(builder, lineStart, column - 1);
        var lineEnd = FindLineEnd(builder, lineStart);
        cursor = Math.Clamp(lineStart + column - 1, lineStart, lineEnd);
    }

    private static void HandleCursorColumn(StringBuilder builder, ref int cursor, string parameters)
    {
        var column = Math.Max(1, ParseParameter(parameters, 1));
        var lineStart = FindLineStart(builder, cursor);
        EnsureColumn(builder, lineStart, column - 1);
        var lineEnd = FindLineEnd(builder, lineStart);
        cursor = Math.Clamp(lineStart + column - 1, lineStart, lineEnd);
    }

    private static void MoveCursorHorizontally(StringBuilder builder, ref int cursor, int delta)
    {
        var lineStart = FindLineStart(builder, cursor);
        var targetCursor = Math.Max(lineStart, cursor + delta);
        EnsureColumn(builder, lineStart, targetCursor - lineStart);
        var lineEnd = FindLineEnd(builder, lineStart);
        cursor = Math.Clamp(targetCursor, lineStart, lineEnd);
    }

    private static void MoveCursorVertically(StringBuilder builder, ref int cursor, int lineDelta, bool moveToLineStart)
    {
        if (lineDelta == 0)
        {
            return;
        }

        var currentLineStart = FindLineStart(builder, cursor);
        var targetLineStart = currentLineStart;
        var column = moveToLineStart ? 0 : Math.Max(0, cursor - currentLineStart);

        if (lineDelta > 0)
        {
            for (var step = 0; step < lineDelta; step++)
            {
                targetLineStart = FindNextLineStart(builder, targetLineStart);
            }
        }
        else
        {
            for (var step = 0; step < -lineDelta; step++)
            {
                targetLineStart = FindPreviousLineStart(builder, targetLineStart);
            }
        }

        EnsureColumn(builder, targetLineStart, column);
        var lineEnd = FindLineEnd(builder, targetLineStart);
        cursor = Math.Clamp(targetLineStart + column, targetLineStart, lineEnd);
    }

    private static void WriteCharacter(StringBuilder builder, ref int cursor, char ch)
    {
        if (cursor < builder.Length)
        {
            if (builder[cursor] == '\n')
            {
                builder.Insert(cursor, ch);
            }
            else
            {
                builder[cursor] = ch;
            }

            cursor++;
            return;
        }

        builder.Append(ch);
        cursor = builder.Length;
    }

    private static int FindLineStart(StringBuilder builder, int cursor)
    {
        var clampedCursor = Math.Clamp(cursor, 0, builder.Length);
        for (var index = clampedCursor - 1; index >= 0; index--)
        {
            if (builder[index] == '\n')
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static int FindLineEnd(StringBuilder builder, int cursor)
    {
        var clampedCursor = Math.Clamp(cursor, 0, builder.Length);
        for (var index = clampedCursor; index < builder.Length; index++)
        {
            if (builder[index] == '\n')
            {
                return index;
            }
        }

        return builder.Length;
    }

    private static int FindPreviousLineStart(StringBuilder builder, int currentLineStart)
    {
        if (currentLineStart <= 0)
        {
            return 0;
        }

        for (var index = currentLineStart - 2; index >= 0; index--)
        {
            if (builder[index] == '\n')
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static int FindNextLineStart(StringBuilder builder, int currentLineStart)
    {
        var currentLineEnd = FindLineEnd(builder, currentLineStart);
        if (currentLineEnd < builder.Length)
        {
            return currentLineEnd + 1;
        }

        builder.Append('\n');
        return builder.Length;
    }

    private static int FindLineStartForRow(StringBuilder builder, int row)
    {
        var lineStart = 0;
        for (var currentRow = 1; currentRow < row; currentRow++)
        {
            lineStart = FindNextLineStart(builder, lineStart);
        }

        return lineStart;
    }

    private static void EnsureColumn(StringBuilder builder, int lineStart, int zeroBasedColumn)
    {
        if (zeroBasedColumn <= 0)
        {
            return;
        }

        var lineEnd = FindLineEnd(builder, lineStart);
        var currentWidth = Math.Max(0, lineEnd - lineStart);
        if (currentWidth >= zeroBasedColumn)
        {
            return;
        }

        builder.Insert(lineEnd, new string(' ', zeroBasedColumn - currentWidth));
    }

    private static ShellTranscriptAppendResult TrimTranscript(
        string text,
        int cursor,
        string pendingControlSequence,
        bool inAlternateScreen,
        int? savedCursor,
        int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return new ShellTranscriptAppendResult(text, cursor, pendingControlSequence, inAlternateScreen, savedCursor);
        }

        var tailLength = Math.Max(0, maxChars - TruncatedPrefix.Length);
        var removeCount = Math.Max(0, text.Length - tailLength);
        var tail = tailLength == 0 ? string.Empty : text[^tailLength..];
        var trimmedCursor = Math.Clamp(cursor - removeCount, 0, tail.Length);
        int? trimmedSavedCursor = savedCursor is int value
            ? Math.Clamp(value - removeCount, 0, tail.Length)
            : null;
        return new ShellTranscriptAppendResult(
            TruncatedPrefix + tail,
            trimmedCursor,
            pendingControlSequence,
            inAlternateScreen,
            trimmedSavedCursor);
    }

    private static bool IsCsiFinal(char ch)
        => ch is >= '@' and <= '~';

    private static bool TryGetAlternateScreenMode(string parameters, char finalChar, out bool enteringAlternateScreen)
    {
        enteringAlternateScreen = false;
        if (finalChar is not ('h' or 'l'))
        {
            return false;
        }

        var normalized = parameters.Trim();
        if (normalized is not "?47" and not "?1047" and not "?1049")
        {
            return false;
        }

        enteringAlternateScreen = finalChar == 'h';
        return true;
    }

    private static int ParseParameter(string? parameterText, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(parameterText))
        {
            return defaultValue;
        }

        var normalized = parameterText.Trim();
        if (normalized.Length > 0 && normalized[0] == '?')
        {
            normalized = normalized[1..];
        }

        return int.TryParse(normalized, out var value)
            ? value
            : defaultValue;
    }

    private static int GetDefaultCursor(string? currentTranscript)
    {
        var current = currentTranscript ?? string.Empty;
        return current.StartsWith(TruncatedPrefix, StringComparison.Ordinal)
            ? Math.Max(0, current.Length - TruncatedPrefix.Length)
            : current.Length;
    }

    private sealed record EscapeConsumptionResult(
        int NextIndex,
        string? PendingControlSequence,
        bool? AlternateScreenMode = null);
}

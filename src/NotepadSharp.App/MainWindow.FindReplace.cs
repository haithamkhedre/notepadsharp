using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using NotepadSharp.App.Dialogs;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private void OnShowFindClick(object? sender, RoutedEventArgs e)
        => ShowFind(replace: false);

    private void OnShowReplaceClick(object? sender, RoutedEventArgs e)
        => ShowFind(replace: true);

    private async void OnGoToLineClick(object? sender, RoutedEventArgs e)
        => await ShowGoToLineAsync();

    private void OnHideFindReplaceClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsFindReplaceVisible = false;
        _viewModel.IsReplaceVisible = false;
        _viewModel.FindSummary = string.Empty;
        EditorTextBox?.Focus();
    }

    private void ShowFind(bool replace)
    {
        _viewModel.IsFindReplaceVisible = true;
        _viewModel.IsReplaceVisible = replace;

        // Pre-fill find with current selection if any.
        if (EditorTextBox is not null && GetSelectionEnd() > EditorTextBox.SelectionStart)
        {
            var selected = EditorTextBox.SelectedText;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _viewModel.FindText = selected;
            }
        }

        UpdateFindSummary();
        (FindTextBox as Control)?.Focus();
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
        => FindNext(forward: true);

    private void OnFindPrevClick(object? sender, RoutedEventArgs e)
        => FindNext(forward: false);

    private void FindNext(bool forward)
    {
        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query) || EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var (rangeStart, rangeEnd) = GetSearchRange(text);
        if (rangeStart >= rangeEnd)
        {
            return;
        }

        var startIndex = GetSearchStartIndex(forward);
        startIndex = Math.Clamp(startIndex, rangeStart, rangeEnd);

        var match = FindMatchInRange(
            text,
            query,
            startIndex,
            forward,
            _viewModel.MatchCase,
            _viewModel.WholeWord,
            _viewModel.UseRegex,
            _viewModel.WrapAround,
            rangeStart,
            rangeEnd);
        if (match is null)
        {
            UpdateFindSummary();
            return;
        }

        SelectMatch(match.Value.index, match.Value.length);
        UpdateFindSummary();
    }

    private (int start, int end) GetSearchRange(string text)
    {
        if (!_viewModel.InSelection || EditorTextBox is null)
        {
            return (0, text.Length);
        }

        var selectionEnd = GetSelectionEnd();
        var start = Math.Min(EditorTextBox.SelectionStart, selectionEnd);
        var end = Math.Max(EditorTextBox.SelectionStart, selectionEnd);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        return (start, end);
    }

    private static (int index, int length)? FindMatchInRange(
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
        rangeStart = Math.Clamp(rangeStart, 0, fullText.Length);
        rangeEnd = Math.Clamp(rangeEnd, 0, fullText.Length);
        if (rangeEnd <= rangeStart)
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

    private int GetSearchStartIndex(bool forward)
    {
        if (EditorTextBox is null)
        {
            return 0;
        }

        if (forward)
        {
            var start = Math.Max(EditorTextBox.SelectionStart, 0);
            if (GetSelectionEnd() > EditorTextBox.SelectionStart)
            {
                start = GetSelectionEnd();
            }

            return start;
        }

        var s = Math.Max(EditorTextBox.SelectionStart, 0);
        if (s > 0)
        {
            s--;
        }

        return s;
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

    private static Regex? TryCreateRegex(string pattern, bool matchCase, bool wholeWord)
    {
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
        startIndex = Math.Clamp(startIndex, 0, Math.Max(0, text.Length - 1));

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

        // Wrap: take the last match in the document.
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

    private static bool IsWholeWordAt(string text, int index, int length)
    {
        var leftOk = index == 0 || !IsWordChar(text[index - 1]);
        var rightIndex = index + length;
        var rightOk = rightIndex >= text.Length || !IsWordChar(text[rightIndex]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private void SelectMatch(int index, int length)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Focus();
        SetSelection(index, index + length);
        EditorTextBox.CaretOffset = index + length;
    }


    private async Task ShowGoToLineAsync()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var currentLine = GetCaretLineNumber(EditorTextBox.Text ?? string.Empty, EditorTextBox.CaretOffset);
        var dialog = new GoToLineDialog(currentLine);
        var raw = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var (line, col) = ParseLineColumn(raw);
        if (line <= 0)
        {
            return;
        }

        GoToLine(EditorTextBox, line, col);
    }

    private static (int line, int? column) ParseLineColumn(string raw)
    {
        raw = raw.Trim();
        var parts = raw.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (0, null);
        }

        if (!int.TryParse(parts[0], out var line) || line <= 0)
        {
            return (0, null);
        }

        if (parts.Length >= 2 && int.TryParse(parts[1], out var col) && col > 0)
        {
            return (line, col);
        }

        return (line, null);
    }

    private static int GetCaretLineNumber(string text, int caretIndex)
    {
        if (caretIndex < 0)
        {
            caretIndex = 0;
        }

        if (caretIndex > text.Length)
        {
            caretIndex = text.Length;
        }

        var line = 1;
        for (var i = 0; i < caretIndex && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static void GoToLine(TextEditor editor, int lineNumber, int? columnNumber)
    {
        if (lineNumber <= 0)
        {
            return;
        }

        var text = editor.Text ?? string.Empty;
        // Our Core normalizes to \n; editor text follows that.
        var targetLine = 1;
        var index = 0;

        while (targetLine < lineNumber && index < text.Length)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0)
            {
                // Requested line past end; clamp to last line start.
                break;
            }

            index = next + 1;
            targetLine++;
        }

        if (columnNumber is not null && columnNumber.Value > 1)
        {
            var col = columnNumber.Value;
            var lineEnd = text.IndexOf('\n', index);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            // Clamp within the line.
            index = Math.Min(index + (col - 1), lineEnd);
        }

        editor.Focus();
        editor.SelectionStart = index;
        editor.SelectionLength = 0;
        editor.CaretOffset = index;
        var targetColumn = columnNumber is > 0 ? columnNumber.Value : 1;
        editor.ScrollTo(lineNumber, targetColumn);
    }

    private void UpdateCaretStatus()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = EditorTextBox.CaretOffset;
        var (line, col) = GetLineColumn(text, caret);
        var selectionLength = Math.Max(0, EditorTextBox.SelectionLength);
        _viewModel.SetCaretPosition(line, col, selectionLength);
        UpdateFindSummary();
    }

    private void UpdateFindSummary()
    {
        if (!_viewModel.IsFindReplaceVisible || EditorTextBox is null)
        {
            _viewModel.FindSummary = string.Empty;
            return;
        }

        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query))
        {
            _viewModel.FindSummary = "Type to search";
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var (rangeStart, rangeEnd) = GetSearchRange(text);
        var total = CountMatchesInRange(
            text,
            query,
            _viewModel.MatchCase,
            _viewModel.WholeWord,
            _viewModel.UseRegex,
            rangeStart,
            rangeEnd);

        if (total <= 0)
        {
            _viewModel.FindSummary = "No matches";
            return;
        }

        var scopeSuffix = _viewModel.InSelection ? " in selection" : string.Empty;
        _viewModel.FindSummary = total == 1
            ? $"1 match{scopeSuffix}"
            : $"{total} matches{scopeSuffix}";
    }

    private static int CountMatchesInRange(
        string fullText,
        string query,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        int rangeStart,
        int rangeEnd)
    {
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

    private static (int line, int column) GetLineColumn(string text, int caretIndex)
    {
        if (caretIndex < 0) caretIndex = 0;
        if (caretIndex > text.Length) caretIndex = text.Length;

        var line = 1;
        var col = 1;

        for (var i = 0; i < caretIndex; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }

        return (line, col);
    }
}

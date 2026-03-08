using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Editing;
using NotepadSharp.App.Dialogs;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private void OnDuplicateClick(object? sender, RoutedEventArgs e)
        => DuplicateLineOrSelection();

    private void OnDeleteLineClick(object? sender, RoutedEventArgs e)
        => DeleteCurrentLine();

    private void OnUppercaseClick(object? sender, RoutedEventArgs e)
        => TransformSelection(static s => s.ToUpperInvariant());

    private void OnLowercaseClick(object? sender, RoutedEventArgs e)
        => TransformSelection(static s => s.ToLowerInvariant());

    private int GetSelectionEnd()
        => EditorTextBox is null ? 0 : EditorTextBox.SelectionStart + EditorTextBox.SelectionLength;

    private void SetSelection(int start, int end)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.SelectionStart = start;
        EditorTextBox.SelectionLength = Math.Max(0, end - start);
    }

    private void OnEditorTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || sender is not TextArea textArea)
        {
            return;
        }

        var editor = ReferenceEquals(textArea, EditorTextBox?.TextArea)
            ? EditorTextBox
            : ReferenceEquals(textArea, SplitEditorTextBox?.TextArea)
                ? SplitEditorTextBox
                : null;
        if (editor is null || !TryGetAutoClosePair(e.Text[0], out var closing))
        {
            return;
        }

        var doc = editor.Document;
        if (doc is null)
        {
            return;
        }

        var offset = editor.CaretOffset;
        if (offset < 0 || offset > doc.TextLength)
        {
            return;
        }

        if ((e.Text[0] == '"' || e.Text[0] == '\'')
            && offset < doc.TextLength
            && doc.GetCharAt(offset) == e.Text[0])
        {
            return;
        }

        doc.Insert(offset, closing.ToString());
        editor.CaretOffset = offset;
    }

    private static bool TryGetAutoClosePair(char open, out char close)
    {
        close = open switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '"' => '"',
            '\'' => '\'',
            _ => '\0',
        };

        return close != '\0';
    }

    private void OnGoToMatchingBracketClick(object? sender, RoutedEventArgs e)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (TryFindMatchingBracket(text, EditorTextBox.CaretOffset, out var matchOffset))
        {
            EditorTextBox.Focus();
            EditorTextBox.CaretOffset = matchOffset;
            EditorTextBox.SelectionStart = matchOffset;
            EditorTextBox.SelectionLength = 0;
        }
    }

    private static bool TryFindMatchingBracket(string text, int caretOffset, out int matchOffset)
    {
        matchOffset = -1;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var pivot = caretOffset > 0 && caretOffset <= text.Length ? caretOffset - 1 : caretOffset;
        if (pivot < 0 || pivot >= text.Length)
        {
            return false;
        }

        var c = text[pivot];
        if (c is '(' or '[' or '{')
        {
            var close = c == '(' ? ')' : c == '[' ? ']' : '}';
            var depth = 0;
            for (var i = pivot; i < text.Length; i++)
            {
                if (text[i] == c) depth++;
                else if (text[i] == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        matchOffset = i;
                        return true;
                    }
                }
            }

            return false;
        }

        if (c is ')' or ']' or '}')
        {
            var open = c == ')' ? '(' : c == ']' ? '[' : '{';
            var depth = 0;
            for (var i = pivot; i >= 0; i--)
            {
                if (text[i] == c) depth++;
                else if (text[i] == open)
                {
                    depth--;
                    if (depth == 0)
                    {
                        matchOffset = i;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void TransformSelection(Func<string, string> transform)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var end = GetSelectionEnd();
        if (end <= EditorTextBox.SelectionStart)
        {
            return;
        }

        var start = EditorTextBox.SelectionStart;
        var text = EditorTextBox.Text ?? string.Empty;
        var selected = EditorTextBox.SelectedText ?? string.Empty;

        var replacement = transform(selected);
        var newText = text.Substring(0, start) + replacement + text.Substring(end);
        EditorTextBox.Text = newText;
        SetSelection(start, start + replacement.Length);
    }

    private void DuplicateLineOrSelection()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var selStart = EditorTextBox.SelectionStart;
        var selEnd = GetSelectionEnd();

        if (selEnd > selStart)
        {
            var selected = EditorTextBox.SelectedText ?? string.Empty;
            var insertAt = selEnd;
            var newText = text.Substring(0, insertAt) + selected + text.Substring(insertAt);
            EditorTextBox.Text = newText;
            SelectMatch(insertAt, selected.Length);
            return;
        }

        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }
        else
        {
            lineEnd += 1; // include newline
        }

        var line = text.Substring(lineStart, lineEnd - lineStart);
        var insertPos = lineEnd;
        var text2 = text.Substring(0, insertPos) + line + text.Substring(insertPos);
        EditorTextBox.Text = text2;
        EditorTextBox.CaretOffset = insertPos + line.Length;
    }

    private void DeleteCurrentLine()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }
        else
        {
            lineEnd += 1;
        }

        var newText = text.Substring(0, lineStart) + text.Substring(lineEnd);
        EditorTextBox.Text = newText;
        EditorTextBox.CaretOffset = Math.Min(lineStart, newText.Length);
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
        => ZoomBy(+1);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
        => ZoomBy(-1);

    private void OnZoomResetClick(object? sender, RoutedEventArgs e)
        => ZoomReset();

    private void ZoomBy(int delta)
    {
        SetEditorFontSize(_viewModel.EditorFontSize + delta);
    }

    private void ZoomReset()
        => SetEditorFontSize(DefaultEditorFontSize);

    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var ctrlOrCmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!ctrlOrCmd || Math.Abs(e.Delta.Y) <= double.Epsilon)
        {
            return;
        }

        ZoomBy(e.Delta.Y > 0 ? +1 : -1);
        e.Handled = true;
    }

    private void OnEditorTouchPadMagnify(object? sender, EventArgs e)
    {
        if (!TryGetZoomDelta(e, out var delta))
        {
            return;
        }

        SetEditorFontSize(_viewModel.EditorFontSize + delta);
        MarkHandled(e);
    }

    private void OnEditorPinch(object? sender, EventArgs e)
    {
        if (!TryGetZoomDelta(e, out var delta))
        {
            return;
        }

        SetEditorFontSize(_viewModel.EditorFontSize + delta);
        MarkHandled(e);
    }

    private void SetEditorFontSize(double size, bool persist = true)
    {
        _viewModel.EditorFontSize = Math.Clamp(Math.Round(size), MinEditorFontSize, MaxEditorFontSize);
        UpdateEditorTypographySelectors();
        UpdateSettingsControls();
        if (persist)
        {
            PersistState();
        }
    }

    private static bool TryGetZoomDelta(EventArgs e, out double delta)
    {
        delta = 0;

        if (TryGetDoubleProperty(e, "ScaleDelta", out var scaleDelta))
        {
            delta = NormalizeGestureDelta(scaleDelta);
            return Math.Abs(delta) > double.Epsilon;
        }

        if (TryGetDoubleProperty(e, "Scale", out var scale))
        {
            delta = NormalizeGestureDelta(scale - 1d);
            return Math.Abs(delta) > double.Epsilon;
        }

        if (TryGetDoubleProperty(e, "Delta", out var scalarDelta))
        {
            delta = NormalizeGestureDelta(scalarDelta);
            return Math.Abs(delta) > double.Epsilon;
        }

        if (TryGetVectorDelta(e, "Delta", out var vectorDelta))
        {
            delta = NormalizeGestureDelta(vectorDelta);
            return Math.Abs(delta) > double.Epsilon;
        }

        return false;
    }

    private static bool TryGetDoubleProperty(object target, string propertyName, out double value)
    {
        value = 0;
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
        {
            return false;
        }

        var raw = property.GetValue(target);
        switch (raw)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case int i:
                value = i;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetVectorDelta(object target, string propertyName, out double value)
    {
        value = 0;
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
        {
            return false;
        }

        var raw = property.GetValue(target);
        if (raw is null)
        {
            return false;
        }

        if (raw is Vector vector)
        {
            value = Math.Abs(vector.Y) >= Math.Abs(vector.X) ? vector.Y : vector.X;
            return true;
        }

        var yProperty = raw.GetType().GetProperty("Y", BindingFlags.Instance | BindingFlags.Public);
        if (yProperty?.GetValue(raw) is IConvertible y)
        {
            value = y.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        var xProperty = raw.GetType().GetProperty("X", BindingFlags.Instance | BindingFlags.Public);
        if (xProperty?.GetValue(raw) is IConvertible x)
        {
            value = x.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static double NormalizeGestureDelta(double rawDelta)
    {
        if (Math.Abs(rawDelta) < 0.001)
        {
            return 0;
        }

        return rawDelta > 0 ? +1 : -1;
    }

    private static void MarkHandled(EventArgs e)
    {
        if (e is RoutedEventArgs routed)
        {
            routed.Handled = true;
            return;
        }

        var handledProperty = e.GetType().GetProperty("Handled", BindingFlags.Instance | BindingFlags.Public);
        if (handledProperty is not null && handledProperty.PropertyType == typeof(bool) && handledProperty.CanWrite)
        {
            handledProperty.SetValue(e, true);
        }
    }

    private async void OnKeyboardShortcutsClick(object? sender, RoutedEventArgs e)
        => await ShowKeyboardShortcutsAsync();

    private void OnCutClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Cut();

    private void OnCopyClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Copy();

    private void OnPasteClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Paste();

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.SelectAll();

    private async Task ShowKeyboardShortcutsAsync()
    {
        var dialog = new KeyboardShortcutsDialog();
        await dialog.ShowDialog(this);
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e)
        => Undo();

    private void OnRedoClick(object? sender, RoutedEventArgs e)
        => Redo();

    private void Undo()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Undo();
        UpdateCaretStatus();
    }

    private void Redo()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Redo();
        UpdateCaretStatus();
    }
}

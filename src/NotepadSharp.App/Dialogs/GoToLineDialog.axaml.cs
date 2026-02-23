using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public partial class GoToLineDialog : Window
{
    public GoToLineDialog()
    {
        InitializeComponent();
        Opened += (_, __) =>
        {
            LineNumberTextBox.Focus();
            LineNumberTextBox.SelectAll();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                TryCloseOk();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    public GoToLineDialog(int? initialLineNumber)
        : this()
    {
        if (initialLineNumber is not null && initialLineNumber > 0)
        {
            LineNumberTextBox.Text = initialLineNumber.Value.ToString();
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e)
        => TryCloseOk();

    private void OnCancel(object? sender, RoutedEventArgs e)
        => Close(null);

    private void TryCloseOk()
    {
        var raw = LineNumberTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            LineNumberTextBox.Focus();
            LineNumberTextBox.SelectAll();
            return;
        }

        // Accept: "12" or "12:34".
        var parts = raw.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 1 or 2 && int.TryParse(parts[0], out var line) && line > 0)
        {
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1], out var col) || col <= 0)
                {
                    LineNumberTextBox.Focus();
                    LineNumberTextBox.SelectAll();
                    return;
                }

                Close($"{line}:{col}");
                return;
            }

            Close(line.ToString());
            return;
        }

        LineNumberTextBox.Focus();
        LineNumberTextBox.SelectAll();
    }
}

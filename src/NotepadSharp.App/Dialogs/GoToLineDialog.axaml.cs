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
        if (int.TryParse(LineNumberTextBox.Text?.Trim(), out var value) && value > 0)
        {
            Close(value);
        }
        else
        {
            // Keep dialog open; just refocus for correction.
            LineNumberTextBox.Focus();
            LineNumberTextBox.SelectAll();
        }
    }
}

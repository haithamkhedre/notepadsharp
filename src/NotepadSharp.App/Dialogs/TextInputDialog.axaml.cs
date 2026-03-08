using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public partial class TextInputDialog : Window
{
    public TextInputDialog()
    {
        InitializeComponent();
        Opened += (_, __) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnOk(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                OnCancel(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };
    }

    public TextInputDialog(string title, string prompt, string? initialValue = null)
        : this()
    {
        Title = title;
        PromptTextBlock.Text = prompt;
        ValueTextBox.Text = initialValue ?? string.Empty;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var value = ValueTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
            return;
        }

        Close(value);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
        => Close(null);
}


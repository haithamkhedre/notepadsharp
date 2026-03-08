using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Close(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close(false);
                e.Handled = true;
            }
        };
    }

    public ConfirmDialog(string title, string message)
        : this()
    {
        Title = title;
        MessageTextBlock.Text = message;
    }

    private void OnYes(object? sender, RoutedEventArgs e)
        => Close(true);

    private void OnNo(object? sender, RoutedEventArgs e)
        => Close(false);
}


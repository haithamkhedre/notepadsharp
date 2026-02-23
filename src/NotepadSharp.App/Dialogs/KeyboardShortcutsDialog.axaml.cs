using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public partial class KeyboardShortcutsDialog : Window
{
    public KeyboardShortcutsDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
        => Close();
}

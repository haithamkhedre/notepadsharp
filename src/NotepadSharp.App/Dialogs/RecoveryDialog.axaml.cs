using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public partial class RecoveryDialog : Window
{
    public RecoveryDialog()
    {
        InitializeComponent();
    }

    private void OnDiscard(object? sender, RoutedEventArgs e)
        => Close(false);

    private void OnRestore(object? sender, RoutedEventArgs e)
        => Close(true);
}

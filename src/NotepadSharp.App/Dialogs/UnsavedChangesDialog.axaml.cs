using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public enum UnsavedChangesChoice
{
    Cancel,
    Save,
    DontSave,
}

public partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    public UnsavedChangesDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
        => Close(UnsavedChangesChoice.Save);

    private void OnDontSave(object? sender, RoutedEventArgs e)
        => Close(UnsavedChangesChoice.DontSave);

    private void OnCancel(object? sender, RoutedEventArgs e)
        => Close(UnsavedChangesChoice.Cancel);
}

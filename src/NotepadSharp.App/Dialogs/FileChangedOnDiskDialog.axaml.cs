using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public enum FileChangedOnDiskChoice
{
    Cancel = 0,
    Reload = 1,
    Overwrite = 2,
}

public partial class FileChangedOnDiskDialog : Window
{
    public FileChangedOnDiskDialog()
    {
        InitializeComponent();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
        => Close(FileChangedOnDiskChoice.Cancel);

    private void OnReload(object? sender, RoutedEventArgs e)
        => Close(FileChangedOnDiskChoice.Reload);

    private void OnOverwrite(object? sender, RoutedEventArgs e)
        => Close(FileChangedOnDiskChoice.Overwrite);
}

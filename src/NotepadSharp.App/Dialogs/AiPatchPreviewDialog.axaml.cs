using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public partial class AiPatchPreviewDialog : Window
{
    public AiPatchPreviewDialog()
    {
        InitializeComponent();
        KeyDown += OnDialogKeyDown;
    }

    public AiPatchPreviewDialog(string title, string summary, string currentText, string proposedText)
        : this()
    {
        Title = title;
        SummaryTextBlock.Text = summary;
        CurrentTextBox.Text = currentText;
        ProposedTextBox.Text = proposedText;
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrlOrCmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (ctrlOrCmd && e.Key == Key.Enter)
        {
            Close(true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
        => Close(true);

    private void OnRejectClick(object? sender, RoutedEventArgs e)
        => Close(false);
}

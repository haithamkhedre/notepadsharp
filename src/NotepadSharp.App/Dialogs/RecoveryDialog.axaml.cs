using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NotepadSharp.App.Dialogs;

public partial class RecoveryDialog : Window
{
    public RecoveryDialog()
        : this(Array.Empty<RecoveryCandidate>())
    {
    }

    public RecoveryDialog(IReadOnlyList<RecoveryCandidate> candidates)
    {
        InitializeComponent();
        Candidates = candidates ?? Array.Empty<RecoveryCandidate>();
        DataContext = this;
    }

    public IReadOnlyList<RecoveryCandidate> Candidates { get; }

    public string SummaryText
    {
        get
        {
            if (Candidates.Count == 1)
            {
                return "1 recoverable tab was found from the previous run.";
            }

            return $"{Candidates.Count} recoverable tabs were found from the previous run.";
        }
    }

    public IReadOnlyList<string> PreviewLines
        => Candidates.Select(ToPreviewLine).ToList();

    private static string ToPreviewLine(RecoveryCandidate candidate)
    {
        var name = string.IsNullOrWhiteSpace(candidate.FilePath)
            ? "Untitled"
            : Path.GetFileName(candidate.FilePath);
        var pathOrDraft = string.IsNullOrWhiteSpace(candidate.FilePath)
            ? "unsaved draft"
            : candidate.FilePath;
        var when = candidate.TimestampUtc.ToLocalTime().ToString("g");
        return $"{name} ({pathOrDraft}) | {candidate.LineCount} lines | {when}";
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
        => Close(RecoveryChoice.Cancel);

    private void OnDiscard(object? sender, RoutedEventArgs e)
        => Close(RecoveryChoice.Discard);

    private void OnRestore(object? sender, RoutedEventArgs e)
        => Close(RecoveryChoice.Restore);
}

public enum RecoveryChoice
{
    Cancel,
    Discard,
    Restore,
}

public sealed record RecoveryCandidate(
    string? FilePath,
    DateTimeOffset TimestampUtc,
    int LineCount);

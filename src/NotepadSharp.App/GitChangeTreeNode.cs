using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Material.Icons;

namespace NotepadSharp.App;

public sealed class GitChangeTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }
    public MaterialIconKind IconKind { get; init; } = MaterialIconKind.FileDocumentOutline;
    public string? Status { get; init; }
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public List<GitChangeTreeNode> Children { get; } = new();

    public IBrush StatusBrush => (Status ?? string.Empty) switch
    {
        var s when s.Contains('D', StringComparison.Ordinal) => new SolidColorBrush(Color.Parse("#E06C75")),
        var s when s.Contains('A', StringComparison.Ordinal) || s.Contains('?', StringComparison.Ordinal) => new SolidColorBrush(Color.Parse("#98C379")),
        var s when s.Contains('M', StringComparison.Ordinal) || s.Contains('R', StringComparison.Ordinal) || s.Contains('U', StringComparison.Ordinal)
            => new SolidColorBrush(Color.Parse("#E5C07B")),
        _ => new SolidColorBrush(Color.Parse("#AAB8C5")),
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

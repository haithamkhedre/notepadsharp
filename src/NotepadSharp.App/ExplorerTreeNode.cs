using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Material.Icons;

namespace NotepadSharp.App;

public sealed class ExplorerTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isChildrenLoaded;
    private bool _isLoadingChildren;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public MaterialIconKind IconKind { get; init; } = MaterialIconKind.FileDocumentOutline;
    public string? GitBadge { get; init; }
    public bool IsPlaceholder { get; init; }
    public string DisplayName => Name;
    public bool HasGitBadge => !string.IsNullOrWhiteSpace(GitBadge);
    public ObservableCollection<ExplorerTreeNode> Children { get; } = new();

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

    public bool IsChildrenLoaded
    {
        get => _isChildrenLoaded;
        set
        {
            if (_isChildrenLoaded == value)
            {
                return;
            }

            _isChildrenLoaded = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoadingChildren
    {
        get => _isLoadingChildren;
        set
        {
            if (_isLoadingChildren == value)
            {
                return;
            }

            _isLoadingChildren = value;
            OnPropertyChanged();
        }
    }

    public static ExplorerTreeNode CreatePlaceholder(string parentPath)
        => new()
        {
            Name = "(loading...)",
            FullPath = parentPath,
            IsDirectory = false,
            IsPlaceholder = true,
            IconKind = MaterialIconKind.DotsHorizontal,
        };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

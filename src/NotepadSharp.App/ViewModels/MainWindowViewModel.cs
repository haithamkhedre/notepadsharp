using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System;
using NotepadSharp.Core;

namespace NotepadSharp.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private TextDocument? _selectedDocument;
    private bool _isFindReplaceVisible;
    private bool _isReplaceVisible;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private bool _matchCase;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TextDocument> Documents { get; } = new();

    public ObservableCollection<string> RecentFiles { get; } = new();

    public TextDocument? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (ReferenceEquals(_selectedDocument, value))
            {
                return;
            }

            if (_selectedDocument is not null)
            {
                _selectedDocument.PropertyChanged -= SelectedDocumentOnPropertyChanged;
            }

            _selectedDocument = value;

            if (_selectedDocument is not null)
            {
                _selectedDocument.PropertyChanged += SelectedDocumentOnPropertyChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle
    {
        get
        {
            var docName = SelectedDocument?.DisplayName;
            return string.IsNullOrWhiteSpace(docName)
                ? "NotepadSharp"
                : $"{docName} - NotepadSharp";
        }
    }

    public MainWindowViewModel()
    {
        var first = TextDocument.CreateNew();
        Documents.Add(first);
        SelectedDocument = first;

        RecentFiles.CollectionChanged += RecentFilesOnCollectionChanged;
    }

    public bool IsFindReplaceVisible
    {
        get => _isFindReplaceVisible;
        set
        {
            if (_isFindReplaceVisible == value)
            {
                return;
            }

            _isFindReplaceVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsReplaceVisible
    {
        get => _isReplaceVisible;
        set
        {
            if (_isReplaceVisible == value)
            {
                return;
            }

            _isReplaceVisible = value;
            OnPropertyChanged();
        }
    }

    public string FindText
    {
        get => _findText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_findText, value, StringComparison.Ordinal))
            {
                return;
            }

            _findText = value;
            OnPropertyChanged();
        }
    }

    public string ReplaceText
    {
        get => _replaceText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_replaceText, value, StringComparison.Ordinal))
            {
                return;
            }

            _replaceText = value;
            OnPropertyChanged();
        }
    }

    public bool MatchCase
    {
        get => _matchCase;
        set
        {
            if (_matchCase == value)
            {
                return;
            }

            _matchCase = value;
            OnPropertyChanged();
        }
    }

    public void NewDocument()
    {
        var doc = TextDocument.CreateNew();
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    private void SelectedDocumentOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TextDocument.DisplayName) or nameof(TextDocument.IsDirty) or nameof(TextDocument.FilePath))
        {
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void AddRecentFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var normalized = NormalizePath(filePath);
        if (normalized is null)
        {
            return;
        }

        var existing = RecentFiles.FirstOrDefault(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RecentFiles.Remove(existing);
        }

        RecentFiles.Insert(0, normalized);

        const int max = 10;
        while (RecentFiles.Count > max)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }
    }

    public void SetRecentFiles(System.Collections.Generic.IEnumerable<string> paths)
    {
        RecentFiles.Clear();
        foreach (var p in paths.Select(NormalizePath).Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            RecentFiles.Add(p!);
        }
    }

    public string[] GetSessionFilePaths()
        => Documents
            .Select(d => d.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => NormalizePath(p!)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private void RecentFilesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(RecentFiles));
}

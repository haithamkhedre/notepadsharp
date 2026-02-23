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
    private bool _wholeWord;
    private bool _useRegex;
    private bool _wrapAround = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TextDocument> Documents { get; } = new();

    public ObservableCollection<string> RecentFiles { get; } = new();

    private int _caretLine = 1;
    private int _caretColumn = 1;
    private string _statusCaret = "Ln 1, Col 1";
    private string _statusFormat = string.Empty;

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
        UpdateStatusFormat();
    }

    public string StatusCaret
    {
        get => _statusCaret;
        private set
        {
            if (string.Equals(_statusCaret, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusCaret = value;
            OnPropertyChanged();
        }
    }

    public string StatusFormat
    {
        get => _statusFormat;
        private set
        {
            if (string.Equals(_statusFormat, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusFormat = value;
            OnPropertyChanged();
        }
    }

    public void SetCaretPosition(int line, int column)
    {
        if (line <= 0) line = 1;
        if (column <= 0) column = 1;

        _caretLine = line;
        _caretColumn = column;
        StatusCaret = $"Ln {_caretLine}, Col {_caretColumn}";
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

    public bool WholeWord
    {
        get => _wholeWord;
        set
        {
            if (_wholeWord == value)
            {
                return;
            }

            _wholeWord = value;
            OnPropertyChanged();
        }
    }

    public bool UseRegex
    {
        get => _useRegex;
        set
        {
            if (_useRegex == value)
            {
                return;
            }

            _useRegex = value;
            OnPropertyChanged();
        }
    }

    public bool WrapAround
    {
        get => _wrapAround;
        set
        {
            if (_wrapAround == value)
            {
                return;
            }

            _wrapAround = value;
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

        if (e.PropertyName is nameof(TextDocument.Encoding) or nameof(TextDocument.HasBom) or nameof(TextDocument.PreferredLineEnding))
        {
            UpdateStatusFormat();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void UpdateStatusFormat()
    {
        var doc = SelectedDocument;
        if (doc is null)
        {
            StatusFormat = string.Empty;
            return;
        }

        var encodingName = GetEncodingLabel(doc);
        var eol = doc.PreferredLineEnding switch
        {
            LineEnding.Lf => "LF",
            LineEnding.CrLf => "CRLF",
            LineEnding.Cr => "CR",
            _ => "LF",
        };

        StatusFormat = $"{encodingName} | {eol}";
    }

    private static string GetEncodingLabel(TextDocument doc)
    {
        var enc = doc.Encoding;

        // Friendly labels for common encodings.
        if (ReferenceEquals(enc, System.Text.Encoding.UTF8) || enc.WebName.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            return doc.HasBom ? "UTF-8 BOM" : "UTF-8";
        }

        if (ReferenceEquals(enc, System.Text.Encoding.Unicode) || enc.WebName.Equals("utf-16", StringComparison.OrdinalIgnoreCase) || enc.WebName.Equals("utf-16le", StringComparison.OrdinalIgnoreCase))
        {
            return "UTF-16 LE";
        }

        if (ReferenceEquals(enc, System.Text.Encoding.BigEndianUnicode) || enc.WebName.Equals("utf-16be", StringComparison.OrdinalIgnoreCase))
        {
            return "UTF-16 BE";
        }

        return enc.WebName;
    }

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

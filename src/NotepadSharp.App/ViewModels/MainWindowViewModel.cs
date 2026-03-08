using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System;
using NotepadSharp.Core;
using Avalonia.Media;

namespace NotepadSharp.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const string StarterCode = """
// NotepadSharp starter: syntax, guide, search, and replace demo.
// Try: Format -> Column Guide -> 80/100/120 and Format -> Language.
// Try: Find words like 'apple', 'BinarySearch', or regex: [A-Z][a-z]+Buzz.
// Column ruler hint (100 chars target): 0000000000111111111122222222223333333333444444444455555555556666666666777777777788888888889999999999

using System;
using System.Collections.Generic;
using System.Linq;

public static class Program
{
    public static void Main()
    {
        var veryLongLine = "This line intentionally crosses eighty and one hundred characters so the column guide can be tested quickly.";

        // FizzBuzz: the classic interview warmup.
        for (var i = 1; i <= 100; i++)
        {
            var text = (i % 3 == 0 ? "Fizz" : "") + (i % 5 == 0 ? "Buzz" : "");
            Console.WriteLine(text.Length == 0 ? i.ToString() : text);
        }

        // Search demo terms (mixed case):
        // apple APPLE Apple banana BANANA banana
        var tags = new List<string> { "apple", "Apple", "APPLE", "banana", "BANANA" };
        var grouped = tags.GroupBy(t => t.ToLowerInvariant());
        foreach (var g in grouped)
        {
            Console.WriteLine($"{g.Key} -> {g.Count()}");
        }
    }
}

// Binary search (iterative): O(log n)
public static int BinarySearch(int[] a, int target)
{
    var lo = 0;
    var hi = a.Length - 1;
    while (lo <= hi)
    {
        var mid = lo + ((hi - lo) / 2);
        if (a[mid] == target) return mid;
        if (a[mid] < target) lo = mid + 1;
        else hi = mid - 1;
    }

    return -1;
}

// Prime checker: quick extra snippet for highlighting.
public static bool IsPrime(int n)
{
    if (n < 2) return false;
    if (n == 2) return true;
    if (n % 2 == 0) return false;

    var limit = (int)Math.Sqrt(n);
    for (var d = 3; d <= limit; d += 2)
    {
        if (n % d == 0) return false;
    }

    return true;
}
""";

    private TextDocument? _selectedDocument;
    private bool _isFindReplaceVisible;
    private bool _isReplaceVisible;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private bool _matchCase;
    private bool _wholeWord;
    private bool _useRegex;
    private bool _wrapAround = true;
    private bool _inSelection;
    private string _findSummary = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TextDocument> Documents { get; } = new();

    public ObservableCollection<string> RecentFiles { get; } = new();

    private int _caretLine = 1;
    private int _caretColumn = 1;
    private string _statusCaret = "Ln 1, Col 1";
    private string _statusFormat = string.Empty;
    private string _statusLanguage = "Plain Text";
    private string _statusBracket = "No Match";
    private double _editorFontSize = 16;
    private IBrush _editorForeground = new SolidColorBrush(Color.Parse("#EAF2F8"));

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
            UpdateStatusFormat();
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
        first.Text = StarterCode;
        first.MarkSaved();
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

    public double EditorFontSize
    {
        get => _editorFontSize;
        set
        {
            if (Math.Abs(_editorFontSize - value) < 0.01)
            {
                return;
            }

            _editorFontSize = value;
            OnPropertyChanged();
        }
    }

    public string StatusLanguage
    {
        get => _statusLanguage;
        set
        {
            value ??= "Plain Text";
            if (string.Equals(_statusLanguage, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusLanguage = value;
            OnPropertyChanged();
        }
    }

    public string StatusBracket
    {
        get => _statusBracket;
        set
        {
            value ??= "No Match";
            if (string.Equals(_statusBracket, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusBracket = value;
            OnPropertyChanged();
        }
    }

    public IBrush EditorForeground
    {
        get => _editorForeground;
        set
        {
            if (ReferenceEquals(_editorForeground, value))
            {
                return;
            }

            _editorForeground = value;
            OnPropertyChanged();
        }
    }

    public void SetCaretPosition(int line, int column, int selectionLength)
    {
        if (line <= 0) line = 1;
        if (column <= 0) column = 1;

        if (selectionLength < 0)
        {
            selectionLength = 0;
        }

        _caretLine = line;
        _caretColumn = column;
        StatusCaret = selectionLength > 0
            ? $"Ln {_caretLine}, Col {_caretColumn} | Sel {selectionLength}"
            : $"Ln {_caretLine}, Col {_caretColumn}";
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

    public bool InSelection
    {
        get => _inSelection;
        set
        {
            if (_inSelection == value)
            {
                return;
            }

            _inSelection = value;
            OnPropertyChanged();
        }
    }

    public string FindSummary
    {
        get => _findSummary;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_findSummary, value, StringComparison.Ordinal))
            {
                return;
            }

            _findSummary = value;
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

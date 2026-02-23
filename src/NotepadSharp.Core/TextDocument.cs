using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace NotepadSharp.Core;

public sealed class TextDocument : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string? _filePath;
    private Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private bool _hasBom;
    private bool _isDirty;
    private LineEnding _preferredLineEnding = LineEnding.Lf;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text
    {
        get => _text;
        set
        {
            if (string.Equals(_text, value, StringComparison.Ordinal))
            {
                return;
            }

            _text = value;
            IsDirty = true;
            OnPropertyChanged();
        }
    }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (string.Equals(_filePath, value, StringComparison.Ordinal))
            {
                return;
            }

            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public Encoding Encoding
    {
        get => _encoding;
        set
        {
            if (ReferenceEquals(_encoding, value))
            {
                return;
            }

            _encoding = value ?? throw new ArgumentNullException(nameof(value));
            OnPropertyChanged();
        }
    }

    public bool HasBom
    {
        get => _hasBom;
        set
        {
            if (_hasBom == value)
            {
                return;
            }

            _hasBom = value;
            OnPropertyChanged();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
            {
                return;
            }

            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FilePath)
                ? "Untitled"
                : Path.GetFileName(FilePath);

            return IsDirty ? $"{name}*" : name;
        }
    }

    public static TextDocument CreateNew()
        => new();

    public LineEnding PreferredLineEnding
    {
        get => _preferredLineEnding;
        set
        {
            if (_preferredLineEnding == value)
            {
                return;
            }

            _preferredLineEnding = value;
            OnPropertyChanged();
        }
    }

    public void MarkSaved()
    {
        IsDirty = false;
    }

    internal void LoadFrom(string text, Encoding encoding, bool hasBom, string? filePath, LineEnding preferredLineEnding)
    {
        _text = text;
        _encoding = encoding;
        _hasBom = hasBom;
        _filePath = filePath;
        _preferredLineEnding = preferredLineEnding;
        IsDirty = false;

        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Encoding));
        OnPropertyChanged(nameof(HasBom));
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(PreferredLineEnding));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

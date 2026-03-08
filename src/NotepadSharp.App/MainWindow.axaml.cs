using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Editing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using NotepadSharp.App.Dialogs;
using NotepadSharp.App.Services;
using NotepadSharp.App.ViewModels;
using NotepadSharp.Core;
using YamlDotNet.Serialization;

namespace NotepadSharp.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly TextDocumentFileService _fileService = new();
    private readonly AppStateStore _stateStore = new();
    private readonly RecoveryStore _recoveryStore = new();
    private readonly RecoveryManager _recoveryManager;
    private AppState _state;
    private bool _allowClose;
    private ScrollViewer? _editorScrollViewer;
    private ScrollViewer? _splitEditorScrollViewer;
    private readonly TranslateTransform _lineNumbersTransform = new(0, 0);
    private const int DefaultColumnGuide = 100;
    private static readonly string[] LanguageModes =
    {
        "Auto",
        "Plain Text",
        "C#",
        "JSON",
        "XML",
        "YAML",
        "Markdown",
        "JavaScript",
        "TypeScript",
        "Python",
        "SQL",
        "HTML",
        "CSS",
    };
    private static readonly string[] ThemeModes =
    {
        "Dark+",
        "One Dark",
        "Monokai",
        "Light",
    };
    private readonly Stack<ClosedTabSnapshot> _closedTabs = new();
    private string _languageMode = "Auto";
    private string _themeMode = "Dark+";
    private bool _isColumnGuideEnabled = true;
    private int _columnGuideColumn = DefaultColumnGuide;
    private bool _isMiniMapEnabled = true;
    private bool _isSplitViewEnabled;
    private bool _isFoldingEnabled = true;
    private bool _isSyncingEditorText;
    private bool _isSyncingSplitEditorText;
    private readonly HashSet<string> _themedHighlightDefinitions = new(StringComparer.Ordinal);
    private readonly List<int> _miniMapLineMap = new();
    private readonly Dictionary<string, Action> _commandPaletteActions = new(StringComparer.Ordinal);
    private FoldingManager? _foldingManager;
    private TextDocument? _splitDocument;
    private bool _isUpdatingLanguageModeSelector;
    private bool _isUpdatingThemeModeSelector;

    private sealed record ClosedTabSnapshot(
        string? FilePath,
        string Text,
        string EncodingWebName,
        bool HasBom,
        LineEnding PreferredLineEnding,
        bool WordWrap,
        bool WasDirty,
        DateTimeOffset? FileLastWriteTimeUtc);

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _recoveryManager = new RecoveryManager(_recoveryStore);
        InitializeCommandPaletteActions();

        if (EditorTextBox is not null)
        {
            ConfigureEditor(EditorTextBox);
            EditorTextBox.TextChanged += OnPrimaryEditorTextChanged;
            EditorTextBox.TextArea.Caret.PositionChanged += (_, __) => UpdateCaretStatus();
            _foldingManager = FoldingManager.Install(EditorTextBox.TextArea);
        }

        if (SplitEditorTextBox is not null)
        {
            ConfigureEditor(SplitEditorTextBox);
            SplitEditorTextBox.TextChanged += OnSplitEditorTextChanged;
        }

        if (LineNumbersTextBlock is not null)
        {
            LineNumbersTextBlock.RenderTransform = _lineNumbersTransform;
        }

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedDocument))
            {
                if (_splitDocument is null)
                {
                    _splitDocument = _viewModel.SelectedDocument;
                }

                SyncEditorFromDocument();
                SyncSplitEditorFromDocument();
                ApplyWordWrap();
                UpdateColumnGuide();
                UpdateLineNumbers();
                UpdateCaretStatus();
                UpdateFindSummary();
                ApplyLanguageStyling();
                RefreshSplitEditorTitle();
                UpdateMiniMap();
                UpdateFolding();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.EditorFontSize))
            {
                UpdateColumnGuide();
            }
            else if (e.PropertyName is nameof(MainWindowViewModel.FindText)
                or nameof(MainWindowViewModel.MatchCase)
                or nameof(MainWindowViewModel.WholeWord)
                or nameof(MainWindowViewModel.UseRegex)
                or nameof(MainWindowViewModel.InSelection))
            {
                UpdateFindSummary();
            }
        };

        _state = _stateStore.Load();
        _viewModel.SetRecentFiles(_state.RecentFiles);

        _isColumnGuideEnabled = _state.ColumnGuideEnabled;
        if (_state.ColumnGuideColumn > 0)
        {
            _columnGuideColumn = _state.ColumnGuideColumn;
        }

        _isMiniMapEnabled = _state.ShowMiniMap;
        _isSplitViewEnabled = _state.SplitViewEnabled;
        _isFoldingEnabled = _state.FoldingEnabled;
        _themeMode = NormalizeThemeMode(_state.Theme);
        _languageMode = NormalizeLanguageMode(_state.LanguageMode);
        UpdateThemeModeSelector();
        UpdateLanguageModeSelector();

        RefreshOpenRecentMenu();
        _viewModel.RecentFiles.CollectionChanged += (_, __) => RefreshOpenRecentMenu();

        Opened += async (_, __) =>
        {
            await MaybeRecoverAsync();
            await ReopenLastSessionAsync();
            _recoveryManager.Start(() => _viewModel.Documents);

            AttachEditorScrollSync();
            AttachSplitEditorScrollSync();
            _splitDocument ??= _viewModel.SelectedDocument;
            ApplyThemeMode(_themeMode, persist: false);
            ApplySplitView();
            ApplyMiniMapVisibility();
            ApplyWordWrap();
            UpdateColumnGuideMenuChecks();
            UpdateThemeMenuChecks();
            UpdateLineNumbers();
            UpdateColumnGuide();
            SyncEditorFromDocument();
            SyncSplitEditorFromDocument();
            ApplyLanguageStyling();
            RefreshSplitEditorTitle();
            UpdateMiniMap();
            UpdateFolding();
        };

        Closing += OnWindowClosing;
    }

    private void ConfigureEditor(TextEditor editor)
    {
        var visibleForeground = new SolidColorBrush(Color.Parse("#D4D4D4"));
        editor.Foreground = visibleForeground;
        editor.TextArea.Foreground = visibleForeground;
        editor.TextArea.SelectionForeground = visibleForeground;
        editor.TextArea.CaretBrush = new SolidColorBrush(Color.Parse("#AEAFAD"));

        editor.Options.AcceptsTab = true;
        editor.Options.EnableHyperlinks = false;
        editor.Options.HighlightCurrentLine = true;
        editor.Options.AllowScrollBelowDocument = true;
        editor.Options.EnableRectangularSelection = true;
        editor.TextArea.TextEntered += OnEditorTextEntered;
    }

    private void OnPrimaryEditorTextChanged(object? sender, EventArgs e)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        if (!_isSyncingEditorText && _viewModel.SelectedDocument is not null)
        {
            var editorText = EditorTextBox.Text ?? string.Empty;
            if (!string.Equals(_viewModel.SelectedDocument.Text, editorText, StringComparison.Ordinal))
            {
                _viewModel.SelectedDocument.Text = editorText;
            }
        }

        if (ReferenceEquals(_splitDocument, _viewModel.SelectedDocument))
        {
            SyncSplitEditorFromDocument();
        }

        UpdateCaretStatus();
        UpdateLineNumbers();
        UpdateFindSummary();
        ApplyLanguageStyling();
        UpdateMiniMap();
        UpdateFolding();
    }

    private void OnSplitEditorTextChanged(object? sender, EventArgs e)
    {
        if (SplitEditorTextBox is null || _splitDocument is null)
        {
            return;
        }

        if (!_isSyncingSplitEditorText)
        {
            var text = SplitEditorTextBox.Text ?? string.Empty;
            if (!string.Equals(_splitDocument.Text, text, StringComparison.Ordinal))
            {
                _splitDocument.Text = text;
            }
        }

        if (ReferenceEquals(_splitDocument, _viewModel.SelectedDocument))
        {
            SyncEditorFromDocument();
            UpdateLineNumbers();
        }

        UpdateMiniMap();
        UpdateFolding();
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        var names = e.Data.GetFileNames();
        if (names is null || !names.Any())
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        var names = e.Data.GetFileNames();
        if (names is null)
        {
            return;
        }

        foreach (var p in names)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
                {
                    continue;
                }

                await OpenFilePathAsync(p);
            }
            catch
            {
                // Ignore.
            }
        }
    }

    private void OnToggleWordWrapClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.WordWrap = !_viewModel.SelectedDocument.WordWrap;
        ApplyWordWrap();
    }

    private void ApplyWordWrap()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var wrap = _viewModel.SelectedDocument?.WordWrap ?? false;
        EditorTextBox.WordWrap = wrap;

        if (SplitEditorTextBox is not null)
        {
            SplitEditorTextBox.WordWrap = _splitDocument?.WordWrap ?? wrap;
        }

        UpdateColumnGuide();
    }

    private void OnColumnGuideOffClick(object? sender, RoutedEventArgs e)
        => SetColumnGuide(0);

    private void OnColumnGuide80Click(object? sender, RoutedEventArgs e)
        => SetColumnGuide(80);

    private void OnColumnGuide100Click(object? sender, RoutedEventArgs e)
        => SetColumnGuide(100);

    private void OnColumnGuide120Click(object? sender, RoutedEventArgs e)
        => SetColumnGuide(120);

    private void SetColumnGuide(int column)
    {
        _isColumnGuideEnabled = column > 0;
        if (column > 0)
        {
            _columnGuideColumn = column;
        }

        UpdateColumnGuideMenuChecks();
        UpdateColumnGuide();
        PersistState();
    }

    private void UpdateColumnGuideMenuChecks()
    {
        if (ColumnGuideOffMenuItem is null
            || ColumnGuide80MenuItem is null
            || ColumnGuide100MenuItem is null
            || ColumnGuide120MenuItem is null)
        {
            return;
        }

        ColumnGuideOffMenuItem.IsChecked = !_isColumnGuideEnabled;
        ColumnGuide80MenuItem.IsChecked = _isColumnGuideEnabled && _columnGuideColumn == 80;
        ColumnGuide100MenuItem.IsChecked = _isColumnGuideEnabled && _columnGuideColumn == 100;
        ColumnGuide120MenuItem.IsChecked = _isColumnGuideEnabled && _columnGuideColumn == 120;
    }

    private void OnToggleSplitViewClick(object? sender, RoutedEventArgs e)
    {
        _isSplitViewEnabled = !_isSplitViewEnabled;
        if (_splitDocument is null)
        {
            _splitDocument = _viewModel.SelectedDocument;
        }

        ApplySplitView();
        PersistState();
    }

    private void OnSplitWithNextTabClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Documents.Count == 0)
        {
            return;
        }

        var selected = _viewModel.SelectedDocument;
        if (selected is null)
        {
            return;
        }

        var idx = _viewModel.Documents.IndexOf(selected);
        if (idx < 0)
        {
            return;
        }

        var nextIdx = (idx + 1) % _viewModel.Documents.Count;
        _splitDocument = _viewModel.Documents[nextIdx];
        _isSplitViewEnabled = true;
        ApplySplitView();
        SyncSplitEditorFromDocument();
        RefreshSplitEditorTitle();
        PersistState();
    }

    private void ApplySplitView()
    {
        if (SplitEditorPane is null || EditorSplitSplitter is null || EditorHostGrid is null)
        {
            return;
        }

        SplitEditorPane.IsVisible = _isSplitViewEnabled;
        EditorSplitSplitter.IsVisible = _isSplitViewEnabled;
        EditorHostGrid.ColumnDefinitions = _isSplitViewEnabled
            ? new ColumnDefinitions("*,6,*")
            : new ColumnDefinitions("*,0,0");

        if (SplitViewMenuItem is not null)
        {
            SplitViewMenuItem.IsChecked = _isSplitViewEnabled;
        }

        RefreshSplitEditorTitle();
    }

    private void RefreshSplitEditorTitle()
    {
        if (SplitEditorTitleTextBlock is null)
        {
            return;
        }

        var title = _splitDocument?.DisplayName ?? "current tab";
        SplitEditorTitleTextBlock.Text = $"Split: {title.TrimEnd('*')}";
    }

    private void OnToggleMiniMapClick(object? sender, RoutedEventArgs e)
    {
        _isMiniMapEnabled = !_isMiniMapEnabled;
        ApplyMiniMapVisibility();
        PersistState();
    }

    private void ApplyMiniMapVisibility()
    {
        if (MiniMapPane is null)
        {
            return;
        }

        MiniMapPane.IsVisible = _isMiniMapEnabled;
        if (MiniMapMenuItem is not null)
        {
            MiniMapMenuItem.IsChecked = _isMiniMapEnabled;
        }
    }

    private void UpdateMiniMap()
    {
        if (MiniMapTextBlock is null)
        {
            return;
        }

        var text = EditorTextBox?.Text ?? string.Empty;
        _miniMapLineMap.Clear();
        if (text.Length == 0)
        {
            MiniMapTextBlock.Text = string.Empty;
            return;
        }

        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            _miniMapLineMap.Add(i + 1);
            var line = lines[i];
            if (line.Length > 96)
            {
                line = line[..96];
            }

            sb.Append(line.Replace('\t', ' '));
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }

        MiniMapTextBlock.Text = sb.ToString();
    }

    private void OnMiniMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (MiniMapPane is null || EditorTextBox is null || _miniMapLineMap.Count == 0)
        {
            return;
        }

        var p = e.GetPosition(MiniMapPane);
        var ratio = MiniMapPane.Bounds.Height <= 1 ? 0 : p.Y / MiniMapPane.Bounds.Height;
        var idx = (int)Math.Round(ratio * (_miniMapLineMap.Count - 1));
        idx = Math.Clamp(idx, 0, _miniMapLineMap.Count - 1);
        GoToLine(EditorTextBox, _miniMapLineMap[idx], null);
    }

    private void AttachEditorScrollSync()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        if (_editorScrollViewer is not null)
        {
            return;
        }

        _editorScrollViewer = EditorTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_editorScrollViewer is null)
        {
            return;
        }

        _editorScrollViewer.ScrollChanged += (_, __) => SyncGutterToScroll();
        SyncGutterToScroll();
    }

    private void AttachSplitEditorScrollSync()
    {
        if (SplitEditorTextBox is null)
        {
            return;
        }

        if (_splitEditorScrollViewer is not null)
        {
            return;
        }

        _splitEditorScrollViewer = SplitEditorTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    private void SyncGutterToScroll()
    {
        if (_editorScrollViewer is null)
        {
            return;
        }

        _lineNumbersTransform.Y = -_editorScrollViewer.Offset.Y;
    }

    private void SyncEditorFromDocument()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var next = _viewModel.SelectedDocument?.Text ?? string.Empty;
        if (string.Equals(EditorTextBox.Text, next, StringComparison.Ordinal))
        {
            return;
        }

        _isSyncingEditorText = true;
        try
        {
            EditorTextBox.Text = next;
            EditorTextBox.ScrollTo(1, 1);
        }
        finally
        {
            _isSyncingEditorText = false;
        }
    }

    private void SyncSplitEditorFromDocument()
    {
        if (SplitEditorTextBox is null)
        {
            return;
        }

        _splitDocument ??= _viewModel.SelectedDocument;
        var next = _splitDocument?.Text ?? string.Empty;
        if (string.Equals(SplitEditorTextBox.Text, next, StringComparison.Ordinal))
        {
            return;
        }

        _isSyncingSplitEditorText = true;
        try
        {
            SplitEditorTextBox.Text = next;
            SplitEditorTextBox.ScrollTo(1, 1);
        }
        finally
        {
            _isSyncingSplitEditorText = false;
        }
    }

    private void UpdateLineNumbers()
    {
        if (LineNumbersTextBlock is null || EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var lineCount = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineCount++;
            }
        }

        // Render 1..N.
        var sb = new StringBuilder(lineCount * 4);
        for (var ln = 1; ln <= lineCount; ln++)
        {
            sb.Append(ln);
            if (ln != lineCount)
            {
                sb.Append('\n');
            }
        }

        LineNumbersTextBlock.Text = sb.ToString();
    }

    private void UpdateColumnGuide()
    {
        if (ColumnGuide is null || EditorTextBox is null)
        {
            return;
        }

        ColumnGuide.IsVisible = _isColumnGuideEnabled;
        if (!_isColumnGuideEnabled)
        {
            return;
        }

        // Approximate monospace-ish character width. This is a guide, not an exact ruler.
        var charWidth = EditorTextBox.FontSize * 0.6;
        var left = 10 + (_columnGuideColumn * charWidth);
        ColumnGuide.Margin = new Thickness(left, 0, 0, 0);
    }

    private void OnToggleFoldingClick(object? sender, RoutedEventArgs e)
    {
        _isFoldingEnabled = !_isFoldingEnabled;
        if (FoldingEnabledMenuItem is not null)
        {
            FoldingEnabledMenuItem.IsChecked = _isFoldingEnabled;
        }

        UpdateFolding();
        PersistState();
    }

    private void OnFoldAllClick(object? sender, RoutedEventArgs e)
    {
        if (_foldingManager is null)
        {
            return;
        }

        foreach (var section in _foldingManager.AllFoldings)
        {
            section.IsFolded = true;
        }
    }

    private void OnUnfoldAllClick(object? sender, RoutedEventArgs e)
    {
        if (_foldingManager is null)
        {
            return;
        }

        foreach (var section in _foldingManager.AllFoldings)
        {
            section.IsFolded = false;
        }
    }

    private void UpdateFolding()
    {
        if (_foldingManager is null || EditorTextBox is null)
        {
            return;
        }

        if (FoldingEnabledMenuItem is not null)
        {
            FoldingEnabledMenuItem.IsChecked = _isFoldingEnabled;
        }

        if (!_isFoldingEnabled)
        {
            _foldingManager.Clear();
            return;
        }

        var language = _viewModel.StatusLanguage;
        if (!IsStructuralLanguage(language))
        {
            _foldingManager.Clear();
            return;
        }

        var foldings = BuildFoldings(EditorTextBox.Text ?? string.Empty);
        _foldingManager.UpdateFoldings(foldings, -1);
    }

    private static bool IsStructuralLanguage(string? language)
    {
        return language is "C#" or "JavaScript" or "TypeScript" or "JSON" or "CSS" or "SQL" or "XML" or "HTML";
    }

    private static IEnumerable<NewFolding> BuildFoldings(string text)
    {
        var foldings = new List<NewFolding>();
        if (string.IsNullOrEmpty(text))
        {
            return foldings;
        }

        var lineStarts = new HashSet<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' && i + 1 < text.Length)
            {
                lineStarts.Add(i + 1);
            }
        }

        var braces = new Stack<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '{')
            {
                braces.Push(i);
                continue;
            }

            if (ch == '}' && braces.Count > 0)
            {
                var start = braces.Pop();
                var end = i + 1;
                if (text.IndexOf('\n', start, end - start) >= 0)
                {
                    foldings.Add(new NewFolding(start, end) { Name = "{...}" });
                }
            }
        }

        var regions = new Stack<int>();
        foreach (var lineStart in lineStarts.OrderBy(v => v))
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var raw = text.Substring(lineStart, lineEnd - lineStart).TrimStart();
            if (raw.StartsWith("#region", StringComparison.Ordinal))
            {
                regions.Push(lineStart);
            }
            else if (raw.StartsWith("#endregion", StringComparison.Ordinal) && regions.Count > 0)
            {
                var start = regions.Pop();
                var end = lineEnd;
                if (end > start)
                {
                    foldings.Add(new NewFolding(start, end) { Name = "#region" });
                }
            }
        }

        return foldings.OrderBy(f => f.StartOffset).ThenBy(f => f.EndOffset);
    }

    private void OnThemeDarkPlusClick(object? sender, RoutedEventArgs e)
        => SetThemeMode("Dark+");

    private void OnThemeOneDarkClick(object? sender, RoutedEventArgs e)
        => SetThemeMode("One Dark");

    private void OnThemeMonokaiClick(object? sender, RoutedEventArgs e)
        => SetThemeMode("Monokai");

    private void OnThemeLightClick(object? sender, RoutedEventArgs e)
        => SetThemeMode("Light");

    private void OnThemeModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingThemeModeSelector || ThemeModeComboBox is null)
        {
            return;
        }

        if (ThemeModeComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        var selectedMode = selectedItem.Content?.ToString();
        if (string.IsNullOrWhiteSpace(selectedMode))
        {
            return;
        }

        SetThemeMode(selectedMode);
    }

    private void SetThemeMode(string mode)
    {
        _themeMode = NormalizeThemeMode(mode);
        UpdateThemeModeSelector();
        ApplyThemeMode(_themeMode, persist: true);
    }

    private string NormalizeThemeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Dark+";
        }

        var candidate = mode.Trim();
        return ThemeModes.Any(item => string.Equals(item, candidate, StringComparison.Ordinal))
            ? candidate
            : "Dark+";
    }

    private void UpdateThemeModeSelector()
    {
        if (ThemeModeComboBox is null)
        {
            return;
        }

        _isUpdatingThemeModeSelector = true;
        try
        {
            var selected = NormalizeThemeMode(_themeMode);
            var comboItem = ThemeModeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), selected, StringComparison.Ordinal));

            ThemeModeComboBox.SelectedItem = comboItem ?? ThemeModeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isUpdatingThemeModeSelector = false;
        }
    }

    private void UpdateThemeMenuChecks()
    {
        if (ThemeDarkPlusMenuItem is null
            || ThemeOneDarkMenuItem is null
            || ThemeMonokaiMenuItem is null
            || ThemeLightMenuItem is null)
        {
            return;
        }

        ThemeDarkPlusMenuItem.IsChecked = string.Equals(_themeMode, "Dark+", StringComparison.Ordinal);
        ThemeOneDarkMenuItem.IsChecked = string.Equals(_themeMode, "One Dark", StringComparison.Ordinal);
        ThemeMonokaiMenuItem.IsChecked = string.Equals(_themeMode, "Monokai", StringComparison.Ordinal);
        ThemeLightMenuItem.IsChecked = string.Equals(_themeMode, "Light", StringComparison.Ordinal);
    }

    private void ApplyThemeMode(string mode, bool persist)
    {
        mode = NormalizeThemeMode(mode);
        _themeMode = mode;
        var light = string.Equals(mode, "Light", StringComparison.Ordinal);
        var app = Application.Current;
        if (app is not null)
        {
            app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        var editorBackground = mode switch
        {
            "One Dark" => "#282C34",
            "Monokai" => "#272822",
            "Light" => "#FFFFFF",
            _ => "#1E1E1E",
        };

        var editorForeground = mode switch
        {
            "One Dark" => "#ABB2BF",
            "Monokai" => "#F8F8F2",
            "Light" => "#1F2933",
            _ => "#D4D4D4",
        };

        var chromePanel = mode switch
        {
            "One Dark" => "#21252B",
            "Monokai" => "#252526",
            "Light" => "#EEF2F6",
            _ => "#161F2A",
        };

        var chromeAlt = mode switch
        {
            "One Dark" => "#1E2228",
            "Monokai" => "#1F1F1C",
            "Light" => "#FFFFFF",
            _ => "#121B25",
        };

        var menuBar = mode switch
        {
            "One Dark" => "#1B1F25",
            "Monokai" => "#20201C",
            "Light" => "#E7ECF2",
            _ => "#161F2A",
        };

        var border = mode switch
        {
            "Light" => "#BFC9D3",
            _ => "#2C3B4A",
        };

        var accentSoft = mode switch
        {
            "Light" => "#6D889F",
            _ => "#79BDD8",
        };

        var accentMuted = mode switch
        {
            "Light" => "#98ABBD",
            _ => "#446E86",
        };

        Resources["ChromePanelBrush"] = new SolidColorBrush(Color.Parse(chromePanel));
        Resources["ChromePanelAltBrush"] = new SolidColorBrush(Color.Parse(chromeAlt));
        Resources["MenuBarBrush"] = new SolidColorBrush(Color.Parse(menuBar));
        Resources["ChromeBorderBrush"] = new SolidColorBrush(Color.Parse(border));
        Resources["AccentSoftBrush"] = new SolidColorBrush(Color.Parse(accentSoft));
        Resources["AccentMutedBrush"] = new SolidColorBrush(Color.Parse(accentMuted));

        var menuText = mode switch
        {
            "Light" => "#263646",
            _ => "#E6EDF3",
        };

        var topNavText = mode switch
        {
            "Light" => "#2A3D4F",
            _ => "#F2F7FC",
        };

        var menuIcon = mode switch
        {
            "Light" => "#50657B",
            _ => "#A9C3D8",
        };

        var toolbarSurface = mode switch
        {
            "Light" => "#EEF2F6",
            _ => "#111B29",
        };

        var toolbarButton = mode switch
        {
            "Light" => "#FFFFFF",
            _ => "#152234",
        };

        var toolbarButtonHover = mode switch
        {
            "Light" => "#F1F6FB",
            _ => "#1F3348",
        };

        var toolbarButtonPressed = mode switch
        {
            "Light" => "#E6EEF7",
            _ => "#142537",
        };

        var toolbarButtonText = mode switch
        {
            "Light" => "#23384B",
            _ => "#E8F1F8",
        };

        var toolbarIcon = mode switch
        {
            "Light" => "#4A647E",
            _ => "#9CC6E3",
        };

        var quickPicker = mode switch
        {
            "Light" => "#FFFFFF",
            _ => "#132133",
        };

        var quickPickerHover = mode switch
        {
            "Light" => "#F1F6FB",
            _ => "#1D3044",
        };

        var quickPickerText = mode switch
        {
            "Light" => "#23384B",
            _ => "#E6EDF3",
        };

        var statusPicker = mode switch
        {
            "Light" => "#FFFFFF",
            _ => "#152232",
        };

        var statusPickerHover = mode switch
        {
            "Light" => "#F1F6FB",
            _ => "#1D3044",
        };

        var statusPickerText = mode switch
        {
            "Light" => "#23384B",
            _ => "#E6EDF3",
        };

        var tabBackground = mode switch
        {
            "Light" => "#EEF2F7",
            _ => "#1A2A3A",
        };

        var tabHover = mode switch
        {
            "Light" => "#E5ECF4",
            _ => "#1F2C3A",
        };

        var tabSelected = mode switch
        {
            "Light" => "#DDE7F2",
            _ => "#27435A",
        };

        var tabText = mode switch
        {
            "Light" => "#24384B",
            _ => "#E6EDF3",
        };

        Resources["MenuTextBrush"] = new SolidColorBrush(Color.Parse(menuText));
        Resources["TopNavTextBrush"] = new SolidColorBrush(Color.Parse(topNavText));
        Resources["MenuIconBrush"] = new SolidColorBrush(Color.Parse(menuIcon));
        Resources["ToolbarSurfaceBrush"] = new SolidColorBrush(Color.Parse(toolbarSurface));
        Resources["ToolbarButtonBrush"] = new SolidColorBrush(Color.Parse(toolbarButton));
        Resources["ToolbarButtonHoverBrush"] = new SolidColorBrush(Color.Parse(toolbarButtonHover));
        Resources["ToolbarButtonPressedBrush"] = new SolidColorBrush(Color.Parse(toolbarButtonPressed));
        Resources["ToolbarButtonTextBrush"] = new SolidColorBrush(Color.Parse(toolbarButtonText));
        Resources["ToolbarIconBrush"] = new SolidColorBrush(Color.Parse(toolbarIcon));
        Resources["QuickPickerBrush"] = new SolidColorBrush(Color.Parse(quickPicker));
        Resources["QuickPickerHoverBrush"] = new SolidColorBrush(Color.Parse(quickPickerHover));
        Resources["QuickPickerTextBrush"] = new SolidColorBrush(Color.Parse(quickPickerText));
        Resources["StatusPickerBrush"] = new SolidColorBrush(Color.Parse(statusPicker));
        Resources["StatusPickerHoverBrush"] = new SolidColorBrush(Color.Parse(statusPickerHover));
        Resources["StatusPickerTextBrush"] = new SolidColorBrush(Color.Parse(statusPickerText));
        Resources["TabBackgroundBrush"] = new SolidColorBrush(Color.Parse(tabBackground));
        Resources["TabHoverBrush"] = new SolidColorBrush(Color.Parse(tabHover));
        Resources["TabSelectedBrush"] = new SolidColorBrush(Color.Parse(tabSelected));
        Resources["TabTextBrush"] = new SolidColorBrush(Color.Parse(tabText));

        var backdropStart = mode switch
        {
            "One Dark" => Color.Parse("#20232A"),
            "Monokai" => Color.Parse("#22221E"),
            "Light" => Color.Parse("#F3F6FA"),
            _ => Color.Parse("#0F1620"),
        };

        var backdropMid = mode switch
        {
            "One Dark" => Color.Parse("#1D2026"),
            "Monokai" => Color.Parse("#1F1F1B"),
            "Light" => Color.Parse("#EDF2F8"),
            _ => Color.Parse("#0C121A"),
        };

        var backdropEnd = mode switch
        {
            "One Dark" => Color.Parse("#191C22"),
            "Monokai" => Color.Parse("#1B1B18"),
            "Light" => Color.Parse("#E8EEF5"),
            _ => Color.Parse("#090F15"),
        };

        Resources["WindowBackdropBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(backdropStart, 0.0),
                new GradientStop(backdropMid, 0.55),
                new GradientStop(backdropEnd, 1.0),
            },
        };

        ApplyEditorSurfaceTheme(editorBackground, editorForeground);
        _themedHighlightDefinitions.Clear();
        ApplyLanguageStyling();
        UpdateMiniMap();
        UpdateThemeMenuChecks();
        UpdateThemeModeSelector();

        if (persist)
        {
            PersistState();
        }
    }

    private void ApplyEditorSurfaceTheme(string backgroundHex, string foregroundHex)
    {
        var bg = new SolidColorBrush(Color.Parse(backgroundHex));
        var fg = new SolidColorBrush(Color.Parse(foregroundHex));
        var caret = new SolidColorBrush(Color.Parse(string.Equals(_themeMode, "Light", StringComparison.Ordinal) ? "#333333" : "#AEAFAD"));

        if (EditorTextBox is not null)
        {
            EditorTextBox.Background = bg;
            EditorTextBox.Foreground = fg;
            EditorTextBox.TextArea.Foreground = fg;
            EditorTextBox.TextArea.SelectionForeground = fg;
            EditorTextBox.TextArea.CaretBrush = caret;
        }

        if (SplitEditorTextBox is not null)
        {
            SplitEditorTextBox.Background = bg;
            SplitEditorTextBox.Foreground = fg;
            SplitEditorTextBox.TextArea.Foreground = fg;
            SplitEditorTextBox.TextArea.SelectionForeground = fg;
            SplitEditorTextBox.TextArea.CaretBrush = caret;
        }

        if (LineNumberGutter is not null)
        {
            LineNumberGutter.Background = string.Equals(_themeMode, "Light", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.Parse("#F1F4F8"))
                : new SolidColorBrush(Color.Parse("#111821"));
        }

        if (MiniMapPane is not null)
        {
            MiniMapPane.Background = string.Equals(_themeMode, "Light", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.Parse("#F7FAFD"))
                : new SolidColorBrush(Color.Parse("#101923"));
        }

        if (MiniMapTextBlock is not null)
        {
            MiniMapTextBlock.Foreground = fg;
        }

        if (ColumnGuide is not null)
        {
            ColumnGuide.Background = string.Equals(_themeMode, "Light", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.Parse("#5E8FB8"))
                : new SolidColorBrush(Color.Parse("#8FD3FF"));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var ctrlOrCmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (e.Key == Key.F1 || (ctrlOrCmd && e.Key == Key.OemQuestion))
        {
            _ = ShowKeyboardShortcutsAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_viewModel.IsFindReplaceVisible)
            {
                OnHideFindReplaceClick(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (ctrlOrCmd && e.Key == Key.N)
        {
            _viewModel.NewDocument();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.O)
        {
            OnOpenClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.P)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _ = ShowCommandPaletteAsync();
            }
            else
            {
                _ = ShowQuickOpenAsync();
            }
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.Oem5)
        {
            OnToggleSplitViewClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.S)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                OnSaveAsClick(this, new RoutedEventArgs());
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                OnSaveAllClick(this, new RoutedEventArgs());
            }
            else
            {
                OnSaveClick(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.W)
        {
            OnCloseTabClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.T)
        {
            OnReopenClosedTabClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Meta) && e.Key == Key.Q)
        {
            // macOS quit.
            Close();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.F)
        {
            ShowFind(replace: false);
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.H)
        {
            ShowFind(replace: true);
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            var forward = !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            FindNext(forward);
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.G)
        {
            _ = ShowGoToLineAsync();
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.F)
        {
            OnFormatDocumentClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.OemCloseBrackets)
        {
            OnGoToMatchingBracketClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                Redo();
            }
            else
            {
                Undo();
            }

            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.D)
        {
            DuplicateLineOrSelection();
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.K)
        {
            DeleteCurrentLine();
            e.Handled = true;
        }
        else if (ctrlOrCmd && (e.Key == Key.Add || e.Key == Key.OemPlus))
        {
            ZoomBy(+1);
            e.Handled = true;
        }
        else if (ctrlOrCmd && (e.Key == Key.Subtract || e.Key == Key.OemMinus))
        {
            ZoomBy(-1);
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.D0)
        {
            ZoomReset();
            e.Handled = true;
        }
    }

    private void OnSaveAllClick(object? sender, RoutedEventArgs e)
        => _ = SaveAllAsync();

    private async Task SaveAllAsync()
    {
        foreach (var doc in _viewModel.Documents.ToList())
        {
            if (!doc.IsDirty)
            {
                continue;
            }

            var before = doc.IsDirty;
            await SaveDocumentAsync(doc);
            if (before && doc.IsDirty)
            {
                // Cancelled.
                break;
            }
        }
    }

    private void OnCloseAllTabsClick(object? sender, RoutedEventArgs e)
        => _ = CloseAllTabsAsync();

    private async Task CloseAllTabsAsync()
    {
        foreach (var doc in _viewModel.Documents.ToList())
        {
            var closed = await CloseDocumentAsync(doc);
            if (!closed)
            {
                return;
            }
        }

        if (_viewModel.Documents.Count == 0)
        {
            _viewModel.NewDocument();
        }
    }

    private async void OnReopenClosedTabClick(object? sender, RoutedEventArgs e)
        => await ReopenClosedTabAsync();

    private async Task ReopenClosedTabAsync()
    {
        if (_closedTabs.Count == 0)
        {
            return;
        }

        var snap = _closedTabs.Pop();
        if (!string.IsNullOrWhiteSpace(snap.FilePath) && File.Exists(snap.FilePath) && !snap.WasDirty)
        {
            await OpenFilePathAsync(snap.FilePath);
            var opened = _viewModel.SelectedDocument;
            if (opened is not null)
            {
                opened.WordWrap = snap.WordWrap;
                opened.SetFileLastWriteTimeUtc(snap.FileLastWriteTimeUtc);
                ApplyWordWrap();
            }

            return;
        }

        var doc = TextDocument.CreateNew();
        doc.FilePath = snap.FilePath;
        try
        {
            doc.Encoding = Encoding.GetEncoding(snap.EncodingWebName);
        }
        catch
        {
            // Ignore.
        }

        doc.HasBom = snap.HasBom;
        doc.PreferredLineEnding = snap.PreferredLineEnding;
        doc.WordWrap = snap.WordWrap;
        doc.SetFileLastWriteTimeUtc(snap.FileLastWriteTimeUtc);
        doc.Text = snap.Text;

        if (!snap.WasDirty)
        {
            doc.MarkSaved();
        }

        ReplaceInitialEmptyDocumentIfNeeded();
        _viewModel.Documents.Add(doc);
        _viewModel.SelectedDocument = doc;
        ApplyWordWrap();
    }

    private void OnDuplicateClick(object? sender, RoutedEventArgs e)
        => DuplicateLineOrSelection();

    private void OnDeleteLineClick(object? sender, RoutedEventArgs e)
        => DeleteCurrentLine();

    private void OnUppercaseClick(object? sender, RoutedEventArgs e)
        => TransformSelection(static s => s.ToUpperInvariant());

    private void OnLowercaseClick(object? sender, RoutedEventArgs e)
        => TransformSelection(static s => s.ToLowerInvariant());

    private int GetSelectionEnd()
        => EditorTextBox is null ? 0 : EditorTextBox.SelectionStart + EditorTextBox.SelectionLength;

    private void SetSelection(int start, int end)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.SelectionStart = start;
        EditorTextBox.SelectionLength = Math.Max(0, end - start);
    }

    private void OnEditorTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || sender is not TextArea textArea)
        {
            return;
        }

        var editor = ReferenceEquals(textArea, EditorTextBox?.TextArea)
            ? EditorTextBox
            : ReferenceEquals(textArea, SplitEditorTextBox?.TextArea)
                ? SplitEditorTextBox
                : null;
        if (editor is null || !TryGetAutoClosePair(e.Text[0], out var closing))
        {
            return;
        }

        var doc = editor.Document;
        if (doc is null)
        {
            return;
        }

        var offset = editor.CaretOffset;
        if (offset < 0 || offset > doc.TextLength)
        {
            return;
        }

        if ((e.Text[0] == '"' || e.Text[0] == '\'')
            && offset < doc.TextLength
            && doc.GetCharAt(offset) == e.Text[0])
        {
            return;
        }

        doc.Insert(offset, closing.ToString());
        editor.CaretOffset = offset;
    }

    private static bool TryGetAutoClosePair(char open, out char close)
    {
        close = open switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '"' => '"',
            '\'' => '\'',
            _ => '\0',
        };

        return close != '\0';
    }

    private void OnGoToMatchingBracketClick(object? sender, RoutedEventArgs e)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (TryFindMatchingBracket(text, EditorTextBox.CaretOffset, out var matchOffset))
        {
            EditorTextBox.Focus();
            EditorTextBox.CaretOffset = matchOffset;
            EditorTextBox.SelectionStart = matchOffset;
            EditorTextBox.SelectionLength = 0;
        }
    }

    private static bool TryFindMatchingBracket(string text, int caretOffset, out int matchOffset)
    {
        matchOffset = -1;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var pivot = caretOffset > 0 && caretOffset <= text.Length ? caretOffset - 1 : caretOffset;
        if (pivot < 0 || pivot >= text.Length)
        {
            return false;
        }

        var c = text[pivot];
        if (c is '(' or '[' or '{')
        {
            var close = c == '(' ? ')' : c == '[' ? ']' : '}';
            var depth = 0;
            for (var i = pivot; i < text.Length; i++)
            {
                if (text[i] == c) depth++;
                else if (text[i] == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        matchOffset = i;
                        return true;
                    }
                }
            }

            return false;
        }

        if (c is ')' or ']' or '}')
        {
            var open = c == ')' ? '(' : c == ']' ? '[' : '{';
            var depth = 0;
            for (var i = pivot; i >= 0; i--)
            {
                if (text[i] == c) depth++;
                else if (text[i] == open)
                {
                    depth--;
                    if (depth == 0)
                    {
                        matchOffset = i;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void TransformSelection(Func<string, string> transform)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var end = GetSelectionEnd();
        if (end <= EditorTextBox.SelectionStart)
        {
            return;
        }

        var start = EditorTextBox.SelectionStart;
        var text = EditorTextBox.Text ?? string.Empty;
        var selected = EditorTextBox.SelectedText ?? string.Empty;

        var replacement = transform(selected);
        var newText = text.Substring(0, start) + replacement + text.Substring(end);
        EditorTextBox.Text = newText;
        SetSelection(start, start + replacement.Length);
    }

    private void DuplicateLineOrSelection()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var selStart = EditorTextBox.SelectionStart;
        var selEnd = GetSelectionEnd();

        if (selEnd > selStart)
        {
            var selected = EditorTextBox.SelectedText ?? string.Empty;
            var insertAt = selEnd;
            var newText = text.Substring(0, insertAt) + selected + text.Substring(insertAt);
            EditorTextBox.Text = newText;
            SelectMatch(insertAt, selected.Length);
            return;
        }

        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }
        else
        {
            lineEnd += 1; // include newline
        }

        var line = text.Substring(lineStart, lineEnd - lineStart);
        var insertPos = lineEnd;
        var text2 = text.Substring(0, insertPos) + line + text.Substring(insertPos);
        EditorTextBox.Text = text2;
        EditorTextBox.CaretOffset = insertPos + line.Length;
    }

    private void DeleteCurrentLine()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }
        else
        {
            lineEnd += 1;
        }

        var newText = text.Substring(0, lineStart) + text.Substring(lineEnd);
        EditorTextBox.Text = newText;
        EditorTextBox.CaretOffset = Math.Min(lineStart, newText.Length);
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
        => ZoomBy(+1);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
        => ZoomBy(-1);

    private void OnZoomResetClick(object? sender, RoutedEventArgs e)
        => ZoomReset();

    private void ZoomBy(int delta)
    {
        var size = _viewModel.EditorFontSize;
        size += delta;
        if (size < 8) size = 8;
        if (size > 48) size = 48;
        _viewModel.EditorFontSize = size;
    }

    private void ZoomReset()
        => _viewModel.EditorFontSize = 14;

    private async void OnKeyboardShortcutsClick(object? sender, RoutedEventArgs e)
        => await ShowKeyboardShortcutsAsync();

    private void OnCutClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Cut();

    private void OnCopyClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Copy();

    private void OnPasteClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.Paste();

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
        => EditorTextBox?.SelectAll();

    private async Task ShowKeyboardShortcutsAsync()
    {
        var dialog = new KeyboardShortcutsDialog();
        await dialog.ShowDialog(this);
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e)
        => Undo();

    private void OnRedoClick(object? sender, RoutedEventArgs e)
        => Redo();

    private void Undo()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Undo();
        UpdateCaretStatus();
    }

    private void Redo()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Redo();
        UpdateCaretStatus();
    }

    private void OnNewClick(object? sender, RoutedEventArgs e)
        => _viewModel.NewDocument();

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var provider = StorageProvider;
        if (provider is null)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open",
            AllowMultiple = false,
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        await OpenFileAsync(file);
    }

    private async void OnQuickOpenClick(object? sender, RoutedEventArgs e)
        => await ShowQuickOpenAsync();

    private async void OnCommandPaletteClick(object? sender, RoutedEventArgs e)
        => await ShowCommandPaletteAsync();

    private void InitializeCommandPaletteActions()
    {
        _commandPaletteActions["file.new"] = () => OnNewClick(this, new RoutedEventArgs());
        _commandPaletteActions["file.open"] = () => OnOpenClick(this, new RoutedEventArgs());
        _commandPaletteActions["file.quickOpen"] = () => OnQuickOpenClick(this, new RoutedEventArgs());
        _commandPaletteActions["file.save"] = () => OnSaveClick(this, new RoutedEventArgs());
        _commandPaletteActions["file.saveAs"] = () => OnSaveAsClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.find"] = () => OnShowFindClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.replace"] = () => OnShowReplaceClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.goto"] = () => OnGoToLineClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.formatDocument"] = () => OnFormatDocumentClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.formatSelection"] = () => OnFormatSelectionClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.split"] = () => OnToggleSplitViewClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.minimap"] = () => OnToggleMiniMapClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.theme.darkplus"] = () => OnThemeDarkPlusClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.theme.onedark"] = () => OnThemeOneDarkClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.theme.monokai"] = () => OnThemeMonokaiClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.theme.light"] = () => OnThemeLightClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.foldAll"] = () => OnFoldAllClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.unfoldAll"] = () => OnUnfoldAllClick(this, new RoutedEventArgs());
    }

    private async Task ShowCommandPaletteAsync()
    {
        var items = new[]
        {
            new PaletteItem("file.new", "New File", "File"),
            new PaletteItem("file.open", "Open File...", "File"),
            new PaletteItem("file.quickOpen", "Quick Open...", "File"),
            new PaletteItem("file.save", "Save", "File"),
            new PaletteItem("file.saveAs", "Save As...", "File"),
            new PaletteItem("edit.find", "Find", "Edit"),
            new PaletteItem("edit.replace", "Replace", "Edit"),
            new PaletteItem("edit.goto", "Go To Line", "Edit"),
            new PaletteItem("edit.formatDocument", "Format Document", "Format"),
            new PaletteItem("edit.formatSelection", "Format Selection", "Format"),
            new PaletteItem("view.split", "Toggle Split Editor", "View"),
            new PaletteItem("view.minimap", "Toggle Mini Map", "View"),
            new PaletteItem("view.theme.darkplus", "Theme: Dark+", "View"),
            new PaletteItem("view.theme.onedark", "Theme: One Dark", "View"),
            new PaletteItem("view.theme.monokai", "Theme: Monokai", "View"),
            new PaletteItem("view.theme.light", "Theme: Light", "View"),
            new PaletteItem("view.foldAll", "Fold All", "View"),
            new PaletteItem("view.unfoldAll", "Unfold All", "View"),
        };

        var dialog = new SelectionPaletteDialog("Command Palette", "Search commands...", items);
        var command = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (_commandPaletteActions.TryGetValue(command, out var action))
        {
            action();
        }
    }

    private async Task ShowQuickOpenAsync()
    {
        var items = new List<PaletteItem>();

        foreach (var doc in _viewModel.Documents)
        {
            var id = $"doc:{doc.DocumentId}";
            var desc = string.IsNullOrWhiteSpace(doc.FilePath) ? "Open tab" : doc.FilePath!;
            items.Add(new PaletteItem(id, doc.DisplayName.TrimEnd('*'), desc));
        }

        foreach (var file in _viewModel.RecentFiles)
        {
            items.Add(new PaletteItem($"file:{file}", Path.GetFileName(file), file));
        }

        var dialog = new SelectionPaletteDialog("Quick Open", "Type filename or path...", items);
        var selected = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        if (selected.StartsWith("doc:", StringComparison.Ordinal))
        {
            var raw = selected.Substring("doc:".Length);
            if (Guid.TryParse(raw, out var id))
            {
                var target = _viewModel.Documents.FirstOrDefault(d => d.DocumentId == id);
                if (target is not null)
                {
                    _viewModel.SelectedDocument = target;
                }
            }
            return;
        }

        if (selected.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = selected.Substring("file:".Length);
            if (File.Exists(path))
            {
                await OpenFilePathAsync(path);
            }
        }
    }

    private async Task OpenFilePathAsync(string filePath)
    {
        var existing = _viewModel.Documents.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.FilePath) &&
            string.Equals(Path.GetFullPath(d.FilePath!), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _viewModel.SelectedDocument = existing;
            return;
        }

        await using var input = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var doc = await _fileService.LoadAsync(input, filePath: filePath);
        StampFileWriteTimeIfPossible(doc);

        ReplaceInitialEmptyDocumentIfNeeded();
        _viewModel.Documents.Add(doc);
        _viewModel.SelectedDocument = doc;

        _viewModel.AddRecentFile(filePath);
        PersistState();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var doc = _viewModel.SelectedDocument;
        if (doc is null)
        {
            return;
        }

        await SaveDocumentAsync(doc);
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var doc = _viewModel.SelectedDocument;
        if (doc is null)
        {
            return;
        }

        await SaveAsAsync(doc);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
        => Close();

    private async void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        var doc = _viewModel.SelectedDocument;
        if (doc is null)
        {
            return;
        }

        var closed = await CloseDocumentAsync(doc);
        if (!closed)
        {
            return;
        }

        if (_viewModel.Documents.Count == 0)
        {
            _viewModel.NewDocument();
        }
    }

    private async void OnCloseTabButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.DataContext is not TextDocument doc)
        {
            return;
        }

        e.Handled = true;

        var closed = await CloseDocumentAsync(doc);
        if (!closed)
        {
            return;
        }

        if (_viewModel.Documents.Count == 0)
        {
            _viewModel.NewDocument();
        }
    }

    private async Task OpenFileAsync(IStorageFile file)
    {
        var path = file.Path?.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await OpenFilePathAsync(path);
            return;
        }

        await using var input = await file.OpenReadAsync();
        var doc = await _fileService.LoadAsync(input, filePath: null);

        StampFileWriteTimeIfPossible(doc);

        ReplaceInitialEmptyDocumentIfNeeded();

        _viewModel.Documents.Add(doc);
        _viewModel.SelectedDocument = doc;

        _viewModel.AddRecentFile(path);
        PersistState();
    }

    private async Task SaveAsAsync(TextDocument doc)
    {
        var provider = StorageProvider;
        if (provider is null)
        {
            return;
        }

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = string.IsNullOrWhiteSpace(doc.FilePath) ? "Untitled.txt" : Path.GetFileName(doc.FilePath),
        });

        if (file is null)
        {
            return;
        }

        var localPath = file.Path?.LocalPath;
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            doc.FilePath = localPath;
            await _fileService.SaveToFileAsync(doc, localPath);
        }
        else
        {
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek)
            {
                output.SetLength(0);
            }

            await _fileService.SaveAsync(doc, output);
        }

        _viewModel.AddRecentFile(doc.FilePath);
        PersistState();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            _recoveryManager.Dispose();
            return;
        }

        // We'll close manually after async prompts.
        e.Cancel = true;

        foreach (var doc in _viewModel.Documents.ToList())
        {
            if (!doc.IsDirty)
            {
                continue;
            }

            var closed = await CloseDocumentAsync(doc);
            if (!closed)
            {
                return;
            }
        }

        _allowClose = true;

        _recoveryManager.Dispose();

        PersistState();
        Close();
    }

    private async Task<bool> CloseDocumentAsync(TextDocument doc)
    {
        if (doc.IsDirty)
        {
            var choice = await PromptUnsavedChangesAsync(doc);
            switch (choice)
            {
                case UnsavedChangesChoice.Cancel:
                    return false;
                case UnsavedChangesChoice.DontSave:
                    break;
                case UnsavedChangesChoice.Save:
                    // Save might open a Save As picker.
                    var before = doc.IsDirty;
                    await SaveDocumentAsync(doc);
                    if (before && doc.IsDirty)
                    {
                        // Save was cancelled or failed.
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }

        var index = _viewModel.Documents.IndexOf(doc);

        _closedTabs.Push(new ClosedTabSnapshot(
            FilePath: doc.FilePath,
            Text: doc.Text ?? string.Empty,
            EncodingWebName: doc.Encoding.WebName,
            HasBom: doc.HasBom,
            PreferredLineEnding: doc.PreferredLineEnding,
            WordWrap: doc.WordWrap,
            WasDirty: doc.IsDirty,
            FileLastWriteTimeUtc: doc.FileLastWriteTimeUtc));

        _viewModel.Documents.Remove(doc);

        _recoveryManager.OnDocumentClosed(doc);

        if (ReferenceEquals(_viewModel.SelectedDocument, doc))
        {
            _viewModel.SelectedDocument = _viewModel.Documents.Count == 0
                ? null
                : _viewModel.Documents[Math.Clamp(index, 0, _viewModel.Documents.Count - 1)];
        }

        if (ReferenceEquals(_splitDocument, doc))
        {
            _splitDocument = _viewModel.Documents.FirstOrDefault();
            SyncSplitEditorFromDocument();
            RefreshSplitEditorTitle();
        }

        return true;
    }

    private async Task SaveDocumentAsync(TextDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.FilePath) && File.Exists(doc.FilePath))
        {
            var choice = await CheckForExternalFileChangeAsync(doc);
            if (choice == FileChangedOnDiskChoice.Cancel)
            {
                return;
            }

            if (choice == FileChangedOnDiskChoice.Reload)
            {
                await ReloadFromDiskAsync(doc);
                return;
            }

            await _fileService.SaveToFileAsync(doc, doc.FilePath);
            _recoveryManager.OnDocumentSaved(doc);
            _viewModel.AddRecentFile(doc.FilePath);
            PersistState();
            return;
        }

        await SaveAsAsync(doc);

        if (!doc.IsDirty)
        {
            _recoveryManager.OnDocumentSaved(doc);
        }
    }

    private async Task MaybeRecoverAsync()
    {
        var files = _recoveryStore.ListSnapshotFiles();
        if (files.Count == 0)
        {
            return;
        }

        var snapshots = new List<(string filePath, RecoverySnapshot snapshot)>(files.Count);
        foreach (var file in files)
        {
            var snap = await _recoveryStore.LoadAsync(file);
            if (snap is not null)
            {
                snapshots.Add((file, snap));
            }
        }

        if (snapshots.Count == 0)
        {
            foreach (var file in files)
            {
                _recoveryStore.DeleteFile(file);
            }

            return;
        }

        var candidates = snapshots
            .OrderByDescending(s => s.snapshot.TimestampUtc)
            .Select(s => new RecoveryCandidate(
                FilePath: s.snapshot.FilePath,
                TimestampUtc: s.snapshot.TimestampUtc,
                LineCount: CountLines(s.snapshot.Text)))
            .ToList();

        var dialog = new RecoveryDialog(candidates);
        var choice = await dialog.ShowDialog<RecoveryChoice>(this);

        if (choice == RecoveryChoice.Cancel)
        {
            return;
        }

        if (choice == RecoveryChoice.Discard)
        {
            foreach (var file in files)
            {
                _recoveryStore.DeleteFile(file);
            }

            return;
        }

        foreach (var entry in snapshots.OrderBy(s => s.snapshot.TimestampUtc))
        {
            var snap = entry.snapshot;

            var doc = TextDocument.CreateNew();
            if (!string.IsNullOrWhiteSpace(snap.FilePath))
            {
                doc.FilePath = snap.FilePath;
            }

            try
            {
                doc.Encoding = System.Text.Encoding.GetEncoding(snap.EncodingWebName);
            }
            catch
            {
                // Fallback.
            }

            doc.HasBom = snap.HasBom;
            doc.PreferredLineEnding = snap.PreferredLineEnding;
            doc.SetFileLastWriteTimeUtc(snap.FileLastWriteTimeUtc);
            doc.Text = snap.Text ?? string.Empty;

            ReplaceInitialEmptyDocumentIfNeeded();
            _viewModel.Documents.Add(doc);
            _viewModel.SelectedDocument = doc;
            _recoveryStore.DeleteFile(entry.filePath);
        }
    }

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var count = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private async Task ReloadFromDiskAsync(TextDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            return;
        }

        await using var input = File.Open(doc.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await _fileService.ReloadAsync(doc, input, filePath: doc.FilePath);
        StampFileWriteTimeIfPossible(doc);
    }

    private async Task<FileChangedOnDiskChoice> CheckForExternalFileChangeAsync(TextDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            return FileChangedOnDiskChoice.Overwrite;
        }

        if (doc.FileLastWriteTimeUtc is null)
        {
            StampFileWriteTimeIfPossible(doc);
            return FileChangedOnDiskChoice.Overwrite;
        }

        var current = File.GetLastWriteTimeUtc(doc.FilePath);
        if (doc.FileLastWriteTimeUtc.Value.UtcDateTime == current)
        {
            return FileChangedOnDiskChoice.Overwrite;
        }

        var dialog = new FileChangedOnDiskDialog();
        return await dialog.ShowDialog<FileChangedOnDiskChoice>(this);
    }

    private static void StampFileWriteTimeIfPossible(TextDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            return;
        }

        var utc = DateTime.SpecifyKind(File.GetLastWriteTimeUtc(doc.FilePath), DateTimeKind.Utc);
        doc.SetFileLastWriteTimeUtc(new DateTimeOffset(utc));
    }

    private async Task<UnsavedChangesChoice> PromptUnsavedChangesAsync()
    {
        return await PromptUnsavedChangesAsync(_viewModel.SelectedDocument);
    }

    private async Task<UnsavedChangesChoice> PromptUnsavedChangesAsync(TextDocument? doc)
    {
        var name = doc is null ? "this document" : doc.DisplayName.TrimEnd('*');
        var dialog = new UnsavedChangesDialog($"Save changes to {name}?");
        return await dialog.ShowDialog<UnsavedChangesChoice>(this);
    }

    private void PersistState()
    {
        _state.RecentFiles = _viewModel.RecentFiles.ToList();
        _state.LastSessionFiles = _viewModel.GetSessionFilePaths().ToList();
        _state.Theme = _themeMode;
        _state.LanguageMode = _languageMode;
        _state.ShowMiniMap = _isMiniMapEnabled;
        _state.SplitViewEnabled = _isSplitViewEnabled;
        _state.FoldingEnabled = _isFoldingEnabled;
        _state.ColumnGuideEnabled = _isColumnGuideEnabled;
        _state.ColumnGuideColumn = _columnGuideColumn;
        _stateStore.Save(_state);
    }

    private void ReplaceInitialEmptyDocumentIfNeeded()
    {
        if (_viewModel.Documents.Count != 1)
        {
            return;
        }

        var first = _viewModel.Documents[0];
        if (!string.IsNullOrWhiteSpace(first.FilePath))
        {
            return;
        }

        if (first.IsDirty)
        {
            return;
        }

        if (!string.IsNullOrEmpty(first.Text))
        {
            return;
        }

        _viewModel.Documents.Clear();
        _viewModel.SelectedDocument = null;
    }

    private async Task ReopenLastSessionAsync()
    {
        if (_state.LastSessionFiles is null || _state.LastSessionFiles.Count == 0)
        {
            return;
        }

        // Only reopen if we haven't already opened something.
        if (_viewModel.Documents.Any(d => !string.IsNullOrWhiteSpace(d.FilePath)))
        {
            return;
        }

        foreach (var file in _state.LastSessionFiles.ToList())
        {
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                await OpenFilePathAsync(file);
            }
            catch
            {
                // Ignore broken session entries.
            }
        }
    }

    private void RefreshOpenRecentMenu()
    {
        if (OpenRecentMenuItem is null)
        {
            return;
        }

        var items = new List<object>();
        foreach (var path in _viewModel.RecentFiles)
        {
            var p = path;
            var item = new MenuItem { Header = p };
            item.Click += async (_, __) =>
            {
                try
                {
                    if (File.Exists(p))
                    {
                        await OpenFilePathAsync(p);
                    }
                }
                catch
                {
                    // Ignore.
                }
            };
            items.Add(item);
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
        }

        items.Add(new Separator());

        var clear = new MenuItem { Header = "Clear Recent Files", IsEnabled = _viewModel.RecentFiles.Count > 0 };
        clear.Click += (_, __) =>
        {
            _viewModel.RecentFiles.Clear();
            PersistState();
            RefreshOpenRecentMenu();
        };
        items.Add(clear);

        OpenRecentMenuItem.ItemsSource = items;
    }

    private void OnShowFindClick(object? sender, RoutedEventArgs e)
        => ShowFind(replace: false);

    private void OnShowReplaceClick(object? sender, RoutedEventArgs e)
        => ShowFind(replace: true);

    private async void OnGoToLineClick(object? sender, RoutedEventArgs e)
        => await ShowGoToLineAsync();

    private void OnHideFindReplaceClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.IsFindReplaceVisible = false;
        _viewModel.IsReplaceVisible = false;
        _viewModel.FindSummary = string.Empty;
        EditorTextBox?.Focus();
    }

    private void ShowFind(bool replace)
    {
        _viewModel.IsFindReplaceVisible = true;
        _viewModel.IsReplaceVisible = replace;

        // Pre-fill find with current selection if any.
        if (EditorTextBox is not null && GetSelectionEnd() > EditorTextBox.SelectionStart)
        {
            var selected = EditorTextBox.SelectedText;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _viewModel.FindText = selected;
            }
        }

        UpdateFindSummary();
        (FindTextBox as Control)?.Focus();
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
        => FindNext(forward: true);

    private void OnFindPrevClick(object? sender, RoutedEventArgs e)
        => FindNext(forward: false);

    private void FindNext(bool forward)
    {
        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query) || EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        var (rangeStart, rangeEnd) = GetSearchRange(text);
        if (rangeStart >= rangeEnd)
        {
            return;
        }

        var startIndex = GetSearchStartIndex(forward);
        startIndex = Math.Clamp(startIndex, rangeStart, rangeEnd);

        var match = FindMatchInRange(
            text,
            query,
            startIndex,
            forward,
            _viewModel.MatchCase,
            _viewModel.WholeWord,
            _viewModel.UseRegex,
            _viewModel.WrapAround,
            rangeStart,
            rangeEnd);
        if (match is null)
        {
            UpdateFindSummary();
            return;
        }

        SelectMatch(match.Value.index, match.Value.length);
        UpdateFindSummary();
    }

    private (int start, int end) GetSearchRange(string text)
    {
        if (!_viewModel.InSelection || EditorTextBox is null)
        {
            return (0, text.Length);
        }

        var selectionEnd = GetSelectionEnd();
        var start = Math.Min(EditorTextBox.SelectionStart, selectionEnd);
        var end = Math.Max(EditorTextBox.SelectionStart, selectionEnd);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        return (start, end);
    }

    private static (int index, int length)? FindMatchInRange(
        string fullText,
        string query,
        int startIndex,
        bool forward,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        bool wrapAround,
        int rangeStart,
        int rangeEnd)
    {
        rangeStart = Math.Clamp(rangeStart, 0, fullText.Length);
        rangeEnd = Math.Clamp(rangeEnd, 0, fullText.Length);
        if (rangeEnd <= rangeStart)
        {
            return null;
        }

        if (rangeStart == 0 && rangeEnd == fullText.Length)
        {
            return FindMatch(fullText, query, startIndex, forward, matchCase, wholeWord, useRegex, wrapAround);
        }

        var segment = fullText.Substring(rangeStart, rangeEnd - rangeStart);
        var segStartIndex = Math.Clamp(startIndex - rangeStart, 0, segment.Length);

        var match = FindMatch(segment, query, segStartIndex, forward, matchCase, wholeWord, useRegex, wrapAround);
        return match is null ? null : (match.Value.index + rangeStart, match.Value.length);
    }

    private int GetSearchStartIndex(bool forward)
    {
        if (EditorTextBox is null)
        {
            return 0;
        }

        if (forward)
        {
            var start = Math.Max(EditorTextBox.SelectionStart, 0);
            if (GetSelectionEnd() > EditorTextBox.SelectionStart)
            {
                start = GetSelectionEnd();
            }

            return start;
        }

        var s = Math.Max(EditorTextBox.SelectionStart, 0);
        if (s > 0)
        {
            s--;
        }

        return s;
    }

    private static (int index, int length)? FindMatch(
        string text,
        string query,
        int startIndex,
        bool forward,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        bool wrapAround)
    {
        if (useRegex)
        {
            var regex = TryCreateRegex(query, matchCase, wholeWord);
            if (regex is null)
            {
                return null;
            }

            return forward
                ? FindNextRegex(text, regex, startIndex, wrapAround)
                : FindPrevRegex(text, regex, startIndex, wrapAround);
        }

        return forward
            ? FindNextPlain(text, query, startIndex, matchCase, wholeWord, wrapAround)
            : FindPrevPlain(text, query, startIndex, matchCase, wholeWord, wrapAround);
    }

    private static Regex? TryCreateRegex(string pattern, bool matchCase, bool wholeWord)
    {
        try
        {
            if (wholeWord)
            {
                pattern = $"(?<!\\w)(?:{pattern})(?!\\w)";
            }

            var options = RegexOptions.Compiled;
            if (!matchCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            return new Regex(pattern, options);
        }
        catch
        {
            return null;
        }
    }

    private static (int index, int length)? FindNextRegex(string text, Regex regex, int startIndex, bool wrapAround)
    {
        startIndex = Math.Clamp(startIndex, 0, text.Length);

        var m = regex.Match(text, startIndex);
        if (m.Success)
        {
            return (m.Index, m.Length);
        }

        if (!wrapAround)
        {
            return null;
        }

        m = regex.Match(text, 0);
        return m.Success ? (m.Index, m.Length) : null;
    }

    private static (int index, int length)? FindPrevRegex(string text, Regex regex, int startIndex, bool wrapAround)
    {
        startIndex = Math.Clamp(startIndex, 0, Math.Max(0, text.Length - 1));

        Match? last = null;
        foreach (Match m in regex.Matches(text))
        {
            if (!m.Success)
            {
                continue;
            }

            if (m.Index > startIndex)
            {
                break;
            }

            last = m;
        }

        if (last is not null)
        {
            return (last.Index, last.Length);
        }

        if (!wrapAround)
        {
            return null;
        }

        // Wrap: take the last match in the document.
        Match? final = null;
        foreach (Match m in regex.Matches(text))
        {
            if (m.Success)
            {
                final = m;
            }
        }

        return final is null ? null : (final.Index, final.Length);
    }

    private static (int index, int length)? FindNextPlain(string text, string query, int startIndex, bool matchCase, bool wholeWord, bool wrapAround)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        startIndex = Math.Clamp(startIndex, 0, text.Length);

        var idx = startIndex;
        while (idx <= text.Length)
        {
            var next = text.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, next, query.Length))
            {
                return (next, query.Length);
            }

            idx = next + 1;
        }

        if (!wrapAround)
        {
            return null;
        }

        idx = 0;
        while (idx < startIndex)
        {
            var next = text.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, next, query.Length))
            {
                return (next, query.Length);
            }

            idx = next + 1;
        }

        return null;
    }

    private static (int index, int length)? FindPrevPlain(string text, string query, int startIndex, bool matchCase, bool wholeWord, bool wrapAround)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (text.Length == 0)
        {
            return null;
        }

        startIndex = Math.Clamp(startIndex, 0, text.Length - 1);

        var idx = startIndex;
        while (idx >= 0)
        {
            var prev = text.LastIndexOf(query, idx, comparison);
            if (prev < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, prev, query.Length))
            {
                return (prev, query.Length);
            }

            idx = prev - 1;
        }

        if (!wrapAround)
        {
            return null;
        }

        idx = text.Length - 1;
        while (idx > startIndex)
        {
            var prev = text.LastIndexOf(query, idx, comparison);
            if (prev < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(text, prev, query.Length))
            {
                return (prev, query.Length);
            }

            idx = prev - 1;
        }

        return null;
    }

    private static bool IsWholeWordAt(string text, int index, int length)
    {
        var leftOk = index == 0 || !IsWordChar(text[index - 1]);
        var rightIndex = index + length;
        var rightOk = rightIndex >= text.Length || !IsWordChar(text[rightIndex]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private void SelectMatch(int index, int length)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        EditorTextBox.Focus();
        SetSelection(index, index + length);
        EditorTextBox.CaretOffset = index + length;
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
        => ReplaceOnce();

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
        => ReplaceAll();

    private void OnSetLineEndingLfClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.Lf;
    }

    private void OnSetLineEndingCrLfClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.CrLf;
    }

    private void OnSetLineEndingCrClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.PreferredLineEnding = LineEnding.Cr;
    }

    private void OnSetEncodingUtf8Click(object? sender, RoutedEventArgs e)
        => SetEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), hasBom: false);

    private void OnSetEncodingUtf8BomClick(object? sender, RoutedEventArgs e)
        => SetEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), hasBom: true);

    private void OnSetEncodingUtf16LeClick(object? sender, RoutedEventArgs e)
        => SetEncoding(Encoding.Unicode, hasBom: true);

    private void OnSetEncodingUtf16BeClick(object? sender, RoutedEventArgs e)
        => SetEncoding(Encoding.BigEndianUnicode, hasBom: true);

    private void OnLanguageModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingLanguageModeSelector || LanguageModeComboBox is null)
        {
            return;
        }

        if (LanguageModeComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        var selectedMode = selectedItem.Content?.ToString();
        if (string.IsNullOrWhiteSpace(selectedMode))
        {
            return;
        }

        SetLanguageMode(selectedMode);
    }

    private void OnSetLanguageAutoClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("Auto");

    private void OnSetLanguagePlainTextClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("Plain Text");

    private void OnSetLanguageCSharpClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("C#");

    private void OnSetLanguageJsonClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("JSON");

    private void OnSetLanguageXmlClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("XML");

    private void OnSetLanguageYamlClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("YAML");

    private void OnSetLanguageMarkdownClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("Markdown");

    private void OnSetLanguageJavaScriptClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("JavaScript");

    private void OnSetLanguageTypeScriptClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("TypeScript");

    private void OnSetLanguagePythonClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("Python");

    private void OnSetLanguageSqlClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("SQL");

    private void OnSetLanguageHtmlClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("HTML");

    private void OnSetLanguageCssClick(object? sender, RoutedEventArgs e)
        => SetLanguageMode("CSS");

    private void SetLanguageMode(string mode)
    {
        _languageMode = NormalizeLanguageMode(mode);
        UpdateLanguageModeSelector();
        ApplyLanguageStyling();
    }

    private string NormalizeLanguageMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Auto";
        }

        var candidate = mode.Trim();
        return LanguageModes.Any(item => string.Equals(item, candidate, StringComparison.Ordinal))
            ? candidate
            : "Auto";
    }

    private void UpdateLanguageModeSelector()
    {
        if (LanguageModeComboBox is null)
        {
            return;
        }

        _isUpdatingLanguageModeSelector = true;
        try
        {
            var selected = NormalizeLanguageMode(_languageMode);
            var comboItem = LanguageModeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), selected, StringComparison.Ordinal));

            LanguageModeComboBox.SelectedItem = comboItem ?? LanguageModeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _isUpdatingLanguageModeSelector = false;
        }
    }

    private void ApplyLanguageStyling()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var sourceText = string.IsNullOrWhiteSpace(EditorTextBox.Text)
            ? _viewModel.SelectedDocument?.Text
            : EditorTextBox.Text;

        var resolved = _languageMode == "Auto"
            ? DetectLanguage(_viewModel.SelectedDocument?.FilePath, sourceText)
            : _languageMode;

        _viewModel.StatusLanguage = resolved;
        EditorTextBox.SyntaxHighlighting = ResolveHighlightingDefinition(resolved);

        if (SplitEditorTextBox is not null)
        {
            var splitText = string.IsNullOrWhiteSpace(SplitEditorTextBox.Text)
                ? _splitDocument?.Text
                : SplitEditorTextBox.Text;

            var splitResolved = _languageMode == "Auto"
                ? DetectLanguage(_splitDocument?.FilePath, splitText)
                : _languageMode;

            SplitEditorTextBox.SyntaxHighlighting = ResolveHighlightingDefinition(splitResolved);
        }
    }

    private static string DetectLanguage(string? filePath, string? text)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".cs" or ".csx" => "C#",
                ".json" => "JSON",
                ".xml" or ".xaml" => "XML",
                ".yaml" or ".yml" => "YAML",
                ".md" or ".markdown" => "Markdown",
                ".js" or ".mjs" or ".cjs" => "JavaScript",
                ".ts" or ".tsx" => "TypeScript",
                ".py" => "Python",
                ".sql" => "SQL",
                ".html" or ".htm" => "HTML",
                ".css" => "CSS",
                _ => "Plain Text",
            };
        }

        // Heuristic for untitled buffers so starter/sample code gets a sensible language.
        var sample = text ?? string.Empty;
        if (sample.Contains("using System;", StringComparison.Ordinal)
            || sample.Contains("public static void Main", StringComparison.Ordinal))
        {
            return "C#";
        }

        return "Plain Text";
    }

    private IHighlightingDefinition? ResolveHighlightingDefinition(string language)
    {
        var extension = language switch
        {
            "C#" => ".cs",
            "JSON" => ".json",
            "XML" => ".xml",
            "YAML" => ".yml",
            "Markdown" => ".md",
            "JavaScript" => ".js",
            "TypeScript" => ".js",
            "Python" => ".py",
            "SQL" => ".sql",
            "HTML" => ".html",
            "CSS" => ".css",
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        var definition = HighlightingManager.Instance.GetDefinitionByExtension(extension);
        if (definition is null)
        {
            return null;
        }

        if (_themedHighlightDefinitions.Add(definition.Name))
        {
            ApplyVsCodeDarkPalette(definition);
        }

        return definition;
    }

    private void ApplyVsCodeDarkPalette(IHighlightingDefinition definition)
    {
        foreach (var color in definition.NamedHighlightingColors)
        {
            var hex = ResolveThemeTokenColor(color.Name);
            color.Foreground = new SimpleHighlightingBrush(Color.Parse(hex));
            color.Background = null;
        }
    }

    private string ResolveThemeTokenColor(string? tokenName)
    {
        var n = (tokenName ?? string.Empty).ToLowerInvariant();
        var comment = _themeMode switch
        {
            "Light" => "#008000",
            "Monokai" => "#75715E",
            "One Dark" => "#5C6370",
            _ => "#6A9955",
        };
        var str = _themeMode switch
        {
            "Light" => "#A31515",
            "Monokai" => "#E6DB74",
            "One Dark" => "#98C379",
            _ => "#CE9178",
        };
        var num = _themeMode switch
        {
            "Light" => "#098658",
            "Monokai" => "#AE81FF",
            "One Dark" => "#D19A66",
            _ => "#B5CEA8",
        };
        var keyword = _themeMode switch
        {
            "Light" => "#0000FF",
            "Monokai" => "#F92672",
            "One Dark" => "#C678DD",
            _ => "#C586C0",
        };
        var type = _themeMode switch
        {
            "Light" => "#267F99",
            "Monokai" => "#66D9EF",
            "One Dark" => "#E5C07B",
            _ => "#4EC9B0",
        };
        var method = _themeMode switch
        {
            "Light" => "#795E26",
            "Monokai" => "#A6E22E",
            "One Dark" => "#61AFEF",
            _ => "#DCDCAA",
        };
        var prop = _themeMode switch
        {
            "Light" => "#001080",
            "Monokai" => "#66D9EF",
            "One Dark" => "#56B6C2",
            _ => "#9CDCFE",
        };
        var constant = _themeMode switch
        {
            "Light" => "#0000FF",
            "Monokai" => "#FD971F",
            "One Dark" => "#E06C75",
            _ => "#569CD6",
        };
        var normal = _themeMode switch
        {
            "Light" => "#1F2933",
            "Monokai" => "#F8F8F2",
            "One Dark" => "#ABB2BF",
            _ => "#D4D4D4",
        };

        if (n.Contains("comment"))
        {
            return comment;
        }

        if (n.Contains("string") || n.Contains("char") || n.Contains("regex"))
        {
            return str;
        }

        if (n.Contains("number") || n.Contains("digit") || n.Contains("hex"))
        {
            return num;
        }

        if (n.Contains("preprocessor") || n.Contains("directive") || n.Contains("keyword"))
        {
            return keyword;
        }

        if (n.Contains("class")
            || n.Contains("interface")
            || n.Contains("enum")
            || n.Contains("struct")
            || n.Contains("type"))
        {
            return type;
        }

        if (n.Contains("method") || n.Contains("function") || n.Contains("call"))
        {
            return method;
        }

        if (n.Contains("tag")
            || n.Contains("attribute")
            || n.Contains("property")
            || n.Contains("field")
            || n.Contains("xml")
            || n.Contains("html")
            || n.Contains("css"))
        {
            return prop;
        }

        if (n.Contains("constant") || n.Contains("literal") || n.Contains("bool"))
        {
            return constant;
        }

        if (n.Contains("operator") || n.Contains("punctuation"))
        {
            return normal;
        }

        return normal;
    }

    private void OnFormatDocumentClick(object? sender, RoutedEventArgs e)
        => FormatDocument(selectionOnly: false);

    private void OnFormatSelectionClick(object? sender, RoutedEventArgs e)
        => FormatDocument(selectionOnly: true);

    private void FormatDocument(bool selectionOnly)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var start = 0;
        var end = text.Length;
        if (selectionOnly)
        {
            start = Math.Min(EditorTextBox.SelectionStart, GetSelectionEnd());
            end = Math.Max(EditorTextBox.SelectionStart, GetSelectionEnd());
            if (end <= start)
            {
                return;
            }
        }

        var prefix = text.Substring(0, start);
        var segment = text.Substring(start, end - start);
        var suffix = text.Substring(end);

        var language = _viewModel.StatusLanguage;
        var formatted = TryFormatByLanguage(segment, language);
        if (formatted is null || string.Equals(formatted, segment, StringComparison.Ordinal))
        {
            return;
        }

        EditorTextBox.Text = prefix + formatted + suffix;
        if (selectionOnly)
        {
            SetSelection(start, start + formatted.Length);
        }
        else
        {
            EditorTextBox.CaretOffset = 0;
            EditorTextBox.ScrollToHome();
        }
    }

    private static string? TryFormatByLanguage(string text, string language)
    {
        try
        {
            return language switch
            {
                "JSON" => JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(text), new JsonSerializerOptions { WriteIndented = true }),
                "XML" or "HTML" => XDocument.Parse(text).ToString(),
                "YAML" => FormatYaml(text),
                "C#" => FormatCSharp(text),
                "JavaScript" or "TypeScript" or "CSS" or "SQL" => FormatBraceLanguage(text),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FormatCSharp(string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetRoot();
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var formattedRoot = Formatter.Format(root, workspace);
        return formattedRoot.ToFullString();
    }

    private static string FormatYaml(string text)
    {
        var deserializer = new DeserializerBuilder().Build();
        var value = deserializer.Deserialize<object?>(text);
        var serializer = new SerializerBuilder()
            .DisableAliases()
            .WithIndentedSequences()
            .Build();
        return serializer.Serialize(value).TrimEnd('\r', '\n');
    }

    private static string FormatBraceLanguage(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(text.Length + 64);
        var indent = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0)
            {
                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }
                continue;
            }

            if (raw.StartsWith("}", StringComparison.Ordinal) || raw.StartsWith("]", StringComparison.Ordinal))
            {
                indent = Math.Max(0, indent - 1);
            }

            sb.Append(new string(' ', indent * 4));
            sb.Append(raw);
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }

            if (raw.EndsWith("{", StringComparison.Ordinal) || raw.EndsWith("[", StringComparison.Ordinal))
            {
                indent++;
            }
        }

        return sb.ToString();
    }

    private void SetEncoding(Encoding encoding, bool hasBom)
    {
        if (_viewModel.SelectedDocument is null)
        {
            return;
        }

        _viewModel.SelectedDocument.Encoding = encoding;
        _viewModel.SelectedDocument.HasBom = hasBom;
    }

    private void ReplaceOnce()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var selection = EditorTextBox.SelectedText ?? string.Empty;
        var replacementRaw = _viewModel.ReplaceText ?? string.Empty;

        var selectionEnd = GetSelectionEnd();
        if (selectionEnd > EditorTextBox.SelectionStart && selection.Length > 0)
        {
            var start = EditorTextBox.SelectionStart;
            var end = selectionEnd;
            var text = EditorTextBox.Text ?? string.Empty;

            var replacement = GetReplacementIfSelectionMatches(text, query, replacementRaw, start, end - start);
            if (replacement is not null)
            {
                var newText = text.Substring(0, start) + replacement + text.Substring(end);
                EditorTextBox.Text = newText;
                SelectMatch(start, replacement.Length);
                FindNext(forward: true);
                return;
            }
        }

        FindNext(forward: true);
    }

    private string? GetReplacementIfSelectionMatches(string text, string query, string replacementRaw, int selectionStart, int selectionLength)
    {
        if (_viewModel.UseRegex)
        {
            var regex = TryCreateRegex(query, _viewModel.MatchCase, _viewModel.WholeWord);
            if (regex is null)
            {
                return null;
            }

            var m = regex.Match(text, Math.Clamp(selectionStart, 0, text.Length));
            if (!m.Success || m.Index != selectionStart || m.Length != selectionLength)
            {
                return null;
            }

            try
            {
                return m.Result(replacementRaw);
            }
            catch
            {
                return replacementRaw;
            }
        }

        if (selectionStart < 0 || selectionStart + selectionLength > text.Length)
        {
            return null;
        }

        var selected = text.Substring(selectionStart, selectionLength);
        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!string.Equals(selected, query, comparison))
        {
            return null;
        }

        if (_viewModel.WholeWord && !IsWholeWordAt(text, selectionStart, selectionLength))
        {
            return null;
        }

        return replacementRaw;
    }

    private void ReplaceAll()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var replacement = _viewModel.ReplaceText ?? string.Empty;
        var text = EditorTextBox.Text ?? string.Empty;

        var (rangeStart, rangeEnd) = GetSearchRange(text);
        if (rangeStart >= rangeEnd)
        {
            return;
        }

        var prefix = rangeStart == 0 ? string.Empty : text.Substring(0, rangeStart);
        var segment = text.Substring(rangeStart, rangeEnd - rangeStart);
        var suffix = rangeEnd >= text.Length ? string.Empty : text.Substring(rangeEnd);

        if (_viewModel.UseRegex)
        {
            var regex = TryCreateRegex(query, _viewModel.MatchCase, _viewModel.WholeWord);
            if (regex is null)
            {
                return;
            }

            try
            {
                segment = regex.Replace(segment, replacement);
                EditorTextBox.Text = prefix + segment + suffix;
            }
            catch
            {
                // Ignore invalid replacement.
            }

            return;
        }

        var comparison = _viewModel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var idx = 0;
        var result = new System.Text.StringBuilder(segment.Length);
        while (idx < segment.Length)
        {
            var next = segment.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                result.Append(segment, idx, segment.Length - idx);
                break;
            }

            if (_viewModel.WholeWord && !IsWholeWordAt(segment, next, query.Length))
            {
                result.Append(segment, idx, (next - idx) + 1);
                idx = next + 1;
                continue;
            }

            result.Append(segment, idx, next - idx);
            result.Append(replacement);
            idx = next + query.Length;
        }

        EditorTextBox.Text = prefix + result + suffix;
    }

    private async Task ShowGoToLineAsync()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var currentLine = GetCaretLineNumber(EditorTextBox.Text ?? string.Empty, EditorTextBox.CaretOffset);
        var dialog = new GoToLineDialog(currentLine);
        var raw = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var (line, col) = ParseLineColumn(raw);
        if (line <= 0)
        {
            return;
        }

        GoToLine(EditorTextBox, line, col);
    }

    private static (int line, int? column) ParseLineColumn(string raw)
    {
        raw = raw.Trim();
        var parts = raw.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (0, null);
        }

        if (!int.TryParse(parts[0], out var line) || line <= 0)
        {
            return (0, null);
        }

        if (parts.Length >= 2 && int.TryParse(parts[1], out var col) && col > 0)
        {
            return (line, col);
        }

        return (line, null);
    }

    private static int GetCaretLineNumber(string text, int caretIndex)
    {
        if (caretIndex < 0)
        {
            caretIndex = 0;
        }

        if (caretIndex > text.Length)
        {
            caretIndex = text.Length;
        }

        var line = 1;
        for (var i = 0; i < caretIndex && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static void GoToLine(TextEditor editor, int lineNumber, int? columnNumber)
    {
        if (lineNumber <= 0)
        {
            return;
        }

        var text = editor.Text ?? string.Empty;
        // Our Core normalizes to \n; editor text follows that.
        var targetLine = 1;
        var index = 0;

        while (targetLine < lineNumber && index < text.Length)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0)
            {
                // Requested line past end; clamp to last line start.
                break;
            }

            index = next + 1;
            targetLine++;
        }

        if (columnNumber is not null && columnNumber.Value > 1)
        {
            var col = columnNumber.Value;
            var lineEnd = text.IndexOf('\n', index);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            // Clamp within the line.
            index = Math.Min(index + (col - 1), lineEnd);
        }

        editor.Focus();
        editor.SelectionStart = index;
        editor.SelectionLength = 0;
        editor.CaretOffset = index;
    }

    private void UpdateCaretStatus()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = EditorTextBox.CaretOffset;
        var (line, col) = GetLineColumn(text, caret);
        var selectionLength = Math.Max(0, EditorTextBox.SelectionLength);
        _viewModel.SetCaretPosition(line, col, selectionLength);
        UpdateFindSummary();
    }

    private void UpdateFindSummary()
    {
        if (!_viewModel.IsFindReplaceVisible || EditorTextBox is null)
        {
            _viewModel.FindSummary = string.Empty;
            return;
        }

        var query = _viewModel.FindText;
        if (string.IsNullOrEmpty(query))
        {
            _viewModel.FindSummary = "Type to search";
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var (rangeStart, rangeEnd) = GetSearchRange(text);
        var total = CountMatchesInRange(
            text,
            query,
            _viewModel.MatchCase,
            _viewModel.WholeWord,
            _viewModel.UseRegex,
            rangeStart,
            rangeEnd);

        if (total <= 0)
        {
            _viewModel.FindSummary = "No matches";
            return;
        }

        var scopeSuffix = _viewModel.InSelection ? " in selection" : string.Empty;
        _viewModel.FindSummary = total == 1
            ? $"1 match{scopeSuffix}"
            : $"{total} matches{scopeSuffix}";
    }

    private static int CountMatchesInRange(
        string fullText,
        string query,
        bool matchCase,
        bool wholeWord,
        bool useRegex,
        int rangeStart,
        int rangeEnd)
    {
        rangeStart = Math.Clamp(rangeStart, 0, fullText.Length);
        rangeEnd = Math.Clamp(rangeEnd, 0, fullText.Length);
        if (rangeEnd <= rangeStart)
        {
            return 0;
        }

        var segment = fullText.Substring(rangeStart, rangeEnd - rangeStart);
        if (segment.Length == 0)
        {
            return 0;
        }

        if (useRegex)
        {
            var regex = TryCreateRegex(query, matchCase, wholeWord);
            if (regex is null)
            {
                return 0;
            }

            var count = 0;
            foreach (Match m in regex.Matches(segment))
            {
                if (m.Success && m.Length > 0)
                {
                    count++;
                }
            }

            return count;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var idx = 0;
        var total = 0;
        while (idx <= segment.Length)
        {
            var next = segment.IndexOf(query, idx, comparison);
            if (next < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordAt(segment, next, query.Length))
            {
                total++;
            }

            idx = next + 1;
        }

        return total;
    }

    private static (int line, int column) GetLineColumn(string text, int caretIndex)
    {
        if (caretIndex < 0) caretIndex = 0;
        if (caretIndex > text.Length) caretIndex = text.Length;

        var line = 1;
        var col = 1;

        for (var i = 0; i < caretIndex; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }

        return (line, col);
    }
}

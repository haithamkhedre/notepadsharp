using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Reflection;
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
using AvaloniaEdit.Rendering;
using Material.Icons;
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
    private readonly TranslateTransform _gitDiffTransform = new(0, 0);
    private const int DefaultColumnGuide = 100;
    private const double DefaultEditorFontSize = 16;
    private const double MinEditorFontSize = 8;
    private const double MaxEditorFontSize = 48;
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
    private static readonly string[] EditorFontFamilies =
    {
        "Consolas",
        "Cascadia Mono",
        "JetBrains Mono",
        "Fira Code",
        "Menlo",
        "Monaco",
        "Courier New",
        "Source Code Pro",
    };
    private static readonly string[] SidebarSections =
    {
        "Explorer",
        "Search",
        "Source Control",
        "Diagnostics",
        "Settings",
    };
    private static readonly HashSet<string> IgnoredWorkspaceDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "node_modules",
        "bin",
        "obj",
    };
    private static readonly HashSet<string> SearchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".cs", ".csx", ".json", ".xml", ".xaml", ".yaml", ".yml",
        ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".py", ".sql", ".html", ".htm", ".css",
        ".scss", ".sass", ".less", ".toml", ".ini", ".config", ".props", ".targets", ".csproj",
        ".sln", ".sh", ".ps1", ".cmd", ".bat", ".dockerfile", ".env",
    };
    private static readonly string[] CSharpCompletionKeywords =
    {
        "class", "namespace", "using", "public", "private", "protected", "internal", "static", "readonly",
        "const", "void", "string", "int", "var", "bool", "if", "else", "switch", "case", "for", "foreach",
        "while", "do", "return", "break", "continue", "try", "catch", "finally", "throw", "new", "this",
        "base", "async", "await", "Task", "List", "Dictionary", "IEnumerable", "where", "select", "from",
    };
    private static readonly string[] JavaScriptCompletionKeywords =
    {
        "const", "let", "var", "function", "class", "import", "export", "default", "return",
        "if", "else", "switch", "case", "for", "while", "try", "catch", "finally", "throw",
        "async", "await", "Promise", "console", "document", "window", "Array", "Object", "Map", "Set",
    };
    private static readonly string[] TypeScriptCompletionKeywords =
    {
        "interface", "type", "enum", "implements", "extends", "readonly", "public", "private", "protected",
        "namespace", "declare", "keyof", "unknown", "never", "as", "infer", "satisfies", "const", "let",
        "class", "async", "await", "Promise", "import", "export",
    };
    private static readonly string[] PythonCompletionKeywords =
    {
        "def", "class", "import", "from", "as", "if", "elif", "else", "for", "while", "try", "except",
        "finally", "with", "lambda", "yield", "return", "async", "await", "pass", "break", "continue",
        "self", "None", "True", "False", "list", "dict", "set", "tuple",
    };
    private static readonly string[] SqlCompletionKeywords =
    {
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "GROUP BY", "ORDER BY",
        "HAVING", "INSERT", "UPDATE", "DELETE", "CREATE", "ALTER", "DROP", "UNION", "LIMIT",
    };
    private static readonly Regex GitHunkHeaderRegex = new(
        @"^@@ -(?<oldStart>\d+)(,(?<oldCount>\d+))? \+(?<newStart>\d+)(,(?<newCount>\d+))? @@",
        RegexOptions.Compiled);
    private readonly Stack<ClosedTabSnapshot> _closedTabs = new();
    private string _languageMode = "Auto";
    private string _themeMode = "Dark+";
    private string _editorFontFamily = "Consolas";
    private string? _workspaceRoot;
    private string _sidebarSection = "Explorer";
    private bool _isSidebarAutoHide;
    private bool _isSidebarExpanded = true;
    private bool _isColumnGuideEnabled = true;
    private int _columnGuideColumn = DefaultColumnGuide;
    private bool _isMiniMapEnabled = true;
    private bool _isSplitViewEnabled;
    private bool _isFoldingEnabled = true;
    private bool _showAllCharacters = true;
    private bool _isSyncingEditorText;
    private bool _isSyncingSplitEditorText;
    private readonly HashSet<string> _themedHighlightDefinitions = new(StringComparer.Ordinal);
    private readonly List<int> _miniMapLineMap = new();
    private IReadOnlyList<char> _miniMapDiffMarkers = Array.Empty<char>();
    private readonly Dictionary<string, Action> _commandPaletteActions = new(StringComparer.Ordinal);
    private readonly List<ExplorerTreeNode> _explorerRootNodes = new();
    private readonly List<SearchResultItem> _searchResultItems = new();
    private readonly List<SearchTreeNode> _searchTreeRootNodes = new();
    private readonly List<GitChangeEntryModel> _gitChanges = new();
    private readonly List<GitChangeTreeNode> _gitChangeTreeRootNodes = new();
    private readonly Dictionary<string, string> _gitStatusByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DiagnosticEntry> _diagnosticEntries = new();
    private FoldingManager? _foldingManager;
    private TextDocument? _splitDocument;
    private bool _isUpdatingLanguageModeSelector;
    private bool _isUpdatingThemeModeSelector;
    private bool _isUpdatingEditorTypographySelectors;
    private bool _isUpdatingSettingsControls;
    private bool _isUpdatingWhitespaceToggleControls;
    private bool _isTerminalVisible;
    private bool _isTerminalBusy;
    private double _sidebarWidth = 340;
    private double _terminalHeight = 180;
    private bool _showTabBar = true;
    private bool _autoHideTabBar;
    private DateTimeOffset _lastExternalDiagnosticsRunUtc = DateTimeOffset.MinValue;
    private bool _isApplyingAutoFormat;
    private GitDiffLineColorizer? _gitDiffLineColorizer;

    private sealed record ClosedTabSnapshot(
        string? FilePath,
        string Text,
        string EncodingWebName,
        bool HasBom,
        LineEnding PreferredLineEnding,
        bool WordWrap,
        bool WasDirty,
        DateTimeOffset? FileLastWriteTimeUtc);

    private sealed record SearchResultItem(string FilePath, string RelativePath, int Line, int Column, int Length, string Preview)
    {
        public override string ToString()
            => $"{RelativePath}:{Line}:{Column}  {Preview}";
    }

    private sealed record DiagnosticEntry(string Severity, int Line, int Column, string Message)
    {
        public override string ToString()
            => $"{Severity} L{Line},C{Column}  {Message}";
    }

    private sealed class GitDiffLineColorizer : DocumentColorizingTransformer
    {
        private readonly Dictionary<int, char> _lineKinds = new();
        private IBrush _addedBrush = new SolidColorBrush(Color.Parse("#2E7D3233"));
        private IBrush _modifiedBrush = new SolidColorBrush(Color.Parse("#D2992233"));
        private IBrush _deletedBrush = new SolidColorBrush(Color.Parse("#F8514933"));

        public void SetMarkers(IReadOnlyList<char> markers)
        {
            _lineKinds.Clear();
            for (var i = 0; i < markers.Count; i++)
            {
                var kind = markers[i];
                if (kind is '+' or '~' or '-')
                {
                    _lineKinds[i + 1] = kind;
                }
            }
        }

        public void Clear()
            => _lineKinds.Clear();

        public void SetTheme(string themeMode)
        {
            var isLight = string.Equals(themeMode, "Light", StringComparison.Ordinal);
            if (isLight)
            {
                _addedBrush = new SolidColorBrush(Color.Parse("#6FCF9730"));
                _modifiedBrush = new SolidColorBrush(Color.Parse("#F2C94C28"));
                _deletedBrush = new SolidColorBrush(Color.Parse("#F2999928"));
                return;
            }

            _addedBrush = new SolidColorBrush(Color.Parse("#2E7D3233"));
            _modifiedBrush = new SolidColorBrush(Color.Parse("#D2992233"));
            _deletedBrush = new SolidColorBrush(Color.Parse("#F8514933"));
        }

        protected override void ColorizeLine(AvaloniaEdit.Document.DocumentLine line)
        {
            if (!_lineKinds.TryGetValue(line.LineNumber, out var kind))
            {
                return;
            }

            var brush = kind switch
            {
                '+' => _addedBrush,
                '-' => _deletedBrush,
                '~' => _modifiedBrush,
                _ => (IBrush?)null,
            };

            if (brush is null)
            {
                return;
            }

            ChangeLinePart(line.Offset, line.EndOffset, element =>
            {
                element.TextRunProperties.SetBackgroundBrush(brush);
            });
        }
    }

    public MainWindow()
    {
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        InitializeComponent();

        _recoveryManager = new RecoveryManager(_recoveryStore);
        InitializeCommandPaletteActions();

        if (EditorTextBox is not null)
        {
            ConfigureEditor(EditorTextBox);
            EditorTextBox.TextChanged += OnPrimaryEditorTextChanged;
            EditorTextBox.TextArea.Caret.PositionChanged += (_, __) => UpdateCaretStatus();
            _foldingManager = FoldingManager.Install(EditorTextBox.TextArea);
            _gitDiffLineColorizer = new GitDiffLineColorizer();
            _gitDiffLineColorizer.SetTheme(_themeMode);
            EditorTextBox.TextArea.TextView.LineTransformers.Add(_gitDiffLineColorizer);
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
        if (GitDiffGutterTextBlock is not null)
        {
            GitDiffGutterTextBlock.RenderTransform = _gitDiffTransform;
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
                TryAutoFormatCurrentDocument();
                EnsureWorkspaceRoot();
                RefreshExplorer();
                UpdateGitPanel();
                UpdateDiagnostics();
                UpdateSettingsControls();
                RefreshSplitEditorTitle();
                UpdateMiniMap();
                UpdateFolding();
                UpdateGitDiffGutter();
                UpdateTabStripVisibility();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.EditorFontSize))
            {
                UpdateColumnGuide();
                UpdateLineNumbers();
                UpdateEditorTypographySelectors();
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
        _showAllCharacters = _state.ShowAllCharacters;
        _themeMode = NormalizeThemeMode(_state.Theme);
        _languageMode = NormalizeLanguageMode(_state.LanguageMode);
        _workspaceRoot = NormalizeWorkspaceRoot(_state.WorkspaceRoot);
        _sidebarSection = NormalizeSidebarSection(_state.SidebarSection);
        _isSidebarAutoHide = _state.SidebarAutoHide;
        _isSidebarExpanded = _isSidebarAutoHide ? _state.SidebarExpanded : true;
        _sidebarWidth = _state.SidebarWidth > 180 ? _state.SidebarWidth : 340;
        _isTerminalVisible = _state.TerminalVisible;
        _terminalHeight = _state.TerminalHeight > 110 ? _state.TerminalHeight : 180;
        _showTabBar = _state.ShowTabBar;
        _autoHideTabBar = _state.AutoHideTabBar;
        var persistedFontSize = _state.EditorFontSize <= 0 ? DefaultEditorFontSize : _state.EditorFontSize;
        SetEditorFontSize(persistedFontSize, persist: false);
        _editorFontFamily = NormalizeEditorFontFamily(_state.EditorFontFamily);
        ApplyEditorTypography();
        ApplyWhitespaceOptions();
        UpdateThemeModeSelector();
        UpdateEditorTypographySelectors();
        UpdateLanguageModeSelector();
        UpdateSettingsControls();
        UpdateSidebarSectionUI();
        UpdateSidebarLayout();
        UpdateTerminalLayout();
        UpdateTabStripVisibility();

        RefreshOpenRecentMenu();
        _viewModel.RecentFiles.CollectionChanged += (_, __) => RefreshOpenRecentMenu();
        _viewModel.Documents.CollectionChanged += (_, __) => UpdateTabStripVisibility();

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
            EnsureWorkspaceRoot();
            RefreshExplorer();
            UpdateDiagnostics();
            UpdateSettingsControls();
            UpdateSidebarSectionUI();
            UpdateSidebarLayout();
            RefreshSplitEditorTitle();
            UpdateMiniMap();
            UpdateFolding();
            UpdateGitPanel();
            UpdateGitDiffGutter();
            UpdateTerminalCwd();
            UpdateTerminalMenuChecks();
            UpdateTabStripVisibility();
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
        ApplyWhitespaceOptions(editor);
        editor.TextArea.TextEntered += OnEditorTextEntered;
        editor.AddHandler(InputElement.PointerWheelChangedEvent, OnEditorPointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        Gestures.AddPointerTouchPadGestureMagnifyHandler(editor, OnEditorTouchPadMagnify);
        Gestures.AddPinchHandler(editor, OnEditorPinch);
    }

    private static string NormalizeSidebarSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return "Explorer";
        }

        var candidate = section.Trim();
        return SidebarSections.Any(s => string.Equals(s, candidate, StringComparison.Ordinal))
            ? candidate
            : "Explorer";
    }

    private void ApplyWhitespaceOptions(TextEditor? editor = null)
    {
        if (editor is not null)
        {
            editor.Options.ShowSpaces = true;
            editor.Options.ShowTabs = true;
            editor.Options.ShowEndOfLine = _showAllCharacters;
            return;
        }

        ApplyWhitespaceOptions(EditorTextBox);
        ApplyWhitespaceOptions(SplitEditorTextBox);
    }

    private void OnSidebarExplorerClick(object? sender, RoutedEventArgs e)
        => SetSidebarSection("Explorer", persist: true);

    private void OnSidebarSearchClick(object? sender, RoutedEventArgs e)
        => SetSidebarSection("Search", persist: true);

    private void OnSidebarSourceControlClick(object? sender, RoutedEventArgs e)
        => SetSidebarSection("Source Control", persist: true);

    private void OnSidebarDiagnosticsClick(object? sender, RoutedEventArgs e)
        => SetSidebarSection("Diagnostics", persist: true);

    private void OnSidebarSettingsClick(object? sender, RoutedEventArgs e)
        => SetSidebarSection("Settings", persist: true);

    private void SetSidebarSection(string section, bool persist)
    {
        _sidebarSection = NormalizeSidebarSection(section);
        if (_isSidebarAutoHide)
        {
            _isSidebarExpanded = true;
        }

        UpdateSidebarSectionUI();
        UpdateSidebarLayout();
        if (string.Equals(_sidebarSection, "Source Control", StringComparison.Ordinal))
        {
            UpdateGitPanel();
        }
        if (persist)
        {
            PersistState();
        }
    }

    private void OnSidebarAutoHideToggleClick(object? sender, RoutedEventArgs e)
    {
        if (SidebarAutoHideToggleButton is null)
        {
            return;
        }

        _isSidebarAutoHide = SidebarAutoHideToggleButton.IsChecked == true;
        _isSidebarExpanded = !_isSidebarAutoHide;
        UpdateSidebarLayout();
        PersistState();
    }

    private void OnSidebarHostPointerEntered(object? sender, PointerEventArgs e)
    {
        if (!_isSidebarAutoHide)
        {
            return;
        }

        _isSidebarExpanded = true;
        UpdateSidebarLayout();
    }

    private void OnSidebarHostPointerExited(object? sender, PointerEventArgs e)
    {
        if (!_isSidebarAutoHide)
        {
            return;
        }

        _isSidebarExpanded = false;
        UpdateSidebarLayout();
    }

    private void UpdateSidebarSectionUI()
    {
        if (ExplorerPane is null || SearchPane is null || SourceControlPane is null || DiagnosticsPane is null || SettingsPane is null)
        {
            return;
        }

        ExplorerPane.IsVisible = string.Equals(_sidebarSection, "Explorer", StringComparison.Ordinal);
        SearchPane.IsVisible = string.Equals(_sidebarSection, "Search", StringComparison.Ordinal);
        SourceControlPane.IsVisible = string.Equals(_sidebarSection, "Source Control", StringComparison.Ordinal);
        DiagnosticsPane.IsVisible = string.Equals(_sidebarSection, "Diagnostics", StringComparison.Ordinal);
        SettingsPane.IsVisible = string.Equals(_sidebarSection, "Settings", StringComparison.Ordinal);

        if (SidebarExplorerButton is not null)
        {
            SidebarExplorerButton.IsChecked = ExplorerPane.IsVisible;
        }

        if (SidebarSearchButton is not null)
        {
            SidebarSearchButton.IsChecked = SearchPane.IsVisible;
        }

        if (SidebarSourceControlButton is not null)
        {
            SidebarSourceControlButton.IsChecked = SourceControlPane.IsVisible;
        }

        if (SidebarDiagnosticsButton is not null)
        {
            SidebarDiagnosticsButton.IsChecked = DiagnosticsPane.IsVisible;
        }

        if (SidebarSettingsButton is not null)
        {
            SidebarSettingsButton.IsChecked = SettingsPane.IsVisible;
        }
    }

    private void UpdateSidebarLayout()
    {
        if (EditorLayoutGrid is null || SidebarPaneHost is null)
        {
            return;
        }

        var expanded = !_isSidebarAutoHide || _isSidebarExpanded;
        SidebarPaneHost.IsVisible = expanded;
        if (SidebarAutoHideToggleButton is not null)
        {
            SidebarAutoHideToggleButton.IsChecked = _isSidebarAutoHide;
        }

        if (EditorLayoutGrid.ColumnDefinitions.Count > 1)
        {
            var width = expanded ? _sidebarWidth : 44;
            EditorLayoutGrid.ColumnDefinitions[0].Width = new GridLength(width, GridUnitType.Pixel);
            EditorLayoutGrid.ColumnDefinitions[1].Width = new GridLength(expanded ? 6 : 0, GridUnitType.Pixel);
        }

        if (SidebarWidthSplitter is not null)
        {
            SidebarWidthSplitter.IsVisible = expanded;
        }
    }

    private void UpdateTabStripVisibility()
    {
        if (DocumentTabs is null || ShowTabBarMenuItem is null || AutoHideTabBarMenuItem is null)
        {
            return;
        }

        var visible = _showTabBar && (!_autoHideTabBar || _viewModel.Documents.Count > 1);
        DocumentTabs.IsVisible = visible;
        ShowTabBarMenuItem.IsChecked = _showTabBar;
        AutoHideTabBarMenuItem.IsChecked = _autoHideTabBar;
    }

    private void UpdateTerminalLayout()
    {
        if (TerminalPane is null || TerminalHeightSplitter is null)
        {
            return;
        }

        TerminalPane.IsVisible = _isTerminalVisible;
        TerminalHeightSplitter.IsVisible = _isTerminalVisible;

        if (TerminalPane.Parent is Grid grid && grid.RowDefinitions.Count >= 3)
        {
            grid.RowDefinitions[1].Height = new GridLength(_isTerminalVisible ? 6 : 0, GridUnitType.Pixel);
            grid.RowDefinitions[2].Height = new GridLength(_isTerminalVisible ? _terminalHeight : 0, GridUnitType.Pixel);
        }

        UpdateTerminalMenuChecks();
        UpdateTerminalCwd();
    }

    private void UpdateTerminalMenuChecks()
    {
        if (TerminalMenuItem is not null)
        {
            TerminalMenuItem.IsChecked = _isTerminalVisible;
        }
    }

    private void UpdateTerminalCwd()
    {
        if (TerminalCwdTextBlock is null)
        {
            return;
        }

        var cwd = GetShellWorkingDirectory();
        TerminalCwdTextBlock.Text = string.IsNullOrWhiteSpace(cwd)
            ? string.Empty
            : $"cwd: {cwd}";
    }

    private static string? NormalizeWorkspaceRoot(string? rawRoot)
    {
        if (string.IsNullOrWhiteSpace(rawRoot))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(rawRoot);
            return Directory.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureWorkspaceRoot()
    {
        if (!string.IsNullOrWhiteSpace(_workspaceRoot) && Directory.Exists(_workspaceRoot))
        {
            UpdateWorkspaceRootLabel();
            return;
        }

        _workspaceRoot = NormalizeWorkspaceRoot(InferWorkspaceRootFromContext());
        UpdateWorkspaceRootLabel();
        UpdateTerminalCwd();
    }

    private string? InferWorkspaceRootFromContext()
    {
        var selectedPath = _viewModel.SelectedDocument?.FilePath;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            return Path.GetDirectoryName(selectedPath);
        }

        var firstPath = _viewModel.Documents
            .Select(d => d.FilePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        if (!string.IsNullOrWhiteSpace(firstPath))
        {
            return Path.GetDirectoryName(firstPath);
        }

        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch
        {
            return null;
        }
    }

    private void UpdateWorkspaceRootLabel()
    {
        if (WorkspaceRootTextBlock is null)
        {
            return;
        }

        WorkspaceRootTextBlock.Text = string.IsNullOrWhiteSpace(_workspaceRoot)
            ? "No workspace loaded."
            : _workspaceRoot!;
    }

    private async void OnOpenWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        var provider = StorageProvider;
        if (provider is null)
        {
            return;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Workspace Folder",
            AllowMultiple = false,
        });

        var folder = folders.FirstOrDefault();
        var local = folder?.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(local))
        {
            return;
        }

        SetWorkspaceRoot(local, persist: true);
    }

    private void OnRefreshWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        EnsureWorkspaceRoot();
        RefreshExplorer();
    }

    private void SetWorkspaceRoot(string? root, bool persist)
    {
        var normalized = NormalizeWorkspaceRoot(root);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _workspaceRoot = normalized;
        UpdateWorkspaceRootLabel();
        RefreshExplorer();
        UpdateTerminalCwd();

        if (persist)
        {
            PersistState();
        }
    }

    private void RefreshExplorer()
    {
        if (ExplorerTreeView is null)
        {
            return;
        }

        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            ExplorerTreeView.ItemsSource = Array.Empty<ExplorerTreeNode>();
            return;
        }

        const int maxNodes = 8000;
        var nodeBudget = maxNodes;
        var fileCount = 0;
        _gitStatusByPath.Clear();
        foreach (var kvp in GetGitStatusByPath(_workspaceRoot!))
        {
            _gitStatusByPath[kvp.Key] = kvp.Value;
        }

        _explorerRootNodes.Clear();
        var nodes = BuildExplorerNodes(_workspaceRoot!, ref nodeBudget, ref fileCount);
        _explorerRootNodes.AddRange(nodes);

        ExplorerTreeView.ItemsSource = _explorerRootNodes.ToList();
        if (WorkspaceRootTextBlock is not null)
        {
            WorkspaceRootTextBlock.Text = $"{_workspaceRoot}  ({fileCount} files)";
        }

        UpdateGitPanel();
    }

    private List<ExplorerTreeNode> BuildExplorerNodes(string directoryPath, ref int nodeBudget, ref int fileCount)
    {
        var nodes = new List<ExplorerTreeNode>();
        if (nodeBudget <= 0)
        {
            return nodes;
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(directoryPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            directories = Array.Empty<string>();
        }

        foreach (var dir in directories)
        {
            if (nodeBudget <= 0)
            {
                break;
            }

            var name = Path.GetFileName(dir);
            if (IgnoredWorkspaceDirectories.Contains(name))
            {
                continue;
            }

            nodeBudget--;
            var node = new ExplorerTreeNode
            {
                Name = name,
                FullPath = dir,
                IsDirectory = true,
                IconKind = MaterialIconKind.FolderOutline,
                GitBadge = GetDirectoryGitBadge(dir),
            };

            var children = BuildExplorerNodes(dir, ref nodeBudget, ref fileCount);
            node.Children.AddRange(children);
            nodes.Add(node);
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directoryPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            files = Array.Empty<string>();
        }

        foreach (var file in files)
        {
            if (nodeBudget <= 0)
            {
                break;
            }

            if (!IsExplorerVisibleFile(file))
            {
                continue;
            }

            nodeBudget--;
            fileCount++;
            nodes.Add(new ExplorerTreeNode
            {
                Name = Path.GetFileName(file),
                FullPath = file,
                IsDirectory = false,
                IconKind = GetFileIconKind(file),
                GitBadge = GetGitBadgeForPath(file),
            });
        }

        return nodes;
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var dir in directories)
            {
                var name = Path.GetFileName(dir);
                if (IgnoredWorkspaceDirectories.Contains(name))
                {
                    continue;
                }

                stack.Push(dir);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsSearchableFile(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsSearchableFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext))
        {
            return SearchableExtensions.Contains(ext);
        }

        var fileName = Path.GetFileName(filePath);
        return fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(".env", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplorerVisibleFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string ToRelativePath(string root, string fullPath)
    {
        try
        {
            return Path.GetRelativePath(root, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }

    private async void OnExplorerTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ExplorerTreeView?.SelectedItem is not ExplorerTreeNode item || item.IsDirectory)
        {
            return;
        }

        await OpenFilePathAsync(item.FullPath);
    }

    private async void OnExplorerTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ExplorerTreeView?.SelectedItem is not ExplorerTreeNode item || item.IsDirectory)
        {
            return;
        }

        await OpenFilePathAsync(item.FullPath);
    }

    private ExplorerTreeNode? GetSelectedExplorerNode()
        => ExplorerTreeView?.SelectedItem as ExplorerTreeNode;

    private string? GetExplorerTargetDirectory()
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            return null;
        }

        var selected = GetSelectedExplorerNode();
        if (selected is null)
        {
            return _workspaceRoot;
        }

        if (selected.IsDirectory)
        {
            return selected.FullPath;
        }

        return Path.GetDirectoryName(selected.FullPath) ?? _workspaceRoot;
    }

    private async void OnExplorerNewFileClick(object? sender, RoutedEventArgs e)
    {
        var directory = GetExplorerTargetDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var fileName = await PromptTextAsync("New File", "Enter file name:");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var fullPath = Path.Combine(directory, fileName);
        try
        {
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (!File.Exists(fullPath))
            {
                await File.WriteAllTextAsync(fullPath, string.Empty);
            }

            RefreshExplorer();
            await OpenFilePathAsync(fullPath);
        }
        catch
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Could not create file.";
            }
        }
    }

    private async void OnExplorerNewFolderClick(object? sender, RoutedEventArgs e)
    {
        var directory = GetExplorerTargetDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var folderName = await PromptTextAsync("New Folder", "Enter folder name:");
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        var fullPath = Path.Combine(directory, folderName);
        try
        {
            Directory.CreateDirectory(fullPath);
            RefreshExplorer();
        }
        catch
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Could not create folder.";
            }
        }
    }

    private async void OnExplorerRenameClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedExplorerNode();
        if (selected is null)
        {
            return;
        }

        var nextName = await PromptTextAsync("Rename", $"Rename '{selected.Name}' to:", selected.Name);
        if (string.IsNullOrWhiteSpace(nextName) || string.Equals(nextName, selected.Name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var parent = Path.GetDirectoryName(selected.FullPath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                return;
            }

            var nextPath = Path.Combine(parent, nextName);
            if (selected.IsDirectory)
            {
                Directory.Move(selected.FullPath, nextPath);
            }
            else
            {
                File.Move(selected.FullPath, nextPath);
                var openDoc = _viewModel.Documents.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(d.FilePath)
                    && string.Equals(Path.GetFullPath(d.FilePath!), Path.GetFullPath(selected.FullPath), StringComparison.OrdinalIgnoreCase));
                if (openDoc is not null)
                {
                    openDoc.FilePath = nextPath;
                    _viewModel.AddRecentFile(nextPath);
                }
            }

            RefreshExplorer();
            UpdateGitPanel();
            PersistState();
        }
        catch
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Rename failed.";
            }
        }
    }

    private async void OnExplorerDeleteClick(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedExplorerNode();
        if (selected is null)
        {
            return;
        }

        var ok = await ConfirmAsync("Delete", $"Delete '{selected.Name}'?");
        if (!ok)
        {
            return;
        }

        try
        {
            if (selected.IsDirectory)
            {
                Directory.Delete(selected.FullPath, recursive: true);
                foreach (var doc in _viewModel.Documents.ToList())
                {
                    if (string.IsNullOrWhiteSpace(doc.FilePath))
                    {
                        continue;
                    }

                    if (Path.GetFullPath(doc.FilePath!).StartsWith(Path.GetFullPath(selected.FullPath), StringComparison.OrdinalIgnoreCase))
                    {
                        _viewModel.Documents.Remove(doc);
                    }
                }
            }
            else
            {
                File.Delete(selected.FullPath);
                var doc = _viewModel.Documents.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(d.FilePath)
                    && string.Equals(Path.GetFullPath(d.FilePath!), Path.GetFullPath(selected.FullPath), StringComparison.OrdinalIgnoreCase));
                if (doc is not null)
                {
                    _viewModel.Documents.Remove(doc);
                }
            }

            if (_viewModel.Documents.Count == 0)
            {
                _viewModel.NewDocument();
            }

            RefreshExplorer();
            UpdateGitPanel();
        }
        catch
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Delete failed.";
            }
        }
    }

    private void OnExplorerTreeDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        var hasFiles = e.Data.GetFileNames()?.Any() == true;
#pragma warning restore CS0618
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnExplorerTreeDrop(object? sender, DragEventArgs e)
    {
        var directory = GetExplorerTargetDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

#pragma warning disable CS0618
        var files = e.Data.GetFileNames()?.ToList();
#pragma warning restore CS0618
        if (files is null || files.Count == 0)
        {
            return;
        }

        foreach (var source in files)
        {
            try
            {
                if (File.Exists(source))
                {
                    var target = Path.Combine(directory, Path.GetFileName(source));
                    File.Copy(source, target, overwrite: true);
                }
                else if (Directory.Exists(source))
                {
                    var target = Path.Combine(directory, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));
                    CopyDirectory(source, target);
                }
            }
            catch
            {
                // Ignore bad drops.
            }
        }

        RefreshExplorer();
        UpdateGitPanel();
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, dest);
        }
    }

    private string? GetGitRepositoryRoot()
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            return null;
        }

        var result = RunGit(_workspaceRoot!, "rev-parse --show-toplevel", timeoutMs: 1500);
        if (result.exitCode != 0)
        {
            return null;
        }

        var root = result.stdout.Trim();
        return Directory.Exists(root) ? root : null;
    }

    private Dictionary<string, string> GetGitStatusByPath(string workspaceRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return result;
        }

        var status = RunGit(repoRoot, "status --porcelain=v1 --untracked-files=all", timeoutMs: 3000);
        if (status.exitCode != 0)
        {
            return result;
        }

        foreach (var line in status.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var code = line.Substring(0, 2).Trim();
            var pathPart = line.Substring(3).Trim();
            if (pathPart.Contains("->", StringComparison.Ordinal))
            {
                var parts = pathPart.Split("->", StringSplitOptions.TrimEntries);
                pathPart = parts.LastOrDefault() ?? pathPart;
            }

            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, pathPart));
            result[fullPath] = MapGitBadge(code);
        }

        return result;
    }

    private static string MapGitBadge(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        if (code.Contains('?', StringComparison.Ordinal))
        {
            return "?";
        }

        if (code.Contains('A', StringComparison.Ordinal))
        {
            return "A";
        }

        if (code.Contains('D', StringComparison.Ordinal))
        {
            return "D";
        }

        if (code.Contains('R', StringComparison.Ordinal))
        {
            return "R";
        }

        if (code.Contains('U', StringComparison.Ordinal))
        {
            return "U";
        }

        if (code.Contains('M', StringComparison.Ordinal))
        {
            return "M";
        }

        return code.Trim();
    }

    private string? GetGitBadgeForPath(string fullPath)
        => _gitStatusByPath.TryGetValue(Path.GetFullPath(fullPath), out var badge) ? badge : null;

    private string? GetDirectoryGitBadge(string directoryPath)
    {
        var prefix = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var kvp in _gitStatusByPath)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private static MaterialIconKind GetFileIconKind(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".md" or ".markdown" or ".txt" => MaterialIconKind.FileDocumentOutline,
            ".json" or ".yaml" or ".yml" or ".xml" => MaterialIconKind.CodeJson,
            ".cs" or ".js" or ".ts" or ".py" or ".sql" or ".css" or ".html" => MaterialIconKind.FileCodeOutline,
            _ => MaterialIconKind.FileDocumentOutline,
        };
    }

    private void UpdateGitPanel()
    {
        if (GitChangesTreeView is null || GitSummaryTextBlock is null)
        {
            return;
        }

        _gitChanges.Clear();
        _gitChangeTreeRootNodes.Clear();
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            GitChangesTreeView.ItemsSource = Array.Empty<GitChangeTreeNode>();
            GitSummaryTextBlock.Text = "No git repository detected.";
            return;
        }

        var result = RunGit(repoRoot, "status --porcelain=v1 --untracked-files=all", timeoutMs: 3000);
        if (result.exitCode != 0)
        {
            GitChangesTreeView.ItemsSource = Array.Empty<GitChangeTreeNode>();
            GitSummaryTextBlock.Text = "Failed to read git status.";
            return;
        }

        var stagedEntries = new List<GitChangeEntryModel>();
        var unstagedEntries = new List<GitChangeEntryModel>();
        foreach (var line in result.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var xy = line.Substring(0, 2);
            var pathPart = line.Substring(3).Trim();
            if (pathPart.Contains("->", StringComparison.Ordinal))
            {
                var parts = pathPart.Split("->", StringSplitOptions.TrimEntries);
                pathPart = parts.LastOrDefault() ?? pathPart;
            }

            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, pathPart));
            var relative = ToRelativePath(repoRoot, fullPath);
            var stagedCode = GetStagedCode(xy);
            var unstagedCode = GetUnstagedCode(xy);
            if (stagedCode is not null)
            {
                stagedEntries.Add(new GitChangeEntryModel
                {
                    Code = stagedCode,
                    RelativePath = relative,
                    FullPath = fullPath,
                });
            }

            if (unstagedCode is not null)
            {
                unstagedEntries.Add(new GitChangeEntryModel
                {
                    Code = unstagedCode,
                    RelativePath = relative,
                    FullPath = fullPath,
                });
            }

            _gitChanges.Add(new GitChangeEntryModel
            {
                Code = stagedCode ?? unstagedCode ?? "--",
                RelativePath = relative,
                FullPath = fullPath,
            });
        }

        _gitChangeTreeRootNodes.AddRange(BuildGitChangeTree(repoRoot, stagedEntries, unstagedEntries));
        GitChangesTreeView.ItemsSource = _gitChangeTreeRootNodes.ToList();
        GitChangesTreeView.SelectedItem = null;
        GitSummaryTextBlock.Text = _gitChanges.Count == 0
            ? "Working tree clean."
            : $"{_gitChanges.Count} files changed | {stagedEntries.Count} staged | {unstagedEntries.Count} unstaged";
    }

    private static List<GitChangeTreeNode> BuildGitChangeTree(
        string repoRoot,
        IReadOnlyList<GitChangeEntryModel> stagedEntries,
        IReadOnlyList<GitChangeEntryModel> unstagedEntries)
    {
        var stagedRoot = new GitChangeTreeNode
        {
            Name = $"Staged ({stagedEntries.Count})",
            FullPath = repoRoot,
            IsDirectory = true,
            IsExpanded = true,
            IconKind = MaterialIconKind.SourceBranch,
        };
        stagedRoot.Children.AddRange(BuildGitSectionNodes(repoRoot, stagedEntries));
        if (stagedRoot.Children.Count == 0)
        {
            stagedRoot.Children.Add(new GitChangeTreeNode
            {
                Name = "(no staged files)",
                FullPath = repoRoot,
                IsDirectory = false,
                IsExpanded = false,
                IconKind = MaterialIconKind.FileDocumentOutline,
            });
        }

        var unstagedRoot = new GitChangeTreeNode
        {
            Name = $"Changes ({unstagedEntries.Count})",
            FullPath = repoRoot,
            IsDirectory = true,
            IsExpanded = true,
            IconKind = MaterialIconKind.SourceBranch,
        };
        unstagedRoot.Children.AddRange(BuildGitSectionNodes(repoRoot, unstagedEntries));
        if (unstagedRoot.Children.Count == 0)
        {
            unstagedRoot.Children.Add(new GitChangeTreeNode
            {
                Name = "(working tree clean)",
                FullPath = repoRoot,
                IsDirectory = false,
                IsExpanded = false,
                IconKind = MaterialIconKind.FileDocumentOutline,
            });
        }

        return new List<GitChangeTreeNode> { stagedRoot, unstagedRoot };
    }

    private static List<GitChangeTreeNode> BuildGitSectionNodes(string repoRoot, IReadOnlyList<GitChangeEntryModel> changes)
    {
        var roots = new List<GitChangeTreeNode>();
        var directoryLookup = new Dictionary<string, GitChangeTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes.OrderBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedPath = (change.RelativePath ?? string.Empty).Replace("\\", "/", StringComparison.Ordinal);
            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var currentPath = string.Empty;
            var currentNodes = roots;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                currentPath = string.IsNullOrEmpty(currentPath)
                    ? segments[i]
                    : $"{currentPath}/{segments[i]}";

                if (!directoryLookup.TryGetValue(currentPath, out var dirNode))
                {
                    var fullPath = Path.GetFullPath(Path.Combine(repoRoot, currentPath.Replace('/', Path.DirectorySeparatorChar)));
                    dirNode = new GitChangeTreeNode
                    {
                        Name = segments[i],
                        FullPath = fullPath,
                        IsDirectory = true,
                        IsExpanded = false,
                        IconKind = MaterialIconKind.FolderOutline,
                    };
                    directoryLookup[currentPath] = dirNode;
                    currentNodes.Add(dirNode);
                }

                currentNodes = dirNode.Children;
            }

            var fileName = segments[^1];
            currentNodes.Add(new GitChangeTreeNode
            {
                Name = fileName,
                FullPath = change.FullPath,
                IsDirectory = false,
                IsExpanded = false,
                IconKind = GetFileIconKind(change.FullPath),
                Status = change.Status,
            });
        }

        SortGitTreeNodes(roots);
        return roots;
    }

    private static void SortGitTreeNodes(List<GitChangeTreeNode> nodes)
    {
        nodes.Sort((left, right) =>
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });

        foreach (var node in nodes.Where(n => n.IsDirectory))
        {
            SortGitTreeNodes(node.Children);
        }
    }

    private void OnGitRefreshClick(object? sender, RoutedEventArgs e)
    {
        RefreshExplorer();
        UpdateGitPanel();
        UpdateGitDiffGutter();
    }

    private static string? GetStagedCode(string xy)
    {
        if (string.IsNullOrEmpty(xy) || xy.Length < 2)
        {
            return null;
        }

        var x = xy[0];
        if (x is ' ' or '?' or '!')
        {
            return null;
        }

        return x.ToString();
    }

    private static string? GetUnstagedCode(string xy)
    {
        if (string.IsNullOrEmpty(xy) || xy.Length < 2)
        {
            return null;
        }

        if (xy[0] == '?' && xy[1] == '?')
        {
            return "?";
        }

        var y = xy[1];
        if (y is ' ' or '!')
        {
            return null;
        }

        return y == '?' ? "?" : y.ToString();
    }

    private void OnGitExpandAllClick(object? sender, RoutedEventArgs e)
    {
        if (GitChangesTreeView is null)
        {
            return;
        }

        // Expand in multiple passes so newly materialized nested items also expand.
        for (var pass = 0; pass < 8; pass++)
        {
            var changed = false;
            foreach (var item in GitChangesTreeView.GetVisualDescendants().OfType<TreeViewItem>())
            {
                if (!item.IsExpanded)
                {
                    item.IsExpanded = true;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private void OnGitCollapseAllClick(object? sender, RoutedEventArgs e)
    {
        if (GitChangesTreeView is null)
        {
            return;
        }

        foreach (var item in GitChangesTreeView.GetVisualDescendants().OfType<TreeViewItem>())
        {
            item.IsExpanded = false;
        }
    }

    private void OnGitStageAllClick(object? sender, RoutedEventArgs e)
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        _ = RunGit(repoRoot, "add -A", timeoutMs: 5000);
        OnGitRefreshClick(sender, e);
    }

    private void OnGitUnstageAllClick(object? sender, RoutedEventArgs e)
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        _ = RunGit(repoRoot, "reset", timeoutMs: 5000);
        OnGitRefreshClick(sender, e);
    }

    private async void OnGitCommitClick(object? sender, RoutedEventArgs e)
    {
        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return;
        }

        var message = await PromptTextAsync("Commit", "Commit message:");
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var safeMessage = message.Replace("\"", "'", StringComparison.Ordinal);
        var result = RunGit(repoRoot, $"commit -m \"{safeMessage}\"", timeoutMs: 10000);
        if (GitSummaryTextBlock is not null)
        {
            GitSummaryTextBlock.Text = result.exitCode == 0
                ? "Committed successfully."
                : "Commit failed (check staged changes/message).";
        }

        OnGitRefreshClick(sender, e);
    }

    private async void OnGitChangeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (GitChangesTreeView?.SelectedItem is not GitChangeTreeNode item || item.IsDirectory)
        {
            return;
        }

        if (File.Exists(item.FullPath))
        {
            await OpenFilePathAsync(item.FullPath);
        }
    }

    private async void OnGitChangesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GitChangesTreeView?.SelectedItem is not GitChangeTreeNode item || item.IsDirectory)
        {
            return;
        }

        if (File.Exists(item.FullPath))
        {
            await OpenFilePathAsync(item.FullPath);
        }
    }

    private void OnShowSearchInFilesClick(object? sender, RoutedEventArgs e)
    {
        SetSidebarSection("Search", persist: true);

        if (SearchInFilesTextBox is not null)
        {
            if (string.IsNullOrWhiteSpace(SearchInFilesTextBox.Text) && !string.IsNullOrWhiteSpace(_viewModel.FindText))
            {
                SearchInFilesTextBox.Text = _viewModel.FindText;
            }

            SearchInFilesTextBox.Focus();
            SearchInFilesTextBox.CaretIndex = SearchInFilesTextBox.Text?.Length ?? 0;
        }
    }

    private void OnReplaceInFilesMenuClick(object? sender, RoutedEventArgs e)
    {
        OnShowSearchInFilesClick(sender, e);
        if (ReplaceInFilesTextBox is not null)
        {
            ReplaceInFilesTextBox.Focus();
            ReplaceInFilesTextBox.CaretIndex = ReplaceInFilesTextBox.Text?.Length ?? 0;
        }
    }

    private void OnSearchInFilesClick(object? sender, RoutedEventArgs e)
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Select a workspace folder first.";
            }

            return;
        }

        var query = SearchInFilesTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Type a search query.";
            }

            return;
        }

        var useRegex = SearchInFilesRegexCheckBox?.IsChecked == true;
        var matchCase = SearchInFilesCaseCheckBox?.IsChecked == true;
        var regex = BuildSearchRegex(query, useRegex, matchCase);
        if (regex is null)
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Invalid regex pattern.";
            }

            return;
        }

        var (scanned, matchedFiles) = ScanWorkspaceMatches(regex, maxMatches: 1500);
        RebuildSearchTree();

        if (SearchInFilesSummaryTextBlock is not null)
        {
            SearchInFilesSummaryTextBlock.Text = $"{_searchResultItems.Count} matches in {matchedFiles.Count} files (scanned {scanned}).";
        }
    }

    private (int scanned, HashSet<string> matchedFiles) ScanWorkspaceMatches(Regex regex, int maxMatches)
    {
        _searchResultItems.Clear();
        var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        foreach (var file in EnumerateWorkspaceFiles(_workspaceRoot!))
        {
            scanned++;
            var lineNo = 0;
            IEnumerable<string> lines;
            try
            {
                lines = File.ReadLines(file);
            }
            catch
            {
                continue;
            }

            foreach (var raw in lines)
            {
                lineNo++;
                foreach (Match m in regex.Matches(raw))
                {
                    if (!m.Success)
                    {
                        continue;
                    }

                    _searchResultItems.Add(new SearchResultItem(
                        file,
                        ToRelativePath(_workspaceRoot!, file),
                        lineNo,
                        m.Index + 1,
                        Math.Max(1, m.Length),
                        raw.Trim()));

                    matchedFiles.Add(file);
                    if (_searchResultItems.Count >= maxMatches)
                    {
                        break;
                    }
                }

                if (_searchResultItems.Count >= maxMatches)
                {
                    break;
                }
            }

            if (_searchResultItems.Count >= maxMatches)
            {
                break;
            }
        }

        return (scanned, matchedFiles);
    }

    private void RebuildSearchTree(Dictionary<string, string>? replacementPreviewByKey = null)
    {
        _searchTreeRootNodes.Clear();
        if (_searchResultItems.Count == 0)
        {
            if (SearchInFilesResultsTreeView is not null)
            {
                SearchInFilesResultsTreeView.ItemsSource = Array.Empty<SearchTreeNode>();
            }
            return;
        }

        var grouped = _searchResultItems
            .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var relative = ToRelativePath(_workspaceRoot ?? string.Empty, group.Key);
            var fileNode = new SearchTreeNode
            {
                DisplayText = $"{relative}  ({group.Count()} matches)",
            };

            foreach (var item in group.OrderBy(i => i.Line).ThenBy(i => i.Column))
            {
                var key = $"{item.FilePath}|{item.Line}|{item.Column}|{item.Length}";
                var preview = replacementPreviewByKey is not null && replacementPreviewByKey.TryGetValue(key, out var replaced)
                    ? $"{item.Line}:{item.Column}  {item.Preview}  =>  {replaced}"
                    : $"{item.Line}:{item.Column}  {item.Preview}";

                fileNode.Children.Add(new SearchTreeNode
                {
                    DisplayText = preview,
                    Location = new SearchResultLocation
                    {
                        FilePath = item.FilePath,
                        Line = item.Line,
                        Column = item.Column,
                        Length = item.Length,
                    },
                });
            }

            _searchTreeRootNodes.Add(fileNode);
        }

        if (SearchInFilesResultsTreeView is not null)
        {
            SearchInFilesResultsTreeView.ItemsSource = _searchTreeRootNodes.ToList();
        }
    }

    private void OnPreviewReplaceInFilesClick(object? sender, RoutedEventArgs e)
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Select a workspace folder first.";
            }

            return;
        }

        var query = SearchInFilesTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Type a search query before preview.";
            }

            return;
        }

        var replacement = ReplaceInFilesTextBox?.Text ?? string.Empty;
        var useRegex = SearchInFilesRegexCheckBox?.IsChecked == true;
        var matchCase = SearchInFilesCaseCheckBox?.IsChecked == true;
        var regex = BuildSearchRegex(query, useRegex, matchCase);
        if (regex is null)
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Invalid regex pattern.";
            }

            return;
        }

        var (_, matchedFiles) = ScanWorkspaceMatches(regex, maxMatches: 1200);
        var previewMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in _searchResultItems)
        {
            try
            {
                var replaced = regex.Replace(item.Preview, replacement);
                var key = $"{item.FilePath}|{item.Line}|{item.Column}|{item.Length}";
                previewMap[key] = replaced;
            }
            catch
            {
                // ignore bad replacement expression
            }
        }

        RebuildSearchTree(previewMap);
        if (SearchInFilesSummaryTextBlock is not null)
        {
            SearchInFilesSummaryTextBlock.Text = $"Previewing {_searchResultItems.Count} replacements in {matchedFiles.Count} files.";
        }
    }

    private async void OnReplaceInFilesClick(object? sender, RoutedEventArgs e)
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Select a workspace folder first.";
            }

            return;
        }

        var query = SearchInFilesTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Type a search query before replace.";
            }

            return;
        }

        var replacement = ReplaceInFilesTextBox?.Text ?? string.Empty;
        var useRegex = SearchInFilesRegexCheckBox?.IsChecked == true;
        var matchCase = SearchInFilesCaseCheckBox?.IsChecked == true;
        var regex = BuildSearchRegex(query, useRegex, matchCase);
        if (regex is null)
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Invalid regex pattern.";
            }

            return;
        }

        var replacements = 0;
        var changed = new List<string>();
        foreach (var file in EnumerateWorkspaceFiles(_workspaceRoot!))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            var localCount = 0;
            var updated = regex.Replace(text, _ =>
            {
                localCount++;
                return replacement;
            });

            if (localCount <= 0 || string.Equals(updated, text, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                File.WriteAllText(file, updated);
                replacements += localCount;
                changed.Add(file);
            }
            catch
            {
                // Ignore locked/unwritable files.
            }
        }

        foreach (var path in changed)
        {
            var doc = _viewModel.Documents.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.FilePath)
                && string.Equals(Path.GetFullPath(d.FilePath!), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

            if (doc is null || doc.IsDirty)
            {
                continue;
            }

            await ReloadFromDiskAsync(doc);
        }

        if (SearchInFilesSummaryTextBlock is not null)
        {
            SearchInFilesSummaryTextBlock.Text = $"Replaced {replacements} matches across {changed.Count} files.";
        }

        OnSearchInFilesClick(sender, e);
    }

    private static Regex? BuildSearchRegex(string query, bool useRegex, bool matchCase)
    {
        try
        {
            var pattern = useRegex ? query : Regex.Escape(query);
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

    private async void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SearchInFilesResultsTreeView?.SelectedItem is not SearchTreeNode node || node.Location is null)
        {
            return;
        }

        await OpenSearchResultAsync(node.Location);
    }

    private async Task OpenSearchResultAsync(SearchResultLocation item)
    {
        await OpenFilePathAsync(item.FilePath);
        if (EditorTextBox is null)
        {
            return;
        }

        GoToLine(EditorTextBox, item.Line, item.Column);
        var start = EditorTextBox.CaretOffset;
        var end = Math.Min(start + Math.Max(1, item.Length), (EditorTextBox.Text ?? string.Empty).Length);
        SetSelection(start, end);
    }

    private void UpdateDiagnostics()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        _diagnosticEntries.Clear();
        var text = EditorTextBox.Text ?? string.Empty;
        var language = _viewModel.StatusLanguage;

        try
        {
            switch (language)
            {
                case "C#":
                {
                    var syntaxTree = CSharpSyntaxTree.ParseText(text);
                    foreach (var diag in syntaxTree.GetDiagnostics()
                                 .Where(d => d.Severity is Microsoft.CodeAnalysis.DiagnosticSeverity.Warning or Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                                 .Take(100))
                    {
                        var span = diag.Location.GetLineSpan();
                        var line = span.StartLinePosition.Line + 1;
                        var col = span.StartLinePosition.Character + 1;
                        var sev = diag.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ? "Error" : "Warning";
                        _diagnosticEntries.Add(new DiagnosticEntry(sev, line, col, diag.GetMessage()));
                    }

                    break;
                }
                case "JSON":
                {
                    try
                    {
                        JsonDocument.Parse(text);
                    }
                    catch (JsonException ex)
                    {
                        var line = (int)(ex.LineNumber ?? 0) + 1;
                        var col = (int)(ex.BytePositionInLine ?? 0) + 1;
                        _diagnosticEntries.Add(new DiagnosticEntry("Error", line, col, ex.Message));
                    }

                    break;
                }
                case "XML":
                case "HTML":
                {
                    try
                    {
                        XDocument.Parse(text);
                    }
                    catch (Exception ex)
                    {
                        _diagnosticEntries.Add(new DiagnosticEntry("Error", 1, 1, ex.Message));
                    }

                    break;
                }
                case "YAML":
                {
                    try
                    {
                        var deserializer = new DeserializerBuilder().Build();
                        _ = deserializer.Deserialize<object?>(text);
                    }
                    catch (Exception ex)
                    {
                        _diagnosticEntries.Add(new DiagnosticEntry("Error", 1, 1, ex.Message));
                    }

                    break;
                }
                case "Python":
                {
                    if (!ShouldRunExternalDiagnostics(text))
                    {
                        break;
                    }

                    var ext = ".py";
                    var result = TryRunSyntaxTool("python3", $"-m py_compile \"{{file}}\"", text, ext);
                    if (!result.success && !string.IsNullOrWhiteSpace(result.error))
                    {
                        _diagnosticEntries.Add(new DiagnosticEntry("Warning", 1, 1, result.error));
                    }

                    break;
                }
                case "JavaScript":
                {
                    if (!ShouldRunExternalDiagnostics(text))
                    {
                        break;
                    }

                    var result = TryRunSyntaxTool("node", "--check \"{file}\"", text, ".js");
                    if (!result.success && !string.IsNullOrWhiteSpace(result.error))
                    {
                        _diagnosticEntries.Add(new DiagnosticEntry("Warning", 1, 1, result.error));
                    }

                    break;
                }
                case "TypeScript":
                {
                    if (!ShouldRunExternalDiagnostics(text))
                    {
                        break;
                    }

                    var result = TryRunSyntaxTool("tsc", "--pretty false --noEmit \"{file}\"", text, ".ts");
                    if (!result.success && !string.IsNullOrWhiteSpace(result.error))
                    {
                        _diagnosticEntries.Add(new DiagnosticEntry("Warning", 1, 1, result.error));
                    }

                    break;
                }
            }
        }
        catch
        {
            // Keep diagnostics best-effort and non-blocking.
        }

        if (StatusDiagnosticsTextBlock is not null)
        {
            StatusDiagnosticsTextBlock.Text = $"Diagnostics: {_diagnosticEntries.Count}";
        }

        if (DiagnosticsSummaryTextBlock is not null)
        {
            DiagnosticsSummaryTextBlock.Text = _diagnosticEntries.Count == 0
                ? "No diagnostics."
                : $"{_diagnosticEntries.Count} diagnostics";
        }

        if (DiagnosticsListBox is not null)
        {
            DiagnosticsListBox.ItemsSource = _diagnosticEntries.ToList();
        }
    }

    private void OnSettingsThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls || SettingsThemeComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var mode = item.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(mode))
        {
            SetThemeMode(mode);
        }
    }

    private bool ShouldRunExternalDiagnostics(string text)
    {
        if (text.Length > 20000)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if ((now - _lastExternalDiagnosticsRunUtc).TotalMilliseconds < 1200)
        {
            return false;
        }

        _lastExternalDiagnosticsRunUtc = now;
        return true;
    }

    private void OnSettingsLanguageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls || SettingsLanguageComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var mode = item.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(mode))
        {
            SetLanguageMode(mode);
        }
    }

    private void OnSettingsFontSizeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls)
        {
            return;
        }

        SetEditorFontSize(e.NewValue);
    }

    private void OnSettingsWordWrapClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls || _viewModel.SelectedDocument is null || SettingsWordWrapCheckBox is null)
        {
            return;
        }

        _viewModel.SelectedDocument.WordWrap = SettingsWordWrapCheckBox.IsChecked == true;
        ApplyWordWrap();
    }

    private void OnSettingsMiniMapClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls || SettingsMiniMapCheckBox is null)
        {
            return;
        }

        _isMiniMapEnabled = SettingsMiniMapCheckBox.IsChecked == true;
        ApplyMiniMapVisibility();
        PersistState();
    }

    private void OnSettingsFoldingClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls || SettingsFoldingCheckBox is null)
        {
            return;
        }

        _isFoldingEnabled = SettingsFoldingCheckBox.IsChecked == true;
        if (FoldingEnabledMenuItem is not null)
        {
            FoldingEnabledMenuItem.IsChecked = _isFoldingEnabled;
        }

        UpdateFolding();
        PersistState();
    }

    private void OnSettingsShowAllCharactersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls || _isUpdatingWhitespaceToggleControls || SettingsShowAllCharactersCheckBox is null)
        {
            return;
        }

        SetShowAllCharacters(SettingsShowAllCharactersCheckBox.IsChecked == true);
    }

    private void OnToolbarShowAllCharactersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls || _isUpdatingWhitespaceToggleControls || ToolbarShowAllCharactersCheckBox is null)
        {
            return;
        }

        SetShowAllCharacters(ToolbarShowAllCharactersCheckBox.IsChecked == true);
    }

    private void SetShowAllCharacters(bool enabled)
    {
        _showAllCharacters = enabled;
        ApplyWhitespaceOptions();
        UpdateWhitespaceToggleControls();
        PersistState();
    }

    private void UpdateWhitespaceToggleControls()
    {
        _isUpdatingWhitespaceToggleControls = true;
        try
        {
            if (SettingsShowAllCharactersCheckBox is not null)
            {
                SettingsShowAllCharactersCheckBox.IsChecked = _showAllCharacters;
            }

            if (ToolbarShowAllCharactersCheckBox is not null)
            {
                ToolbarShowAllCharactersCheckBox.IsChecked = _showAllCharacters;
            }
        }
        finally
        {
            _isUpdatingWhitespaceToggleControls = false;
        }
    }

    private void OnSettingsColumnGuideSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingSettingsControls || SettingsColumnGuideComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var raw = item.Content?.ToString();
        if (string.Equals(raw, "Off", StringComparison.OrdinalIgnoreCase))
        {
            SetColumnGuide(0);
            return;
        }

        if (int.TryParse(raw, out var col) && col > 0)
        {
            SetColumnGuide(col);
        }
    }

    private void UpdateSettingsControls()
    {
        _isUpdatingSettingsControls = true;
        try
        {
            SelectComboBoxItem(SettingsThemeComboBox, _themeMode);
            SelectComboBoxItem(SettingsLanguageComboBox, _languageMode);

            if (SettingsFontSizeSlider is not null)
            {
                SettingsFontSizeSlider.Value = _viewModel.EditorFontSize;
            }

            if (SettingsWordWrapCheckBox is not null)
            {
                SettingsWordWrapCheckBox.IsChecked = _viewModel.SelectedDocument?.WordWrap ?? false;
            }

            if (SettingsMiniMapCheckBox is not null)
            {
                SettingsMiniMapCheckBox.IsChecked = _isMiniMapEnabled;
            }

            if (SettingsFoldingCheckBox is not null)
            {
                SettingsFoldingCheckBox.IsChecked = _isFoldingEnabled;
            }

            UpdateWhitespaceToggleControls();

            if (SettingsColumnGuideComboBox is not null)
            {
                var guideLabel = _isColumnGuideEnabled ? _columnGuideColumn.ToString() : "Off";
                SelectComboBoxItem(SettingsColumnGuideComboBox, guideLabel);
            }
        }
        finally
        {
            _isUpdatingSettingsControls = false;
        }
    }

    private static void SelectComboBoxItem(ComboBox? comboBox, string? value)
    {
        if (comboBox is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var selected = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), value, StringComparison.Ordinal));

        if (selected is not null)
        {
            comboBox.SelectedItem = selected;
        }
    }

    private void OnSidebarWidthSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (EditorLayoutGrid is null || EditorLayoutGrid.ColumnDefinitions.Count == 0)
        {
            return;
        }

        var width = EditorLayoutGrid.ColumnDefinitions[0].ActualWidth;
        if (width > 180)
        {
            _sidebarWidth = width;
            PersistState();
        }
    }

    private void OnTerminalHeightSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (TerminalPane?.Bounds.Height > 110)
        {
            _terminalHeight = TerminalPane.Bounds.Height;
            PersistState();
        }
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
        UpdateDiagnostics();
        UpdateMiniMap();
        UpdateFolding();
        UpdateGitDiffGutter();
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
            UpdateDiagnostics();
            UpdateGitDiffGutter();
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
        UpdateSettingsControls();
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
        UpdateSettingsControls();
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

    private void OnToggleTerminalClick(object? sender, RoutedEventArgs e)
    {
        _isTerminalVisible = !_isTerminalVisible;
        UpdateTerminalLayout();
        PersistState();
    }

    private void OnToggleTabBarClick(object? sender, RoutedEventArgs e)
    {
        _showTabBar = !_showTabBar;
        UpdateTabStripVisibility();
        PersistState();
    }

    private void OnToggleAutoHideTabBarClick(object? sender, RoutedEventArgs e)
    {
        _autoHideTabBar = !_autoHideTabBar;
        UpdateTabStripVisibility();
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

        UpdateMiniMapDiffOverlay();
        UpdateSettingsControls();
    }

    private string GetShellWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_workspaceRoot) && Directory.Exists(_workspaceRoot))
        {
            return _workspaceRoot!;
        }

        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    private async void OnTerminalRunClick(object? sender, RoutedEventArgs e)
        => await RunTerminalCommandAsync();

    private void OnTerminalClearClick(object? sender, RoutedEventArgs e)
    {
        if (TerminalOutputTextBox is not null)
        {
            TerminalOutputTextBox.Text = string.Empty;
        }
    }

    private async void OnTerminalInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await RunTerminalCommandAsync();
    }

    private async Task RunTerminalCommandAsync()
    {
        if (_isTerminalBusy || TerminalInputTextBox is null || TerminalOutputTextBox is null)
        {
            return;
        }

        var command = TerminalInputTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _isTerminalBusy = true;
        try
        {
            AppendTerminalOutput($"> {command}\n");
            TerminalInputTextBox.Text = string.Empty;

            var workingDir = GetShellWorkingDirectory();
            var result = await Task.Run(() => RunProcess("/bin/zsh", $"-lc {EscapeShellArg(command)}", workingDir, timeoutMs: 20000));
            if (!string.IsNullOrWhiteSpace(result.stdout))
            {
                AppendTerminalOutput(result.stdout);
                if (!result.stdout.EndsWith('\n'))
                {
                    AppendTerminalOutput("\n");
                }
            }

            if (!string.IsNullOrWhiteSpace(result.stderr))
            {
                AppendTerminalOutput(result.stderr);
                if (!result.stderr.EndsWith('\n'))
                {
                    AppendTerminalOutput("\n");
                }
            }

            AppendTerminalOutput($"[exit {result.exitCode}]\n");
        }
        finally
        {
            _isTerminalBusy = false;
        }
    }

    private void AppendTerminalOutput(string text)
    {
        if (TerminalOutputTextBox is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        TerminalOutputTextBox.Text = (TerminalOutputTextBox.Text ?? string.Empty) + text;
        TerminalOutputTextBox.CaretIndex = TerminalOutputTextBox.Text.Length;
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
            UpdateMiniMapDiffOverlay();
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
        UpdateMiniMapDiffOverlay();
    }

    private void OnMiniMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (MiniMapPane is null || MiniMapScrollViewer is null || MiniMapTextBlock is null || EditorTextBox is null || _miniMapLineMap.Count == 0)
        {
            return;
        }

        var p = e.GetPosition(MiniMapPane);
        var contentHeight = Math.Max(MiniMapTextBlock.Bounds.Height, _miniMapLineMap.Count * Math.Max(1, MiniMapTextBlock.LineHeight));
        var contentY = p.Y + MiniMapScrollViewer.Offset.Y;
        var ratio = contentHeight <= 1 ? 0 : contentY / contentHeight;
        var idx = (int)Math.Round(ratio * (_miniMapLineMap.Count - 1));
        idx = Math.Clamp(idx, 0, _miniMapLineMap.Count - 1);
        GoToLine(EditorTextBox, _miniMapLineMap[idx], null);
        e.Handled = true;
    }

    private void OnMiniMapPaneSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateMiniMapDiffOverlay();
    }

    private void UpdateMiniMapDiffOverlay()
    {
        if (MiniMapDiffOverlay is null || MiniMapTextBlock is null || MiniMapScrollViewer is null)
        {
            return;
        }

        MiniMapDiffOverlay.Children.Clear();
        if (_miniMapLineMap.Count == 0 || _miniMapDiffMarkers.Count == 0)
        {
            return;
        }

        var lineCount = Math.Max(_miniMapLineMap.Count, _miniMapDiffMarkers.Count);
        var contentHeight = Math.Max(MiniMapTextBlock.Bounds.Height, _miniMapLineMap.Count * Math.Max(1, MiniMapTextBlock.LineHeight));
        var contentWidth = Math.Max(MiniMapScrollViewer.Bounds.Width, MiniMapTextBlock.Bounds.Width);
        MiniMapDiffOverlay.Width = contentWidth;
        MiniMapDiffOverlay.Height = contentHeight;
        if (lineCount <= 0 || contentHeight <= 1 || contentWidth <= 1)
        {
            return;
        }

        var markerWidth = 4.0;
        var markerLeft = Math.Max(0, contentWidth - markerWidth - 1);
        var isLight = string.Equals(_themeMode, "Light", StringComparison.Ordinal);
        var fillWidth = Math.Max(0, contentWidth - markerWidth - 2);
        var lane = new Border
        {
            Width = markerWidth,
            Height = contentHeight,
            Background = new SolidColorBrush(Color.Parse(isLight ? "#C8D3E0" : "#1C2835")),
            Opacity = 0.7,
        };
        Canvas.SetLeft(lane, markerLeft);
        Canvas.SetTop(lane, 0);
        MiniMapDiffOverlay.Children.Add(lane);

        for (var i = 0; i < _miniMapDiffMarkers.Count; i++)
        {
            var kind = _miniMapDiffMarkers[i];
            if (kind is not ('+' or '~' or '-'))
            {
                continue;
            }

            var runStart = i;
            while (i + 1 < _miniMapDiffMarkers.Count && _miniMapDiffMarkers[i + 1] == kind)
            {
                i++;
            }

            var runLength = i - runStart + 1;
            var top = (runStart / (double)lineCount) * contentHeight;
            var height = Math.Max(2.5, (runLength / (double)lineCount) * contentHeight);
            var fill = new Border
            {
                Width = fillWidth,
                Height = height,
                Background = GetDiffMarkerFillBrush(kind),
                Opacity = 0.22,
            };
            Canvas.SetLeft(fill, 0);
            Canvas.SetTop(fill, Math.Clamp(top, 0, Math.Max(0, contentHeight - height)));
            MiniMapDiffOverlay.Children.Add(fill);

            var marker = new Border
            {
                Width = markerWidth,
                Height = height,
                CornerRadius = new CornerRadius(1),
                Background = GetDiffMarkerBrush(kind),
                Opacity = 0.92,
            };

            Canvas.SetLeft(marker, markerLeft);
            Canvas.SetTop(marker, Math.Clamp(top, 0, Math.Max(0, contentHeight - height)));
            MiniMapDiffOverlay.Children.Add(marker);
        }
    }

    private IBrush GetDiffMarkerBrush(char kind)
    {
        var isLight = string.Equals(_themeMode, "Light", StringComparison.Ordinal);
        return kind switch
        {
            '+' => new SolidColorBrush(Color.Parse(isLight ? "#1B7A36" : "#2EA043")),
            '-' => new SolidColorBrush(Color.Parse(isLight ? "#B4232D" : "#F85149")),
            '~' => new SolidColorBrush(Color.Parse(isLight ? "#9A6500" : "#D29922")),
            _ => new SolidColorBrush(Color.Parse(isLight ? "#A0A8B2" : "#7B8490")),
        };
    }

    private IBrush GetDiffMarkerFillBrush(char kind)
    {
        var isLight = string.Equals(_themeMode, "Light", StringComparison.Ordinal);
        return kind switch
        {
            '+' => new SolidColorBrush(Color.Parse(isLight ? "#53C26B" : "#2EA043")),
            '-' => new SolidColorBrush(Color.Parse(isLight ? "#F06A77" : "#F85149")),
            '~' => new SolidColorBrush(Color.Parse(isLight ? "#E1B14A" : "#D29922")),
            _ => new SolidColorBrush(Color.Parse(isLight ? "#A0A8B2" : "#7B8490")),
        };
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
        if (_editorScrollViewer is null || LineNumbersTextBlock is null)
        {
            return;
        }

        // Keep gutter lines pixel-aligned with the editor text viewport.
        var y = -_editorScrollViewer.Offset.Y;
        _lineNumbersTransform.Y = y;
        _gitDiffTransform.Y = y;
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

        var gutterLineHeight = EditorTextBox.TextArea.TextView.DefaultLineHeight;
        if (double.IsNaN(gutterLineHeight) || gutterLineHeight <= 0)
        {
            gutterLineHeight = Math.Max(14, EditorTextBox.FontSize * 1.25);
        }

        LineNumbersTextBlock.LineHeight = gutterLineHeight;
        if (GitDiffGutterTextBlock is not null)
        {
            GitDiffGutterTextBlock.LineHeight = gutterLineHeight;
        }

        LineNumbersTextBlock.Text = sb.ToString();
        SyncGutterToScroll();
        UpdateGitDiffGutter();
    }

    private void UpdateGitDiffGutter()
    {
        if (GitDiffGutterTextBlock is null || EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var currentLines = NormalizeLines(text);
        if (currentLines.Length == 0)
        {
            GitDiffGutterTextBlock.Text = string.Empty;
            _miniMapDiffMarkers = Array.Empty<char>();
            UpdateMiniMapDiffOverlay();
            _gitDiffLineColorizer?.Clear();
            EditorTextBox.TextArea.TextView.InvalidateVisual();
            return;
        }

        var markers = BuildGitDiffMarkersForCurrentFile(_viewModel.SelectedDocument?.FilePath, currentLines.Length);
        GitDiffGutterTextBlock.Text = string.Join('\n', markers.Select(c => c.ToString()));
        _miniMapDiffMarkers = markers;
        UpdateMiniMapDiffOverlay();
        _gitDiffLineColorizer?.SetMarkers(markers);
        EditorTextBox.TextArea.TextView.InvalidateVisual();
    }

    private char[] BuildGitDiffMarkersForCurrentFile(string? filePath, int lineCount)
    {
        var markers = Enumerable.Repeat(' ', Math.Max(1, lineCount)).ToArray();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return markers;
        }

        var fullPath = Path.GetFullPath(filePath);
        var badge = GetGitBadgeForPath(fullPath);
        if (string.Equals(badge, "A", StringComparison.Ordinal) || string.Equals(badge, "?", StringComparison.Ordinal))
        {
            Array.Fill(markers, '+');
            return markers;
        }

        if (!File.Exists(fullPath))
        {
            return markers;
        }

        var repoRoot = GetGitRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return markers;
        }

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(repoRoot, fullPath).Replace("\\", "/", StringComparison.Ordinal);
        }
        catch
        {
            return markers;
        }

        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return markers;
        }

        var escapedPath = relativePath.Replace("\"", "\\\"", StringComparison.Ordinal);
        var diffResult = RunGit(repoRoot, $"diff --no-color --unified=0 HEAD -- \"{escapedPath}\"", timeoutMs: 3500);
        if (diffResult.exitCode != 0 || string.IsNullOrWhiteSpace(diffResult.stdout))
        {
            return markers;
        }

        ApplyGitDiffMarkersFromPatch(diffResult.stdout, markers);
        return markers;
    }

    private static string[] NormalizeLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Split('\n');
    }

    private static void ApplyGitDiffMarkersFromPatch(string patchText, char[] markers)
    {
        if (markers.Length == 0 || string.IsNullOrWhiteSpace(patchText))
        {
            return;
        }

        var inHunk = false;
        var currentNewLine = 1;
        var pendingDeletionCount = 0;

        void FlushPendingDeletions()
        {
            if (!inHunk || pendingDeletionCount <= 0)
            {
                return;
            }

            SetGitDiffMarker(markers, currentNewLine, '-');
            pendingDeletionCount = 0;
        }

        foreach (var rawLine in patchText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var hunkHeader = GitHunkHeaderRegex.Match(line);
            if (hunkHeader.Success)
            {
                FlushPendingDeletions();
                inHunk = true;
                pendingDeletionCount = 0;
                currentNewLine = ParseHunkLineNumber(hunkHeader.Groups["newStart"].Value);
                continue;
            }

            if (!inHunk)
            {
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                FlushPendingDeletions();
                var nextHeader = GitHunkHeaderRegex.Match(line);
                if (nextHeader.Success)
                {
                    pendingDeletionCount = 0;
                    currentNewLine = ParseHunkLineNumber(nextHeader.Groups["newStart"].Value);
                }

                continue;
            }

            if (line.StartsWith("diff --", StringComparison.Ordinal))
            {
                FlushPendingDeletions();
                inHunk = false;
                continue;
            }

            if (line.StartsWith("---", StringComparison.Ordinal)
                || line.StartsWith("+++", StringComparison.Ordinal)
                || line.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                pendingDeletionCount++;
                continue;
            }

            if (line.StartsWith("+", StringComparison.Ordinal))
            {
                if (pendingDeletionCount > 0)
                {
                    SetGitDiffMarker(markers, currentNewLine, '~');
                    pendingDeletionCount--;
                }
                else
                {
                    SetGitDiffMarker(markers, currentNewLine, '+');
                }

                currentNewLine++;
                continue;
            }

            if (line.StartsWith(" ", StringComparison.Ordinal))
            {
                FlushPendingDeletions();
                currentNewLine++;
            }
        }

        if (inHunk)
        {
            FlushPendingDeletions();
        }
    }

    private static int ParseHunkLineNumber(string value)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 1;

    private static void SetGitDiffMarker(char[] markers, int lineNumber, char marker)
    {
        if (markers.Length == 0)
        {
            return;
        }

        var index = Math.Clamp(lineNumber, 1, markers.Length) - 1;
        var existing = markers[index];
        if (existing == marker)
        {
            return;
        }

        if (existing == ' ')
        {
            markers[index] = marker;
            return;
        }

        if (existing == '~' || marker == '~')
        {
            markers[index] = '~';
            return;
        }

        if ((existing == '+' && marker == '-') || (existing == '-' && marker == '+'))
        {
            markers[index] = '~';
            return;
        }

        markers[index] = marker;
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
        UpdateSettingsControls();
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

    private void OnEditorFontFamilySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingEditorTypographySelectors || EditorFontFamilyComboBox is null)
        {
            return;
        }

        if (EditorFontFamilyComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        var selectedFamily = selectedItem.Content?.ToString();
        if (string.IsNullOrWhiteSpace(selectedFamily))
        {
            return;
        }

        SetEditorFontFamily(selectedFamily);
    }

    private void OnEditorFontSizeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingEditorTypographySelectors || EditorFontSizeComboBox is null)
        {
            return;
        }

        if (EditorFontSizeComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        if (!double.TryParse(selectedItem.Content?.ToString(), out var parsedSize))
        {
            return;
        }

        SetEditorFontSize(parsedSize);
    }

    private void SetEditorFontFamily(string fontFamily, bool persist = true)
    {
        var normalized = NormalizeEditorFontFamily(fontFamily);
        if (string.Equals(_editorFontFamily, normalized, StringComparison.Ordinal))
        {
            UpdateEditorTypographySelectors();
            return;
        }

        _editorFontFamily = normalized;
        ApplyEditorTypography();
        UpdateEditorTypographySelectors();

        if (persist)
        {
            PersistState();
        }
    }

    private string NormalizeEditorFontFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return EditorFontFamilies[0];
        }

        var candidate = fontFamily.Trim();
        return EditorFontFamilies.FirstOrDefault(item => string.Equals(item, candidate, StringComparison.Ordinal))
            ?? EditorFontFamilies[0];
    }

    private void UpdateEditorTypographySelectors()
    {
        if (EditorFontFamilyComboBox is null || EditorFontSizeComboBox is null)
        {
            return;
        }

        _isUpdatingEditorTypographySelectors = true;
        try
        {
            SelectComboBoxItem(EditorFontFamilyComboBox, _editorFontFamily);
            SelectComboBoxItem(EditorFontSizeComboBox, Math.Round(_viewModel.EditorFontSize).ToString());
        }
        finally
        {
            _isUpdatingEditorTypographySelectors = false;
        }
    }

    private void ApplyEditorTypography()
    {
        var family = new FontFamily(_editorFontFamily);

        if (EditorTextBox is not null)
        {
            EditorTextBox.FontFamily = family;
        }

        if (SplitEditorTextBox is not null)
        {
            SplitEditorTextBox.FontFamily = family;
        }

        if (LineNumbersTextBlock is not null)
        {
            LineNumbersTextBlock.FontFamily = family;
        }

        if (GitDiffGutterTextBlock is not null)
        {
            GitDiffGutterTextBlock.FontFamily = family;
        }

        if (MiniMapTextBlock is not null)
        {
            MiniMapTextBlock.FontFamily = family;
        }
    }

    private void OnThemeModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel || _isUpdatingThemeModeSelector || ThemeModeComboBox is null)
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
        UpdateSettingsControls();
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

        var activityRail = mode switch
        {
            "Light" => "#E3EAF2",
            _ => "#131F2D",
        };

        var sidebarNav = mode switch
        {
            "Light" => "#F6FAFE",
            _ => "#162434",
        };

        var sidebarNavHover = mode switch
        {
            "Light" => "#EAF2FB",
            _ => "#1F3348",
        };

        var sidebarNavChecked = mode switch
        {
            "Light" => "#D9E8F8",
            _ => "#26455E",
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
        Resources["ActivityRailBrush"] = new SolidColorBrush(Color.Parse(activityRail));
        Resources["SidebarNavBrush"] = new SolidColorBrush(Color.Parse(sidebarNav));
        Resources["SidebarNavHoverBrush"] = new SolidColorBrush(Color.Parse(sidebarNavHover));
        Resources["SidebarNavCheckedBrush"] = new SolidColorBrush(Color.Parse(sidebarNavChecked));

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
        UpdateSettingsControls();

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

        if (GitDiffGutterTextBlock is not null)
        {
            GitDiffGutterTextBlock.Foreground = string.Equals(_themeMode, "Light", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.Parse("#4F708A"))
                : new SolidColorBrush(Color.Parse("#6DB8E6"));
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

        _gitDiffLineColorizer?.SetTheme(_themeMode);
        EditorTextBox?.TextArea.TextView.InvalidateVisual();
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
        else if (ctrlOrCmd && e.Key == Key.Oem3)
        {
            OnToggleTerminalClick(this, new RoutedEventArgs());
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
        else if (ctrlOrCmd && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.F)
        {
            OnShowSearchInFilesClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrlOrCmd && e.Key == Key.Space)
        {
            _ = ShowAutocompleteAsync();
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
        SetEditorFontSize(_viewModel.EditorFontSize + delta);
    }

    private void ZoomReset()
        => SetEditorFontSize(DefaultEditorFontSize);

    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var ctrlOrCmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!ctrlOrCmd || Math.Abs(e.Delta.Y) <= double.Epsilon)
        {
            return;
        }

        ZoomBy(e.Delta.Y > 0 ? +1 : -1);
        e.Handled = true;
    }

    private void OnEditorTouchPadMagnify(object? sender, EventArgs e)
    {
        if (!TryGetZoomDelta(e, out var delta))
        {
            return;
        }

        SetEditorFontSize(_viewModel.EditorFontSize + delta);
        MarkHandled(e);
    }

    private void OnEditorPinch(object? sender, EventArgs e)
    {
        if (!TryGetZoomDelta(e, out var delta))
        {
            return;
        }

        SetEditorFontSize(_viewModel.EditorFontSize + delta);
        MarkHandled(e);
    }

    private void SetEditorFontSize(double size, bool persist = true)
    {
        _viewModel.EditorFontSize = Math.Clamp(Math.Round(size), MinEditorFontSize, MaxEditorFontSize);
        UpdateEditorTypographySelectors();
        UpdateSettingsControls();
        if (persist)
        {
            PersistState();
        }
    }

    private static bool TryGetZoomDelta(EventArgs e, out double delta)
    {
        delta = 0;

        if (TryGetDoubleProperty(e, "ScaleDelta", out var scaleDelta))
        {
            delta = NormalizeGestureDelta(scaleDelta);
            return Math.Abs(delta) > double.Epsilon;
        }

        if (TryGetDoubleProperty(e, "Scale", out var scale))
        {
            delta = NormalizeGestureDelta(scale - 1d);
            return Math.Abs(delta) > double.Epsilon;
        }

        if (TryGetDoubleProperty(e, "Delta", out var scalarDelta))
        {
            delta = NormalizeGestureDelta(scalarDelta);
            return Math.Abs(delta) > double.Epsilon;
        }

        if (TryGetVectorDelta(e, "Delta", out var vectorDelta))
        {
            delta = NormalizeGestureDelta(vectorDelta);
            return Math.Abs(delta) > double.Epsilon;
        }

        return false;
    }

    private static bool TryGetDoubleProperty(object target, string propertyName, out double value)
    {
        value = 0;
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
        {
            return false;
        }

        var raw = property.GetValue(target);
        switch (raw)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case int i:
                value = i;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetVectorDelta(object target, string propertyName, out double value)
    {
        value = 0;
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
        {
            return false;
        }

        var raw = property.GetValue(target);
        if (raw is null)
        {
            return false;
        }

        if (raw is Vector vector)
        {
            value = Math.Abs(vector.Y) >= Math.Abs(vector.X) ? vector.Y : vector.X;
            return true;
        }

        var yProperty = raw.GetType().GetProperty("Y", BindingFlags.Instance | BindingFlags.Public);
        if (yProperty?.GetValue(raw) is IConvertible y)
        {
            value = y.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        var xProperty = raw.GetType().GetProperty("X", BindingFlags.Instance | BindingFlags.Public);
        if (xProperty?.GetValue(raw) is IConvertible x)
        {
            value = x.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static double NormalizeGestureDelta(double rawDelta)
    {
        if (Math.Abs(rawDelta) < 0.001)
        {
            return 0;
        }

        return rawDelta > 0 ? +1 : -1;
    }

    private static void MarkHandled(EventArgs e)
    {
        if (e is RoutedEventArgs routed)
        {
            routed.Handled = true;
            return;
        }

        var handledProperty = e.GetType().GetProperty("Handled", BindingFlags.Instance | BindingFlags.Public);
        if (handledProperty is not null && handledProperty.PropertyType == typeof(bool) && handledProperty.CanWrite)
        {
            handledProperty.SetValue(e, true);
        }
    }

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
        _commandPaletteActions["file.openWorkspace"] = () => OnOpenWorkspaceClick(this, new RoutedEventArgs());
        _commandPaletteActions["file.quickOpen"] = () => OnQuickOpenClick(this, new RoutedEventArgs());
        _commandPaletteActions["file.save"] = () => OnSaveClick(this, new RoutedEventArgs());
        _commandPaletteActions["file.saveAs"] = () => OnSaveAsClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.find"] = () => OnShowFindClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.replace"] = () => OnShowReplaceClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.searchInFiles"] = () => OnShowSearchInFilesClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.replaceInFiles"] = () => OnReplaceInFilesMenuClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.goto"] = () => OnGoToLineClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.formatDocument"] = () => OnFormatDocumentClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.formatSelection"] = () => OnFormatSelectionClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.split"] = () => OnToggleSplitViewClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.minimap"] = () => OnToggleMiniMapClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.terminal"] = () => OnToggleTerminalClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.tabbar"] = () => OnToggleTabBarClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.autohidetabbar"] = () => OnToggleAutoHideTabBarClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.sourceControl"] = () => SetSidebarSection("Source Control", persist: true);
        _commandPaletteActions["view.theme.darkplus"] = () => OnThemeDarkPlusClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.theme.onedark"] = () => OnThemeOneDarkClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.theme.monokai"] = () => OnThemeMonokaiClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.theme.light"] = () => OnThemeLightClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.foldAll"] = () => OnFoldAllClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.unfoldAll"] = () => OnUnfoldAllClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.refresh"] = () => OnGitRefreshClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.stageAll"] = () => OnGitStageAllClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.unstageAll"] = () => OnGitUnstageAllClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.commit"] = () => OnGitCommitClick(this, new RoutedEventArgs());
    }

    private async Task ShowCommandPaletteAsync()
    {
        var items = new[]
        {
            new PaletteItem("file.new", "New File", "File"),
            new PaletteItem("file.open", "Open File...", "File"),
            new PaletteItem("file.openWorkspace", "Open Workspace Folder...", "File"),
            new PaletteItem("file.quickOpen", "Quick Open...", "File"),
            new PaletteItem("file.save", "Save", "File"),
            new PaletteItem("file.saveAs", "Save As...", "File"),
            new PaletteItem("edit.find", "Find", "Edit"),
            new PaletteItem("edit.replace", "Replace", "Edit"),
            new PaletteItem("edit.searchInFiles", "Search in Files", "Edit"),
            new PaletteItem("edit.replaceInFiles", "Replace in Files", "Edit"),
            new PaletteItem("edit.goto", "Go To Line", "Edit"),
            new PaletteItem("edit.formatDocument", "Format Document", "Format"),
            new PaletteItem("edit.formatSelection", "Format Selection", "Format"),
            new PaletteItem("view.split", "Toggle Split Editor", "View"),
            new PaletteItem("view.minimap", "Toggle Mini Map", "View"),
            new PaletteItem("view.terminal", "Toggle Terminal", "View"),
            new PaletteItem("view.tabbar", "Toggle Tab Bar", "View"),
            new PaletteItem("view.autohidetabbar", "Toggle Auto-Hide Tab Bar", "View"),
            new PaletteItem("view.sourceControl", "Open Source Control Panel", "View"),
            new PaletteItem("view.theme.darkplus", "Theme: Dark+", "View"),
            new PaletteItem("view.theme.onedark", "Theme: One Dark", "View"),
            new PaletteItem("view.theme.monokai", "Theme: Monokai", "View"),
            new PaletteItem("view.theme.light", "Theme: Light", "View"),
            new PaletteItem("view.foldAll", "Fold All", "View"),
            new PaletteItem("view.unfoldAll", "Unfold All", "View"),
            new PaletteItem("scm.refresh", "Source Control: Refresh", "Git"),
            new PaletteItem("scm.stageAll", "Source Control: Stage All", "Git"),
            new PaletteItem("scm.unstageAll", "Source Control: Unstage All", "Git"),
            new PaletteItem("scm.commit", "Source Control: Commit...", "Git"),
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

    private async Task ShowAutocompleteAsync()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, text.Length);
        var tokenStart = caret;
        while (tokenStart > 0 && IsIdentifierChar(text[tokenStart - 1]))
        {
            tokenStart--;
        }

        var prefix = text[tokenStart..caret];
        var language = _viewModel.StatusLanguage;
        var suggestions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(language, "C#", StringComparison.Ordinal))
        {
            foreach (var kw in CSharpCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else if (string.Equals(language, "JavaScript", StringComparison.Ordinal))
        {
            foreach (var kw in JavaScriptCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else if (string.Equals(language, "TypeScript", StringComparison.Ordinal))
        {
            foreach (var kw in TypeScriptCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else if (string.Equals(language, "Python", StringComparison.Ordinal))
        {
            foreach (var kw in PythonCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else if (string.Equals(language, "SQL", StringComparison.Ordinal))
        {
            foreach (var kw in SqlCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }

        foreach (Match m in Regex.Matches(text, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
        {
            if (!m.Success || m.Length < 2)
            {
                continue;
            }

            suggestions.Add(m.Value);
            if (suggestions.Count > 3000)
            {
                break;
            }
        }

        var filtered = suggestions
            .Where(s => string.IsNullOrWhiteSpace(prefix) || s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(150)
            .ToList();

        if (filtered.Count == 0)
        {
            return;
        }

        var items = filtered
            .Select(s => new PaletteItem($"completion:{s}", s, "Autocomplete"))
            .ToList();

        var dialog = new SelectionPaletteDialog("IntelliSense", "Pick a completion...", items);
        var selected = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(selected) || !selected.StartsWith("completion:", StringComparison.Ordinal))
        {
            return;
        }

        var completion = selected.Substring("completion:".Length);
        if (string.IsNullOrEmpty(completion))
        {
            return;
        }

        var updated = text.Substring(0, tokenStart) + completion + text.Substring(caret);
        EditorTextBox.Text = updated;
        EditorTextBox.CaretOffset = tokenStart + completion.Length;
    }

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

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
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            _workspaceRoot = NormalizeWorkspaceRoot(Path.GetDirectoryName(filePath));
        }

        RefreshExplorer();
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

        var doc = button.Tag as TextDocument
                  ?? button.DataContext as TextDocument
                  ?? _viewModel.SelectedDocument;
        if (doc is null)
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

    private void OnDocumentTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DocumentTabs?.SelectedItem is not TextDocument doc)
        {
            return;
        }

        if (!ReferenceEquals(_viewModel.SelectedDocument, doc))
        {
            _viewModel.SelectedDocument = doc;
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
        if (string.IsNullOrWhiteSpace(_workspaceRoot) && !string.IsNullOrWhiteSpace(path))
        {
            _workspaceRoot = NormalizeWorkspaceRoot(Path.GetDirectoryName(path));
        }

        RefreshExplorer();
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

    private async Task<string?> PromptTextAsync(string title, string prompt, string? initialValue = null)
    {
        var dialog = new TextInputDialog(title, prompt, initialValue);
        return await dialog.ShowDialog<string?>(this);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message);
        var result = await dialog.ShowDialog<bool>(this);
        return result;
    }

    private void PersistState()
    {
        if (_state is null)
        {
            return;
        }

        _state.RecentFiles = _viewModel.RecentFiles.ToList();
        _state.LastSessionFiles = _viewModel.GetSessionFilePaths().ToList();
        _state.Theme = _themeMode;
        _state.LanguageMode = _languageMode;
        _state.WorkspaceRoot = _workspaceRoot;
        _state.SidebarSection = _sidebarSection;
        _state.SidebarAutoHide = _isSidebarAutoHide;
        _state.SidebarExpanded = _isSidebarExpanded;
        _state.ShowMiniMap = _isMiniMapEnabled;
        _state.SplitViewEnabled = _isSplitViewEnabled;
        _state.FoldingEnabled = _isFoldingEnabled;
        _state.ShowAllCharacters = _showAllCharacters;
        _state.ColumnGuideEnabled = _isColumnGuideEnabled;
        _state.ColumnGuideColumn = _columnGuideColumn;
        _state.SidebarWidth = _sidebarWidth;
        _state.TerminalVisible = _isTerminalVisible;
        _state.TerminalHeight = _terminalHeight;
        _state.ShowTabBar = _showTabBar;
        _state.AutoHideTabBar = _autoHideTabBar;
        _state.EditorFontSize = _viewModel.EditorFontSize;
        _state.EditorFontFamily = _editorFontFamily;
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
        if (DataContext is not MainWindowViewModel || _isUpdatingLanguageModeSelector || LanguageModeComboBox is null)
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
        UpdateSettingsControls();
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

        UpdateDiagnostics();
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

    private void TryAutoFormatCurrentDocument()
    {
        if (_isApplyingAutoFormat || EditorTextBox is null || _viewModel.SelectedDocument is null)
        {
            return;
        }

        var language = _viewModel.StatusLanguage;
        if (!IsAutoFormatLanguage(language))
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200_000)
        {
            return;
        }

        var formatted = TryFormatByLanguage(text, language);
        if (formatted is null || string.Equals(formatted, text, StringComparison.Ordinal))
        {
            return;
        }

        var caret = EditorTextBox.CaretOffset;
        var selectionStart = EditorTextBox.SelectionStart;
        var selectionLength = EditorTextBox.SelectionLength;

        _isApplyingAutoFormat = true;
        try
        {
            EditorTextBox.Text = formatted;
            EditorTextBox.CaretOffset = Math.Min(caret, formatted.Length);
            EditorTextBox.SelectionStart = Math.Min(selectionStart, formatted.Length);
            EditorTextBox.SelectionLength = Math.Min(selectionLength, Math.Max(0, formatted.Length - EditorTextBox.SelectionStart));
        }
        finally
        {
            _isApplyingAutoFormat = false;
        }
    }

    private static bool IsAutoFormatLanguage(string language)
        => language is "C#" or "JSON" or "XML" or "HTML" or "YAML" or "JavaScript" or "TypeScript" or "CSS" or "SQL";

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
                "JavaScript" => TryFormatWithPrettier(text, "babel") ?? FormatBraceLanguage(text),
                "TypeScript" => TryFormatWithPrettier(text, "typescript") ?? FormatBraceLanguage(text),
                "CSS" => TryFormatWithPrettier(text, "css") ?? FormatBraceLanguage(text),
                "SQL" => TryFormatWithSqlFormatter(text) ?? FormatBraceLanguage(text),
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

    private static string? TryFormatWithPrettier(string text, string parser)
    {
        var args = $"--parser {parser}";
        return TryFormatWithCommand("prettier", args, text);
    }

    private static string? TryFormatWithSqlFormatter(string text)
    {
        return TryFormatWithCommand("sql-formatter", "--language sql", text);
    }

    private static (bool success, string? error) TryRunSyntaxTool(string tool, string argumentTemplate, string content, string extension)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"notepadsharp_{Guid.NewGuid():N}{extension}");
        try
        {
            File.WriteAllText(tempPath, content);
            var args = argumentTemplate.Replace("{file}", tempPath, StringComparison.Ordinal);
            var result = RunProcess(tool, args, Path.GetDirectoryName(tempPath) ?? Path.GetTempPath(), timeoutMs: 5000);
            if (result.exitCode == 0)
            {
                return (true, null);
            }

            var error = string.IsNullOrWhiteSpace(result.stderr)
                ? result.stdout
                : result.stderr;
            return (false, TrimSingleLine(error));
        }
        catch (Exception ex)
        {
            return (false, TrimSingleLine(ex.Message));
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Ignore.
            }
        }
    }

    private static string TrimSingleLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var line = text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? string.Empty : line.Trim();
    }

    private (int exitCode, string stdout, string stderr) RunGit(string repoRoot, string arguments, int timeoutMs = 4000)
        => RunProcess("git", $"-C \"{repoRoot}\" {arguments}", repoRoot, timeoutMs);

    private static (int exitCode, string stdout, string stderr) RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs = 5000)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                return (-1, string.Empty, $"Failed to start process: {fileName}");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore.
                }

                return (-1, string.Empty, "Process timed out.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static string EscapeShellArg(string value)
        => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string? TryFormatWithCommand(string command, string arguments, string input, int timeoutMs = 5000)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (!process.Start())
            {
                return null;
            }

            process.StandardInput.Write(input);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore.
                }

                return null;
            }

            var output = outputTask.GetAwaiter().GetResult();
            var _ = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            return output.TrimEnd('\r', '\n');
        }
        catch
        {
            return null;
        }
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
        var targetColumn = columnNumber is > 0 ? columnNumber.Value : 1;
        editor.ScrollTo(lineNumber, targetColumn);
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

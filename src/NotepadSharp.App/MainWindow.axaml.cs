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
    private bool _isEditorTextViewSyncAttached;
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
    private bool _showAllCharacters;
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


}

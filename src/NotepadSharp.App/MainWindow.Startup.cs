using System;
using System.IO;
using System.Linq;
using Avalonia;
using NotepadSharp.App.Services;
using NotepadSharp.App.ViewModels;
using AvaloniaEdit.Folding;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private void InitializeStartup()
    {
        DataContext = _viewModel;
        InitializeComponent();
        ApplyWindowChromeLayout();

        InitializeCommandPaletteActions();
        InitializeEditorControls();
        AttachViewModelObservers();
        LoadPersistedState();
        ApplyPersistedUiState();
        AttachCollectionObservers();
        AttachWindowLifecycleHandlers();
    }

    private void InitializeEditorControls()
    {
        if (EditorTextBox is not null)
        {
            ConfigureEditor(EditorTextBox);
            EditorTextBox.TextChanged += OnPrimaryEditorTextChanged;
            EditorTextBox.TextArea.Caret.PositionChanged += (_, __) => UpdateCaretStatus();
            _foldingManager = FoldingManager.Install(EditorTextBox.TextArea);
            _gitDiffBackgroundRenderer = new GitDiffBackgroundRenderer();
            _gitDiffBackgroundRenderer.SetTheme(_themeMode);
            EditorTextBox.TextArea.TextView.BackgroundRenderers.Add(_gitDiffBackgroundRenderer);
            _gitDiffLineColorizer = new GitDiffLineColorizer();
            _gitDiffLineColorizer.SetTheme(_themeMode);
            EditorTextBox.TextArea.TextView.LineTransformers.Add(_gitDiffLineColorizer);
            _splitComparePrimaryColorizer = new SplitCompareLineColorizer(isPrimaryPane: true);
            _splitComparePrimaryColorizer.SetTheme(_themeMode);
            EditorTextBox.TextArea.TextView.LineTransformers.Add(_splitComparePrimaryColorizer);
        }

        if (SplitEditorTextBox is not null)
        {
            ConfigureEditor(SplitEditorTextBox);
            SplitEditorTextBox.TextChanged += OnSplitEditorTextChanged;
            _splitCompareSecondaryColorizer = new SplitCompareLineColorizer(isPrimaryPane: false);
            _splitCompareSecondaryColorizer.SetTheme(_themeMode);
            SplitEditorTextBox.TextArea.TextView.LineTransformers.Add(_splitCompareSecondaryColorizer);
        }

        if (LineNumbersTextBlock is not null)
        {
            LineNumbersTextBlock.RenderTransform = _lineNumbersTransform;
        }

        if (GitDiffMarkerCanvas is not null)
        {
            GitDiffMarkerCanvas.RenderTransform = _gitDiffTransform;
        }
    }

    private void AttachViewModelObservers()
    {
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedDocument))
            {
                if (_isGitDiffCompareActive)
                {
                    var selectedPath = _viewModel.SelectedDocument?.FilePath;
                    var hasMatchingSelection = !string.IsNullOrWhiteSpace(selectedPath)
                        && !string.IsNullOrWhiteSpace(_gitDiffCompareTargetPath)
                        && string.Equals(
                            Path.GetFullPath(selectedPath!),
                            Path.GetFullPath(_gitDiffCompareTargetPath!),
                            StringComparison.OrdinalIgnoreCase);
                    if (!hasMatchingSelection)
                    {
                        DeactivateGitDiffCompareSession();
                    }
                }

                if (_splitDocument is null)
                {
                    _splitDocument = _viewModel.SelectedDocument;
                }

                SyncEditorFromDocument();
                SyncSplitEditorFromDocument();
                ApplyWordWrap();
                UpdateColumnGuide();
                UpdateCaretStatus();
                UpdateFindSummary();
                EnsureWorkspaceRoot();
                UpdateSettingsControls();
                RefreshSplitEditorTitle();
                UpdateTabStripVisibility();
                UpdateTabOverflowControls();
                ScheduleSelectedDocumentHeavyRefresh();
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
    }

    private void LoadPersistedState()
    {
        _state = _stateStore.Load();
        _viewModel.SetRecentFiles(_state.RecentFiles);

        _isColumnGuideEnabled = _state.ColumnGuideEnabled;
        if (_state.ColumnGuideColumn > 0)
        {
            _columnGuideColumn = _state.ColumnGuideColumn;
        }

        _isMiniMapEnabled = _state.ShowMiniMap;
        _isSplitViewEnabled = _state.SplitViewEnabled;
        _splitCompareMode = NormalizeSplitCompareMode(_state.SplitCompareMode);
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
        _terminalCommandHistory = CommandRunnerHistoryLogic.NormalizeHistory(_state.TerminalCommandHistory).ToList();
        _showTabBar = _state.ShowTabBar;
        _autoHideTabBar = _state.AutoHideTabBar;
        _aiProviderEnabled = _state.AiProviderEnabled;
        _aiProviderEndpoint = AiProviderConfigLogic.NormalizeEndpoint(_state.AiProviderEndpoint);
        _aiProviderModel = AiProviderConfigLogic.NormalizeModel(_state.AiProviderModel);
        _aiProviderApiKeyEnvironmentVariable = AiProviderConfigLogic.NormalizeApiKeyEnvironmentVariable(_state.AiProviderApiKeyEnvironmentVariable);
    }

    private void ApplyPersistedUiState()
    {
        var persistedFontSize = _state.EditorFontSize <= 0 ? DefaultEditorFontSize : _state.EditorFontSize;
        SetEditorFontSize(persistedFontSize, persist: false);
        _editorFontFamily = NormalizeEditorFontFamily(_state.EditorFontFamily);

        ApplyEditorTypography();
        ApplyWhitespaceOptions();
        UpdateSplitCompareControls();
        UpdateThemeModeSelector();
        UpdateEditorTypographySelectors();
        UpdateLanguageModeSelector();
        UpdateSettingsControls();
        UpdateAiAssistantUi();
        UpdateSidebarSectionUI();
        UpdateSidebarLayout();
        UpdateTerminalLayout();
        UpdateTabStripVisibility();
        UpdateEditorMaximizeUI();
        UpdateEditorMaximizeLayout();
    }

    private void ApplyWindowChromeLayout()
    {
        if (MenuBarHostBorder is null || QuickToolbarHostBorder is null)
        {
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            MenuBarHostBorder.Margin = new Thickness(8, 2, 8, 0);
            MenuBarHostBorder.Padding = new Thickness(72, 1, 2, 2);
            QuickToolbarHostBorder.Margin = new Thickness(8, 2, 8, 0);
            if (WindowDragStrip is not null)
            {
                WindowDragStrip.Height = 6;
            }

            return;
        }

        MenuBarHostBorder.Margin = new Thickness(8, 2, 8, 0);
        MenuBarHostBorder.Padding = new Thickness(2);
        QuickToolbarHostBorder.Margin = new Thickness(8, 2, 8, 0);
        if (WindowDragStrip is not null)
        {
            WindowDragStrip.Height = 10;
        }
    }

    private void AttachCollectionObservers()
    {
        RefreshOpenRecentMenu();
        _viewModel.RecentFiles.CollectionChanged += (_, __) => RefreshOpenRecentMenu();
        _viewModel.Documents.CollectionChanged += (_, __) =>
        {
            UpdateTabStripVisibility();
            UpdateTabOverflowControls();
        };
    }

    private void AttachWindowLifecycleHandlers()
    {
        Opened += OnWindowOpenedAsync;
        Activated += OnWindowActivatedAsync;
        Closing += OnWindowClosing;
    }

    private async void OnWindowOpenedAsync(object? sender, EventArgs e)
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
        UpdateTabOverflowControls();
        UpdatePerformanceStatusBar();
    }

    private async void OnWindowActivatedAsync(object? sender, EventArgs e)
    {
        await RefreshOpenDocumentsFromDiskIfNeededAsync();
    }
}

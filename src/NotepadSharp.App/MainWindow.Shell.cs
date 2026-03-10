using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using Material.Icons;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using NotepadSharp.App.Dialogs;
using NotepadSharp.App.ViewModels;
using NotepadSharp.Core;
using YamlDotNet.Serialization;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private const int EditorHeavyRefreshDebounceMs = 140;
    private CancellationTokenSource? _primaryEditorRefreshDebounceCts;
    private CancellationTokenSource? _splitEditorRefreshDebounceCts;

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
            var showWhitespaceMarkers = !_suppressWhitespaceMarkersForDiffOnly;
            editor.Options.ShowSpaces = showWhitespaceMarkers;
            editor.Options.ShowTabs = showWhitespaceMarkers;
            editor.Options.ShowEndOfLine = showWhitespaceMarkers && _showAllCharacters;
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

    private void OnWindowDragStripPointerPressed(object? sender, PointerPressedEventArgs e)
        => HandleTopChromePointerPressed(e);

    private void OnTopChromePointerPressed(object? sender, PointerPressedEventArgs e)
        => HandleTopChromePointerPressed(e);

    private void HandleTopChromePointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (IsTopChromeInteractiveSource(e.Source as StyledElement))
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    private bool IsTopChromeInteractiveSource(StyledElement? source)
    {
        for (var current = source; current is not null; current = current.Parent as StyledElement)
        {
            if (ReferenceEquals(current, WindowDragStrip)
                || ReferenceEquals(current, MenuBarHostBorder)
                || ReferenceEquals(current, QuickToolbarHostBorder))
            {
                break;
            }

            if (current is MenuItem
                or Button
                or ToggleButton
                or ComboBox
                or ComboBoxItem
                or CheckBox
                or TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private void OnToggleEditorMaximizeClick(object? sender, RoutedEventArgs e)
        => SetEditorMaximized(!_isEditorMaximized);

    private void SetEditorMaximized(bool maximized)
    {
        if (_isEditorMaximized == maximized)
        {
            UpdateEditorMaximizeUI();
            return;
        }

        _isEditorMaximized = maximized;
        if (maximized)
        {
            _preMaximizeWindowState = WindowState;
            if (WindowState == WindowState.Normal)
            {
                WindowState = WindowState.Maximized;
            }
        }
        else if (_preMaximizeWindowState != WindowState.Minimized)
        {
            WindowState = _preMaximizeWindowState;
        }

        UpdateEditorMaximizeLayout();
        UpdateEditorMaximizeUI();
        EditorTextBox?.Focus();
    }

    private void UpdateEditorMaximizeLayout()
    {
        if (MenuBarHostBorder is not null)
        {
            MenuBarHostBorder.IsVisible = !_isEditorMaximized;
        }

        if (QuickToolbarHostBorder is not null)
        {
            QuickToolbarHostBorder.IsVisible = !_isEditorMaximized;
        }

        if (StatusBarHostBorder is not null)
        {
            StatusBarHostBorder.IsVisible = !_isEditorMaximized;
        }

        if (EditorSurfaceBorder is not null)
        {
            EditorSurfaceBorder.Margin = _isEditorMaximized ? new Thickness(0) : new Thickness(8, 0, 8, 8);
            EditorSurfaceBorder.CornerRadius = _isEditorMaximized ? new CornerRadius(0) : new CornerRadius(12);
        }

        if (EditorMaximizeOverlayButton is not null)
        {
            EditorMaximizeOverlayButton.IsVisible = _isEditorMaximized;
        }

        UpdateTabStripVisibility();
        UpdateSidebarLayout();
        ApplyMiniMapVisibility();
        UpdateTerminalLayout();
    }

    private void UpdateEditorMaximizeUI()
    {
        if (MaximizeEditorMenuItem is not null)
        {
            MaximizeEditorMenuItem.IsChecked = _isEditorMaximized;
            MaximizeEditorMenuItem.Header = _isEditorMaximized
                ? "_Restore Editor Layout (F11)"
                : "_Maximize Editor (F11)";
        }

        if (EditorMaximizeButtonIcon is not null)
        {
            EditorMaximizeButtonIcon.Kind = _isEditorMaximized ? MaterialIconKind.FullscreenExit : MaterialIconKind.Fullscreen;
        }

        if (EditorMaximizeButtonText is not null)
        {
            EditorMaximizeButtonText.Text = _isEditorMaximized ? "Restore" : "Max";
        }

        if (EditorMaximizeButton is not null)
        {
            ToolTip.SetTip(
                EditorMaximizeButton,
                _isEditorMaximized ? "Restore editor layout (F11)" : "Maximize editor (F11)");
        }
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
        if (EditorLayoutGrid is null || SidebarPaneHost is null || SidebarHostBorder is null)
        {
            return;
        }

        static void SetSidebarColumns(Grid? grid, double sidebarWidth, double splitterWidth)
        {
            if (grid is null || grid.ColumnDefinitions.Count <= 1)
            {
                return;
            }

            grid.ColumnDefinitions[0].Width = new GridLength(sidebarWidth, GridUnitType.Pixel);
            grid.ColumnDefinitions[1].Width = new GridLength(splitterWidth, GridUnitType.Pixel);
        }

        if (_isEditorMaximized)
        {
            SidebarHostBorder.IsVisible = false;
            SidebarPaneHost.IsVisible = false;
            SidebarHostBorder.Margin = default;
            SetSidebarColumns(EditorLayoutGrid, 0, 0);
            SetSidebarColumns(TabStripLayoutGrid, 0, 0);

            if (SidebarWidthSplitter is not null)
            {
                SidebarWidthSplitter.IsVisible = false;
                SidebarWidthSplitter.Margin = default;
            }

            UpdateTabOverflowControls();

            return;
        }

        SidebarHostBorder.IsVisible = true;
        var expanded = !_isSidebarAutoHide || _isSidebarExpanded;
        SidebarPaneHost.IsVisible = expanded;
        if (SidebarAutoHideToggleButton is not null)
        {
            SidebarAutoHideToggleButton.IsChecked = _isSidebarAutoHide;
        }

        var width = expanded ? _sidebarWidth : 44;
        var splitter = expanded ? 6 : 0;
        SetSidebarColumns(EditorLayoutGrid, width, splitter);
        // Keep tabs aligned to the editor columns (after the sidebar).
        SetSidebarColumns(TabStripLayoutGrid, width, splitter);

        if (SidebarWidthSplitter is not null)
        {
            SidebarWidthSplitter.IsVisible = expanded;
        }

        UpdateSidebarTopOffset();
        UpdateTabOverflowControls();
    }

    private void UpdateTabStripVisibility()
    {
        if (DocumentTabs is null || ShowTabBarMenuItem is null || AutoHideTabBarMenuItem is null || TabStripLayoutGrid is null)
        {
            return;
        }

        // Auto-hide only when explicitly enabled and only one document is open.
        var autoHideSingleTab = _autoHideTabBar && _viewModel.Documents.Count <= 1;
        var visible = !_isEditorMaximized && _showTabBar && !autoHideSingleTab;
        TabStripLayoutGrid.IsVisible = visible;
        DocumentTabs.IsVisible = visible;
        ShowTabBarMenuItem.IsChecked = _showTabBar;
        AutoHideTabBarMenuItem.IsChecked = _autoHideTabBar;
        UpdateSidebarTopOffset();
        UpdateTabOverflowControls();
    }

    private void OnTabStripLayoutGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateSidebarTopOffset();
        UpdateTabOverflowControls();
    }

    private void OnTabOverflowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingTabOverflowSelector || TabOverflowComboBox?.SelectedItem is not TextDocument doc)
        {
            return;
        }

        if (!ReferenceEquals(_viewModel.SelectedDocument, doc))
        {
            _viewModel.SelectedDocument = doc;
        }
    }

    private void UpdateTabOverflowControls()
    {
        if (DocumentTabs is null || DocumentTabsHostGrid is null || TabOverflowComboBox is null)
        {
            return;
        }

        var tabsVisible = DocumentTabs.IsVisible;
        if (!tabsVisible)
        {
            TabOverflowComboBox.IsVisible = false;
            return;
        }

        var docCount = _viewModel.Documents.Count;
        var availableWidth = DocumentTabsHostGrid.Bounds.Width;
        var estimatedRequiredWidth = docCount * 160.0;
        var shouldShowOverflow = docCount > 1 && (docCount >= 7 || (availableWidth > 0 && estimatedRequiredWidth > availableWidth));

        TabOverflowComboBox.IsVisible = shouldShowOverflow;

        var tip = shouldShowOverflow
            ? $"Open tabs: {docCount}. Use this picker to jump to any tab."
            : "Select from all open tabs";
        ToolTip.SetTip(TabOverflowComboBox, tip);

        if (ReferenceEquals(TabOverflowComboBox.SelectedItem, _viewModel.SelectedDocument))
        {
            return;
        }

        _isUpdatingTabOverflowSelector = true;
        TabOverflowComboBox.SelectedItem = _viewModel.SelectedDocument;
        _isUpdatingTabOverflowSelector = false;
    }

    private void UpdateSidebarTopOffset()
    {
        if (SidebarHostBorder is null || SidebarWidthSplitter is null || TabStripLayoutGrid is null)
        {
            return;
        }

        var overlap = TabStripLayoutGrid.IsVisible ? Math.Max(0, TabStripLayoutGrid.Bounds.Height) : 0;
        SidebarHostBorder.Margin = overlap > 0
            ? new Thickness(0, -overlap, 0, 0)
            : default;
        SidebarWidthSplitter.Margin = overlap > 0
            ? new Thickness(0, -overlap, 0, 0)
            : default;
    }

    private void UpdateTerminalLayout()
    {
        if (TerminalPane is null || TerminalHeightSplitter is null)
        {
            return;
        }

        var showTerminal = !_isEditorMaximized && _isTerminalVisible;
        TerminalPane.IsVisible = showTerminal;
        TerminalHeightSplitter.IsVisible = showTerminal;

        if (TerminalPane.Parent is Grid grid && grid.RowDefinitions.Count >= 4)
        {
            grid.RowDefinitions[2].Height = new GridLength(showTerminal ? 6 : 0, GridUnitType.Pixel);
            grid.RowDefinitions[3].Height = new GridLength(showTerminal ? _terminalHeight : 0, GridUnitType.Pixel);
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
        UpdateFindSummary();
        SchedulePrimaryEditorHeavyRefresh();
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
            // Primary editor change handler will schedule the heavy refresh path.
            return;
        }

        ScheduleSplitEditorHeavyRefresh();
    }

    private void SchedulePrimaryEditorHeavyRefresh()
    {
        _primaryEditorRefreshDebounceCts?.Cancel();
        _primaryEditorRefreshDebounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        _primaryEditorRefreshDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(EditorHeavyRefreshDebounceMs, cts.Token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    UpdateLineNumbers();
                    ApplyLanguageStyling();
                    UpdateMiniMap();
                    UpdateFolding();
                    UpdateGitDiffGutter();
                    UpdateSplitCompareHighlights();
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when typing continuously.
            }
            catch
            {
                // Keep editor responsive even if a deferred update fails.
            }
        });
    }

    private void ScheduleSplitEditorHeavyRefresh()
    {
        _splitEditorRefreshDebounceCts?.Cancel();
        _splitEditorRefreshDebounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        _splitEditorRefreshDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(EditorHeavyRefreshDebounceMs, cts.Token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    var affectsPrimary = ReferenceEquals(_splitDocument, _viewModel.SelectedDocument);
                    if (affectsPrimary)
                    {
                        UpdateLineNumbers();
                        ApplyLanguageStyling();
                        UpdateMiniMap();
                        UpdateFolding();
                        UpdateGitDiffGutter();
                    }

                    UpdateSplitCompareHighlights();
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when typing continuously.
            }
            catch
            {
                // Keep editor responsive even if a deferred update fails.
            }
        });
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (!HasDroppedPaths(e))
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
        var names = GetDroppedPaths(e);
        if (names.Count == 0)
        {
            return;
        }

        var openedAny = false;
        foreach (var p in names)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
                {
                    continue;
                }

                await OpenFilePathAsync(p, deferUiRefresh: true);
                openedAny = true;
            }
            catch
            {
                // Ignore.
            }
        }

        if (openedAny)
        {
            RefreshExplorer();
            PersistState();
        }
    }

    private static TNode? GetTreeNodeFromEventSource<TNode>(RoutedEventArgs e) where TNode : class
    {
        if (e.Source is not StyledElement source)
        {
            return null;
        }

        for (StyledElement? current = source; current is not null; current = current.Parent as StyledElement)
        {
            if (current.DataContext is TNode node)
            {
                return node;
            }
        }

        return null;
    }

    private async Task OpenTreeFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!File.Exists(fullPath))
        {
            return;
        }

        if (_viewModel.SelectedDocument is not null && !string.IsNullOrWhiteSpace(_viewModel.SelectedDocument.FilePath))
        {
            try
            {
                var selectedFullPath = Path.GetFullPath(_viewModel.SelectedDocument.FilePath!);
                if (string.Equals(selectedFullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch
            {
                // Ignore malformed selected path.
            }
        }

        await OpenFilePathAsync(fullPath);
    }

    private static bool HasDroppedPaths(DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        if (items is null)
        {
            return false;
        }

        foreach (var item in items)
        {
            var localPath = item.Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetDroppedPaths(DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        if (items is null)
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var localPath = item.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(localPath))
            {
                continue;
            }

            if (seen.Add(localPath))
            {
                paths.Add(localPath);
            }
        }

        return paths;
    }


    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var ctrlOrCmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (e.Key == Key.F11)
        {
            SetEditorMaximized(!_isEditorMaximized);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isEditorMaximized)
        {
            SetEditorMaximized(false);
            e.Handled = true;
        }
        else if (e.Key == Key.F1 || (ctrlOrCmd && e.Key == Key.OemQuestion))
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
}

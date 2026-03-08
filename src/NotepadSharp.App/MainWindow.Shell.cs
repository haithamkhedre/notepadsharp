using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
}

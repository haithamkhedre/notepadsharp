using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using NotepadSharp.App.Services;
using NotepadSharp.App.ViewModels;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private const int MaxTerminalTranscriptChars = 80_000;

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

        ApplyEditorReadOnlyModes();
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
        if (_isGitDiffCompareActive)
        {
            DeactivateGitDiffCompareSession();
            return;
        }

        var enabling = !_isSplitViewEnabled;
        _isSplitViewEnabled = enabling;

        if (enabling)
        {
            // Always start split from the currently selected tab.
            _splitDocument = _viewModel.SelectedDocument;
            SyncSplitEditorFromDocument();
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

    private void OnToolbarSplitClick(object? sender, RoutedEventArgs e)
    {
        if (_isSplitViewEnabled)
        {
            _isSplitViewEnabled = false;
            ApplySplitView();
            PersistState();
            return;
        }

        OnSplitWithNextTabClick(sender, e);
    }

    private void OnSplitCompareModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSplitCompareSelector || SplitCompareModeComboBox is null)
        {
            return;
        }

        var selectedMode = GetSplitCompareModeSelection(SplitCompareModeComboBox);
        var normalizedMode = NormalizeSplitCompareMode(selectedMode);
        if (string.Equals(_splitCompareMode, normalizedMode, StringComparison.Ordinal))
        {
            UpdateSplitCompareHighlights();
            return;
        }

        _splitCompareMode = normalizedMode;
        SetSplitCompareModeSelection(SplitCompareModeComboBox, _splitCompareMode);
        UpdateSplitCompareHighlights();
        PersistState();
    }

    private void OnSplitComparePrevClick(object? sender, RoutedEventArgs e)
        => NavigateSplitCompare(-1);

    private void OnSplitCompareNextClick(object? sender, RoutedEventArgs e)
        => NavigateSplitCompare(1);

    private async void OnSplitOpenFileClick(object? sender, RoutedEventArgs e)
    {
        var provider = StorageProvider;
        if (provider is null)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File In Split",
            AllowMultiple = false,
        });

        var localPath = files.FirstOrDefault()?.Path?.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            return;
        }

        await OpenFileInSplitAsync(localPath);
    }

    private void OnSplitEditorDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = HasDroppedPaths(e);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnSplitEditorDrop(object? sender, DragEventArgs e)
    {
        var localPath = GetDroppedPaths(e).FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        e.Handled = true;
        await OpenFileInSplitAsync(localPath);
    }

    private async Task OpenFileInSplitAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (_isGitDiffCompareActive)
        {
            DeactivateGitDiffCompareSession();
        }

        var splitDoc = _viewModel.Documents.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.FilePath)
            && PathsEqual(d.FilePath!, filePath));

        var previousSelected = _viewModel.SelectedDocument;

        if (splitDoc is null)
        {
            await OpenFilePathAsync(filePath);
            splitDoc = _viewModel.Documents.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.FilePath)
                && PathsEqual(d.FilePath!, filePath));
        }

        if (splitDoc is null)
        {
            return;
        }

        if (previousSelected is not null
            && _viewModel.Documents.Contains(previousSelected)
            && !ReferenceEquals(_viewModel.SelectedDocument, previousSelected))
        {
            _viewModel.SelectedDocument = previousSelected;
        }

        _splitDocument = splitDoc;
        _isSplitViewEnabled = true;
        ApplySplitView();
        SyncSplitEditorFromDocument();
        RefreshSplitEditorTitle();
        PersistState();
    }

    private static bool PathsEqual(string leftPath, string rightPath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(leftPath),
                Path.GetFullPath(rightPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeSplitCompareMode(string? mode)
    {
        if (string.Equals(mode, "Compare", StringComparison.OrdinalIgnoreCase))
        {
            return "Compare";
        }

        if (string.Equals(mode, "Show diff only", StringComparison.OrdinalIgnoreCase))
        {
            return "Show diff only";
        }

        if (string.Equals(mode, "Show all", StringComparison.OrdinalIgnoreCase))
        {
            return "Show all";
        }

        return "Show all";
    }

    private static string GetSplitCompareModeSelection(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            return NormalizeSplitCompareMode(selectedItem.Content?.ToString());
        }

        if (comboBox.SelectedItem is ContentControl contentControl)
        {
            return NormalizeSplitCompareMode(contentControl.Content?.ToString());
        }

        if (comboBox.SelectedItem is string selectedText)
        {
            return NormalizeSplitCompareMode(selectedText);
        }

        return comboBox.SelectedIndex switch
        {
            0 => "Compare",
            1 => "Show diff only",
            2 => "Show all",
            _ => "Show all",
        };
    }

    private static void SetSplitCompareModeSelection(ComboBox comboBox, string mode)
    {
        var normalized = NormalizeSplitCompareMode(mode);
        var selected = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Content?.ToString(), normalized, StringComparison.OrdinalIgnoreCase));

        comboBox.SelectedItem = selected ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
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

        UpdateSplitCompareControls();
        UpdateEditorCompareTitles();
        ApplyEditorReadOnlyModes();
        UpdateSplitCompareHighlights();
        RefreshSplitEditorTitle();
    }

    private void RefreshSplitEditorTitle()
    {
        if (SplitEditorTitleTextBlock is null)
        {
            return;
        }

        SplitEditorTitleTextBlock.Text = _isGitDiffCompareActive
            ? _gitDiffCompareSecondaryTitle
            : $"Split: {(_splitDocument?.DisplayName ?? "current tab").TrimEnd('*')}";
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

        var showMiniMap = !_isEditorMaximized && _isMiniMapEnabled;
        MiniMapPane.IsVisible = showMiniMap;
        if (MiniMapMenuItem is not null)
        {
            MiniMapMenuItem.IsChecked = _isMiniMapEnabled;
        }

        UpdateMiniMapDiffOverlay();
        UpdateSettingsControls();
    }

    private void UpdateSplitCompareControls()
    {
        _isUpdatingSplitCompareSelector = true;
        try
        {
            var mode = NormalizeSplitCompareMode(_splitCompareMode);
            var canNavigate = _isSplitViewEnabled && !string.Equals(mode, "Show all", StringComparison.Ordinal);
            SetSplitCompareNavigationEnabled(canNavigate);

            if (SplitCompareModeComboBox is not null)
            {
                SplitCompareModeComboBox.IsEnabled = _isSplitViewEnabled;
                SetSplitCompareModeSelection(SplitCompareModeComboBox, _splitCompareMode);

                ToolTip.SetTip(
                    SplitCompareModeComboBox,
                    _isSplitViewEnabled
                        ? "Compare mode for split editors"
                        : "Enable split view to compare files");
            }
        }
        finally
        {
            _isUpdatingSplitCompareSelector = false;
        }
    }

    private void UpdateSplitCompareHighlights()
    {
        if (_splitComparePrimaryColorizer is null
            || _splitCompareSecondaryColorizer is null
            || EditorTextBox is null
            || SplitEditorTextBox is null)
        {
            return;
        }

        var mode = NormalizeSplitCompareMode(_splitCompareMode);
        var suppressWhitespace = _isSplitViewEnabled && string.Equals(mode, "Show diff only", StringComparison.Ordinal);
        if (_suppressWhitespaceMarkersForDiffOnly != suppressWhitespace)
        {
            _suppressWhitespaceMarkersForDiffOnly = suppressWhitespace;
            ApplyWhitespaceOptions(EditorTextBox);
            ApplyWhitespaceOptions(SplitEditorTextBox);
        }

        if (!_isSplitViewEnabled || string.Equals(mode, "Show all", StringComparison.Ordinal))
        {
            SetSplitCompareEmptyStateVisible(false);
            SetSplitCompareNavigationEnabled(false);
            _splitComparePrimaryColorizer.Clear();
            _splitCompareSecondaryColorizer.Clear();
            EditorTextBox.TextArea.TextView.InvalidateVisual();
            SplitEditorTextBox.TextArea.TextView.InvalidateVisual();
            return;
        }

        var leftLines = NormalizeLines(EditorTextBox.Text ?? string.Empty);
        var rightLines = NormalizeLines(SplitEditorTextBox.Text ?? string.Empty);
        var (leftDiffLines, rightDiffLines) = GetActiveSplitCompareDiffLines(leftLines, rightLines);

        var showDiffOnly = string.Equals(mode, "Show diff only", StringComparison.Ordinal);
        var hasDiff = leftDiffLines.Count > 0 || rightDiffLines.Count > 0;
        SetSplitCompareNavigationEnabled(hasDiff);
        SetSplitCompareEmptyStateVisible(showDiffOnly && leftDiffLines.Count == 0 && rightDiffLines.Count == 0);
        _splitComparePrimaryColorizer.SetDiffLines(leftDiffLines, showDiffOnly);
        _splitCompareSecondaryColorizer.SetDiffLines(rightDiffLines, showDiffOnly);
        EditorTextBox.TextArea.TextView.InvalidateVisual();
        SplitEditorTextBox.TextArea.TextView.InvalidateVisual();
    }

    private void SetSplitCompareEmptyStateVisible(bool visible)
    {
        if (SplitCompareEmptyStateOverlay is not null)
        {
            SplitCompareEmptyStateOverlay.IsVisible = visible;
        }
    }

    private void SetSplitCompareNavigationEnabled(bool enabled)
    {
        if (SplitComparePrevButton is not null)
        {
            SplitComparePrevButton.IsEnabled = enabled;
        }

        if (SplitCompareNextButton is not null)
        {
            SplitCompareNextButton.IsEnabled = enabled;
        }
    }

    private void NavigateSplitCompare(int direction)
    {
        if (!_isSplitViewEnabled
            || EditorTextBox is null
            || SplitEditorTextBox is null
            || direction == 0)
        {
            return;
        }

        var mode = NormalizeSplitCompareMode(_splitCompareMode);
        if (string.Equals(mode, "Show all", StringComparison.Ordinal))
        {
            return;
        }

        var leftLines = NormalizeLines(EditorTextBox.Text ?? string.Empty);
        var rightLines = NormalizeLines(SplitEditorTextBox.Text ?? string.Empty);
        var (leftDiffLines, rightDiffLines) = GetActiveSplitCompareDiffLines(leftLines, rightLines);
        var diffLines = leftDiffLines
            .Concat(rightDiffLines)
            .Distinct()
            .OrderBy(line => line)
            .ToList();

        if (diffLines.Count == 0)
        {
            SetSplitCompareNavigationEnabled(false);
            return;
        }

        SetSplitCompareNavigationEnabled(true);

        var focusPrimary = EditorTextBox.IsKeyboardFocusWithin || EditorTextBox.IsFocused;
        var focusSplit = SplitEditorTextBox.IsKeyboardFocusWithin || SplitEditorTextBox.IsFocused;
        var currentLine = focusSplit
            ? Math.Max(1, SplitEditorTextBox.TextArea.Caret.Line)
            : Math.Max(1, EditorTextBox.TextArea.Caret.Line);

        int targetLine;
        if (direction > 0)
        {
            targetLine = diffLines.FirstOrDefault(line => line > currentLine);
            if (targetLine <= 0)
            {
                targetLine = diffLines[0];
            }
        }
        else
        {
            targetLine = diffLines.LastOrDefault(line => line < currentLine);
            if (targetLine <= 0)
            {
                targetLine = diffLines[^1];
            }
        }

        GoToLine(EditorTextBox, targetLine, null);
        GoToLine(SplitEditorTextBox, targetLine, null);

        if (focusSplit)
        {
            SplitEditorTextBox.Focus();
        }
        else if (focusPrimary)
        {
            EditorTextBox.Focus();
        }
    }

    private static (List<int> leftDiffLines, List<int> rightDiffLines) ComputeSplitCompareDiffLines(string[] leftLines, string[] rightLines)
    {
        var leftLength = leftLines.Length;
        var rightLength = rightLines.Length;
        if (leftLength == 0 && rightLength == 0)
        {
            return (new List<int>(), new List<int>());
        }

        const long maxCells = 1_200_000;
        var cellCount = (long)leftLength * rightLength;
        if (cellCount > maxCells)
        {
            return ComputeSplitCompareDiffLinesFast(leftLines, rightLines);
        }

        var lcs = new int[leftLength + 1, rightLength + 1];
        for (var i = 1; i <= leftLength; i++)
        {
            for (var j = 1; j <= rightLength; j++)
            {
                if (string.Equals(leftLines[i - 1], rightLines[j - 1], StringComparison.Ordinal))
                {
                    lcs[i, j] = lcs[i - 1, j - 1] + 1;
                }
                else
                {
                    lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
                }
            }
        }

        var leftCommon = new bool[leftLength];
        var rightCommon = new bool[rightLength];
        var x = leftLength;
        var y = rightLength;
        while (x > 0 && y > 0)
        {
            if (string.Equals(leftLines[x - 1], rightLines[y - 1], StringComparison.Ordinal))
            {
                leftCommon[x - 1] = true;
                rightCommon[y - 1] = true;
                x--;
                y--;
            }
            else if (lcs[x - 1, y] >= lcs[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        var leftDiffLines = new List<int>();
        var rightDiffLines = new List<int>();
        for (var i = 0; i < leftLength; i++)
        {
            if (!leftCommon[i])
            {
                leftDiffLines.Add(i + 1);
            }
        }

        for (var i = 0; i < rightLength; i++)
        {
            if (!rightCommon[i])
            {
                rightDiffLines.Add(i + 1);
            }
        }

        return (leftDiffLines, rightDiffLines);
    }

    private static (List<int> leftDiffLines, List<int> rightDiffLines) ComputeSplitCompareDiffLinesFast(string[] leftLines, string[] rightLines)
    {
        var leftDiffLines = new HashSet<int>();
        var rightDiffLines = new HashSet<int>();
        const int lookAhead = 24;
        var i = 0;
        var j = 0;

        while (i < leftLines.Length && j < rightLines.Length)
        {
            if (string.Equals(leftLines[i], rightLines[j], StringComparison.Ordinal))
            {
                i++;
                j++;
                continue;
            }

            var nextRight = -1;
            for (var scan = j + 1; scan <= Math.Min(j + lookAhead, rightLines.Length - 1); scan++)
            {
                if (string.Equals(leftLines[i], rightLines[scan], StringComparison.Ordinal))
                {
                    nextRight = scan;
                    break;
                }
            }

            var nextLeft = -1;
            for (var scan = i + 1; scan <= Math.Min(i + lookAhead, leftLines.Length - 1); scan++)
            {
                if (string.Equals(leftLines[scan], rightLines[j], StringComparison.Ordinal))
                {
                    nextLeft = scan;
                    break;
                }
            }

            if (nextRight >= 0 && (nextLeft < 0 || nextRight - j <= nextLeft - i))
            {
                for (var k = j; k < nextRight; k++)
                {
                    rightDiffLines.Add(k + 1);
                }

                j = nextRight;
                continue;
            }

            if (nextLeft >= 0)
            {
                for (var k = i; k < nextLeft; k++)
                {
                    leftDiffLines.Add(k + 1);
                }

                i = nextLeft;
                continue;
            }

            leftDiffLines.Add(i + 1);
            rightDiffLines.Add(j + 1);
            i++;
            j++;
        }

        for (; i < leftLines.Length; i++)
        {
            leftDiffLines.Add(i + 1);
        }

        for (; j < rightLines.Length; j++)
        {
            rightDiffLines.Add(j + 1);
        }

        return (leftDiffLines.OrderBy(v => v).ToList(), rightDiffLines.OrderBy(v => v).ToList());
    }

    private (IReadOnlyList<int> leftDiffLines, IReadOnlyList<int> rightDiffLines) GetActiveSplitCompareDiffLines(string[] leftLines, string[] rightLines)
    {
        if (_isGitDiffCompareActive && _gitDiffCompareUsesExplicitDiffLines)
        {
            return (_gitDiffComparePrimaryDiffLines, _gitDiffCompareSecondaryDiffLines);
        }

        return ComputeSplitCompareDiffLines(leftLines, rightLines);
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

    private async void OnTerminalSendClick(object? sender, RoutedEventArgs e)
        => await SendTerminalInputAsync();

    private async void OnTerminalRestartClick(object? sender, RoutedEventArgs e)
        => await RestartTerminalSessionAsync();

    private async void OnTerminalInterruptClick(object? sender, RoutedEventArgs e)
        => await SendTerminalInterruptAsync();

    private void OnTerminalClearClick(object? sender, RoutedEventArgs e)
    {
        ResetTerminalOutput();
    }

    private async void OnTerminalInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            ResetTerminalOutput();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C
            && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            && string.IsNullOrEmpty(TerminalInputTextBox?.Text))
        {
            e.Handled = true;
            await SendTerminalInterruptAsync();
            return;
        }

        if (e.Key == Key.Up)
        {
            ShowPreviousTerminalCommand();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            ShowNextTerminalCommand();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await SendTerminalInterruptAsync();
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await SendTerminalInputAsync();
    }

    private async Task SendTerminalInputAsync()
    {
        if (_isTerminalBusy || TerminalInputTextBox is null || TerminalOutputTextBox is null)
        {
            return;
        }

        if (_isTerminalCommandInProgress)
        {
            UpdateTerminalStatusText();
            return;
        }

        var rawCommand = TerminalInputTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            return;
        }

        if (!await EnsureTerminalSessionAsync())
        {
            return;
        }

        var command = rawCommand.TrimEnd();
        RecordTerminalCommand(command);
        TerminalInputTextBox.Text = string.Empty;
        AppendTerminalOutput($"> {command}\n");
        _isTerminalCommandInProgress = true;
        _terminalPendingCommand = command;
        _terminalSessionLastExitCode = null;
        UpdateTerminalCwd();
        UpdateTerminalCommandButtons();

        try
        {
            await _terminalSession!.InputWriter.WriteLineAsync(command);
            await WriteTerminalStatusProbeAsync();
            await _terminalSession.InputWriter.FlushAsync();
        }
        catch (Exception ex)
        {
            _isTerminalCommandInProgress = false;
            _terminalPendingCommand = null;
            AppendTerminalOutput($"[session write failed: {ex.Message}]\n");
            StopTerminalSession();
            UpdateTerminalCommandButtons();
        }
    }

    private async Task SendTerminalInterruptAsync()
    {
        if (_isTerminalBusy || !IsTerminalSessionActive())
        {
            return;
        }

        try
        {
            AppendTerminalOutput("^C\n");
            await _terminalSession!.InputWriter.WriteAsync("\u0003");
            await _terminalSession.InputWriter.FlushAsync();
        }
        catch (Exception ex)
        {
            AppendTerminalOutput($"[session interrupt failed: {ex.Message}]\n");
            StopTerminalSession();
            UpdateTerminalCommandButtons();
        }
    }

    private async Task WriteTerminalStatusProbeAsync()
    {
        if (!IsTerminalSessionActive())
        {
            return;
        }

        var probeCommand = ShellSessionMetadataLogic.BuildStatusProbeCommand(
            _terminalSessionShellName,
            _terminalSessionMetadataToken);
        if (string.IsNullOrWhiteSpace(probeCommand))
        {
            return;
        }

        await _terminalSession!.InputWriter.WriteLineAsync(probeCommand);
    }

    private void AppendTerminalOutput(string text)
    {
        if (TerminalOutputTextBox is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        PrepareTerminalOutputForCommand();
        var appendResult = ShellSessionTranscriptLogic.AppendChunk(
            TerminalOutputTextBox.Text,
            _terminalTranscriptCursor,
            _terminalTranscriptPendingControlSequence,
            _terminalTranscriptInAlternateScreen,
            _terminalTranscriptSavedCursor,
            text,
            MaxTerminalTranscriptChars);
        TerminalOutputTextBox.Text = appendResult.Text;
        _terminalTranscriptCursor = appendResult.Cursor;
        _terminalTranscriptPendingControlSequence = appendResult.PendingControlSequence;
        _terminalTranscriptSavedCursor = appendResult.SavedCursor;
        _terminalTranscriptInAlternateScreen = appendResult.InAlternateScreen;
        TerminalOutputTextBox.CaretIndex = TerminalOutputTextBox.Text.Length;
    }

    private void AppendTerminalProcessOutput(string text)
    {
        if (TerminalOutputTextBox is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        var normalized = ShellSessionTranscriptLogic.NormalizeChunk(text);
        if (normalized.Length == 0)
        {
            return;
        }

        var processedChunk = ShellSessionMetadataLogic.ProcessOutputChunk(
            _terminalSessionOutputRemainder,
            normalized,
            _terminalSessionMetadataToken);
        _terminalSessionOutputRemainder = processedChunk.PendingPartialLine;

        if (!string.IsNullOrWhiteSpace(processedChunk.WorkingDirectory)
            && Directory.Exists(processedChunk.WorkingDirectory))
        {
            _terminalSessionWorkingDirectory = processedChunk.WorkingDirectory;
            UpdateTerminalCwd();
        }

        if (processedChunk.ExitCode is int exitCode)
        {
            _terminalSessionLastExitCode = exitCode;
            _isTerminalCommandInProgress = false;
            _terminalPendingCommand = null;
            UpdateTerminalCwd();
            UpdateTerminalCommandButtons();
        }

        if (processedChunk.VisibleText.Length == 0)
        {
            return;
        }

        PrepareTerminalOutputForCommand();
        var appendResult = ShellSessionTranscriptLogic.AppendChunk(
            TerminalOutputTextBox.Text,
            _terminalTranscriptCursor,
            _terminalTranscriptPendingControlSequence,
            _terminalTranscriptInAlternateScreen,
            _terminalTranscriptSavedCursor,
            processedChunk.VisibleText,
            MaxTerminalTranscriptChars);
        TerminalOutputTextBox.Text = appendResult.Text;
        _terminalTranscriptCursor = appendResult.Cursor;
        _terminalTranscriptPendingControlSequence = appendResult.PendingControlSequence;
        _terminalTranscriptSavedCursor = appendResult.SavedCursor;
        _terminalTranscriptInAlternateScreen = appendResult.InAlternateScreen;
        TerminalOutputTextBox.CaretIndex = TerminalOutputTextBox.Text.Length;
    }

    private void FlushTerminalOutputRemainder()
    {
        if (TerminalOutputTextBox is null || string.IsNullOrEmpty(_terminalSessionOutputRemainder))
        {
            _terminalSessionOutputRemainder = string.Empty;
            return;
        }

        if (!ShellSessionMetadataLogic.IsMarkerOnlyRemainder(_terminalSessionOutputRemainder, _terminalSessionMetadataToken))
        {
            PrepareTerminalOutputForCommand();
            var appendResult = ShellSessionTranscriptLogic.AppendChunk(
                TerminalOutputTextBox.Text,
                _terminalTranscriptCursor,
                _terminalTranscriptPendingControlSequence,
                _terminalTranscriptInAlternateScreen,
                _terminalTranscriptSavedCursor,
                _terminalSessionOutputRemainder,
                MaxTerminalTranscriptChars);
            TerminalOutputTextBox.Text = appendResult.Text;
            _terminalTranscriptCursor = appendResult.Cursor;
            _terminalTranscriptPendingControlSequence = appendResult.PendingControlSequence;
            _terminalTranscriptSavedCursor = appendResult.SavedCursor;
            _terminalTranscriptInAlternateScreen = appendResult.InAlternateScreen;
            TerminalOutputTextBox.CaretIndex = TerminalOutputTextBox.Text.Length;
        }

        _terminalSessionOutputRemainder = string.Empty;
    }

    private void PrepareTerminalOutputForCommand()
    {
        if (TerminalOutputTextBox is null)
        {
            return;
        }

        if (string.Equals(TerminalOutputTextBox.Text, GetCommandRunnerPlaceholderText(), StringComparison.Ordinal))
        {
            TerminalOutputTextBox.Text = string.Empty;
            _terminalTranscriptCursor = 0;
            _terminalTranscriptPendingControlSequence = string.Empty;
            _terminalTranscriptSavedCursor = null;
            _terminalTranscriptInAlternateScreen = false;
        }
    }

    private void ResetTerminalOutput()
    {
        if (TerminalOutputTextBox is null)
        {
            return;
        }

        TerminalOutputTextBox.Text = GetCommandRunnerPlaceholderText();
        TerminalOutputTextBox.CaretIndex = 0;
        _terminalTranscriptCursor = 0;
        _terminalTranscriptPendingControlSequence = string.Empty;
        _terminalTranscriptSavedCursor = null;
        _terminalTranscriptInAlternateScreen = false;
    }

    private string GetCommandRunnerPlaceholderText()
        => "Shell Session keeps shell state between commands.\n"
            + "PTY-backed on Unix when the system script wrapper is available.\n"
            + "PTY-backed on Windows when ConPTY is supported.\n"
            + "Common VT line and clear-screen controls are supported.\n"
            + "Alternate-screen apps render inline with basic cursor addressing.\n"
            + "Full VT coverage and full-screen terminal fidelity are still incomplete.\n";

    private bool IsTerminalSessionActive()
        => _terminalSession is { IsAlive: true };

    private void UpdateTerminalCommandButtons()
    {
        if (TerminalRestartButton is not null)
        {
            TerminalRestartButton.IsEnabled = !_isTerminalBusy;
        }

        if (TerminalInterruptButton is not null)
        {
            TerminalInterruptButton.IsEnabled = !_isTerminalBusy
                && _isTerminalCommandInProgress
                && IsTerminalSessionActive();
        }

        if (TerminalInterruptMenuItem is not null)
        {
            TerminalInterruptMenuItem.IsEnabled = !_isTerminalBusy
                && _isTerminalCommandInProgress
                && IsTerminalSessionActive();
        }

        if (TerminalSendButton is not null)
        {
            TerminalSendButton.IsEnabled = !_isTerminalBusy && !_isTerminalCommandInProgress;
        }

        if (TerminalInputTextBox is not null)
        {
            TerminalInputTextBox.IsEnabled = !_isTerminalBusy;
        }

        UpdateTerminalStatusText();
    }

    private void RecordTerminalCommand(string command)
    {
        _terminalCommandHistory = CommandRunnerHistoryLogic.RecordCommand(_terminalCommandHistory, command).ToList();
        ResetTerminalHistoryNavigation();
        PersistState();
    }

    private void ShowPreviousTerminalCommand()
    {
        if (TerminalInputTextBox is null || _terminalCommandHistory.Count == 0)
        {
            return;
        }

        if (_terminalHistoryIndex < 0)
        {
            _terminalHistoryDraft = TerminalInputTextBox.Text ?? string.Empty;
            _terminalHistoryIndex = 0;
        }
        else if (_terminalHistoryIndex < _terminalCommandHistory.Count - 1)
        {
            _terminalHistoryIndex++;
        }

        SetTerminalInputText(_terminalCommandHistory[_terminalHistoryIndex]);
    }

    private void ShowNextTerminalCommand()
    {
        if (TerminalInputTextBox is null || _terminalCommandHistory.Count == 0)
        {
            return;
        }

        if (_terminalHistoryIndex <= 0)
        {
            ResetTerminalHistoryNavigation();
            SetTerminalInputText(_terminalHistoryDraft ?? string.Empty);
            return;
        }

        _terminalHistoryIndex--;
        SetTerminalInputText(_terminalCommandHistory[_terminalHistoryIndex]);
    }

    private void ResetTerminalHistoryNavigation()
    {
        _terminalHistoryIndex = -1;
        _terminalHistoryDraft = null;
    }

    private async Task<bool> EnsureTerminalSessionAsync()
    {
        if (IsTerminalSessionActive())
        {
            return true;
        }

        if (_isTerminalBusy)
        {
            return false;
        }

        _isTerminalBusy = true;
        UpdateTerminalCommandButtons();

        try
        {
            StopTerminalSession();

            var workingDir = GetShellWorkingDirectory();
            var invocation = ShellCommandLogic.BuildSessionInvocation();
            var session = ShellSessionFactory.Start(invocation, workingDir);

            _terminalSession = session;
            _terminalSessionWorkingDirectory = workingDir;
            _terminalSessionShellName = session.DisplayName;
            _terminalSessionUsesPty = session.UsesPty;
            _terminalSessionMetadataToken = Guid.NewGuid().ToString("N");
            _terminalSessionLastExitCode = null;
            _isTerminalCommandInProgress = false;
            _terminalPendingCommand = null;
            _terminalSessionOutputRemainder = string.Empty;
            _terminalTranscriptCursor = 0;
            _terminalTranscriptPendingControlSequence = string.Empty;
            _terminalTranscriptSavedCursor = null;
            _terminalTranscriptInAlternateScreen = false;
            _terminalSessionOutputCts = new CancellationTokenSource();

            foreach (var reader in session.OutputReaders)
            {
                _ = PumpTerminalStreamAsync(reader, _terminalSessionOutputCts.Token);
            }

            _ = MonitorTerminalSessionExitAsync(session);

            AppendTerminalOutput(
                $"[session started | shell: {session.DisplayName} | mode: {GetTerminalSessionModeLabel(session.UsesPty)} | cwd: {workingDir}]\n");
            if (!string.IsNullOrWhiteSpace(session.StartupNote))
            {
                AppendTerminalOutput($"[session note: {session.StartupNote}]\n");
            }

            UpdateTerminalCwd();
            return true;
        }
        catch (Exception ex)
        {
            AppendTerminalOutput($"[failed to start shell session: {ex.Message}]\n");
            StopTerminalSession();
            return false;
        }
        finally
        {
            _isTerminalBusy = false;
            UpdateTerminalCommandButtons();
        }
    }

    private async Task RestartTerminalSessionAsync()
    {
        if (_isTerminalBusy)
        {
            return;
        }

        StopTerminalSession(appendStoppedMessage: _terminalSession is not null);
        await EnsureTerminalSessionAsync();
    }

    private async Task PumpTerminalStreamAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[512];

        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                var chunk = new string(buffer, 0, read);
                await Dispatcher.UIThread.InvokeAsync(() => AppendTerminalProcessOutput(chunk));
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore shutdown races.
        }
        catch (ObjectDisposedException)
        {
            // Ignore shutdown races.
        }
        catch (InvalidOperationException)
        {
            // Ignore process teardown races.
        }
    }

    private async Task MonitorTerminalSessionExitAsync(ShellSessionHandle session)
    {
        try
        {
            await session.WaitForExitAsync();
        }
        catch
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_terminalSession, session))
            {
                return;
            }

            var exitCode = SafeGetSessionExitCode(session);
            ReleaseTerminalSessionState(session);
            AppendTerminalOutput($"[session exited {exitCode}]\n");
            UpdateTerminalCommandButtons();
            UpdateTerminalCwd();
        });
    }

    private static int SafeGetSessionExitCode(ShellSessionHandle session)
        => session.TryGetExitCode(out var exitCode) ? exitCode : -1;

    private void ReleaseTerminalSessionState(ShellSessionHandle? session)
    {
        FlushTerminalOutputRemainder();
        _terminalSession = null;
        _terminalSessionWorkingDirectory = null;
        _terminalSessionShellName = null;
        _terminalSessionUsesPty = false;
        _terminalSessionMetadataToken = null;
        _terminalSessionLastExitCode = null;
        _isTerminalCommandInProgress = false;
        _terminalPendingCommand = null;
        _terminalSessionOutputRemainder = string.Empty;
        _terminalTranscriptCursor = 0;
        _terminalTranscriptPendingControlSequence = string.Empty;
        _terminalTranscriptSavedCursor = null;
        _terminalTranscriptInAlternateScreen = false;

        _terminalSessionOutputCts?.Cancel();
        _terminalSessionOutputCts?.Dispose();
        _terminalSessionOutputCts = null;

        try
        {
            session?.Dispose();
        }
        catch
        {
            // Ignore disposal races.
        }
    }

    private void StopTerminalSession(bool appendStoppedMessage = false)
    {
        var session = _terminalSession;
        var outputCts = _terminalSessionOutputCts;
        if (session is null && outputCts is null)
        {
            return;
        }

        FlushTerminalOutputRemainder();
        _terminalSession = null;
        _terminalSessionWorkingDirectory = null;
        _terminalSessionShellName = null;
        _terminalSessionUsesPty = false;
        _terminalSessionMetadataToken = null;
        _terminalSessionLastExitCode = null;
        _isTerminalCommandInProgress = false;
        _terminalPendingCommand = null;
        _terminalSessionOutputRemainder = string.Empty;
        _terminalTranscriptCursor = 0;
        _terminalTranscriptPendingControlSequence = string.Empty;
        _terminalTranscriptSavedCursor = null;
        _terminalTranscriptInAlternateScreen = false;
        _terminalSessionOutputCts = null;

        outputCts?.Cancel();
        outputCts?.Dispose();

        if (session is not null)
        {
            try
            {
                if (session.IsAlive)
                {
                    session.CloseInput();
                    if (!session.WaitForExit(200))
                    {
                        session.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
                // Ignore process teardown errors.
            }
        }

        try
        {
            session?.Dispose();
        }
        catch
        {
            // Ignore disposal races.
        }

        if (appendStoppedMessage)
        {
            AppendTerminalOutput("[session stopped]\n");
        }

        UpdateTerminalCommandButtons();
        UpdateTerminalCwd();
    }

    private void ResetTerminalSessionForContextChange(string reason)
    {
        if (_terminalSession is null)
        {
            UpdateTerminalCwd();
            return;
        }

        StopTerminalSession();
        AppendTerminalOutput($"[session reset: {reason}]\n");
    }

    private void SetTerminalInputText(string text)
    {
        if (TerminalInputTextBox is null)
        {
            return;
        }

        TerminalInputTextBox.Text = text;
        TerminalInputTextBox.CaretIndex = text.Length;
    }

    private void OnNextGitChangeClick(object? sender, RoutedEventArgs e)
        => NavigateGitChanges(forward: true);

    private void OnPreviousGitChangeClick(object? sender, RoutedEventArgs e)
        => NavigateGitChanges(forward: false);

    private void NavigateGitChanges(bool forward)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var currentLine = Math.Max(1, EditorTextBox.TextArea.Caret.Line);
        var targetLine = GitDiffNavigationLogic.GetTargetLine(_miniMapDiffMarkers, currentLine, forward);
        if (targetLine is null)
        {
            return;
        }

        GoToLine(EditorTextBox, targetLine.Value, null);
    }

    private void UpdateMiniMap()
    {
        if (MiniMapTextBlock is null || !_isMiniMapEnabled || _isEditorMaximized || MiniMapPane?.IsVisible != true)
        {
            return;
        }

        var text = EditorTextBox?.Text ?? string.Empty;
        var lineCount = Math.Max(1, EditorTextBox?.Document?.LineCount ?? 1);
        _miniMapLineMap.Clear();
        if (text.Length == 0)
        {
            MiniMapTextBlock.Text = string.Empty;
            UpdateMiniMapDiffOverlay();
            return;
        }

        if (text.Length > LargeFileTextLengthThreshold || lineCount > LargeFileMiniMapLineThreshold)
        {
            MiniMapTextBlock.Text = "Mini map paused for large file.";
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
        if (MiniMapDiffOverlay is null || MiniMapTextBlock is null || MiniMapScrollViewer is null || MiniMapPane?.IsVisible != true)
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

        if (!_isEditorTextViewSyncAttached)
        {
            var textView = EditorTextBox.TextArea.TextView;
            textView.ScrollOffsetChanged += (_, __) => SyncGutterToScroll();
            textView.VisualLinesChanged += (_, __) => SyncGutterToScroll();
            _isEditorTextViewSyncAttached = true;
        }

        if (_editorScrollViewer is null)
        {
            _editorScrollViewer = EditorTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_editorScrollViewer is not null)
            {
                _editorScrollViewer.ScrollChanged += (_, __) => SyncGutterToScroll();
            }
        }

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
        if (LineNumbersTextBlock is null || EditorTextBox is null)
        {
            return;
        }

        var y = 0d;
        var textView = EditorTextBox.TextArea.TextView;
        if (textView is not null)
        {
            y = -textView.VerticalOffset;
        }
        else if (_editorScrollViewer is not null)
        {
            y = -_editorScrollViewer.Offset.Y;
        }

        _lineNumbersTransform.Y = y;
        _gitDiffTransform.Y = y;
        ScheduleViewportFoldingRefreshFromScroll();
    }

    private void ScheduleViewportFoldingRefreshFromScroll()
    {
        if (!_isFoldingEnabled || _foldingManager is null || EditorTextBox is null)
        {
            return;
        }

        var lineCount = Math.Max(1, EditorTextBox.Document?.LineCount ?? 1);
        var textLength = EditorTextBox.Text?.Length ?? 0;
        if (lineCount <= LargeFileViewportFoldingLineThreshold && textLength <= LargeFileTextLengthThreshold)
        {
            return;
        }

        _scrollViewportFoldingCts?.Cancel();
        _scrollViewportFoldingCts?.Dispose();
        var cts = new CancellationTokenSource();
        _scrollViewportFoldingCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ScrollViewportFoldingDebounceMs, cts.Token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    UpdateFolding();
                });
            }
            catch (OperationCanceledException)
            {
                // Expected while scrolling quickly.
            }
            catch
            {
                // Ignore folding refresh failures from scroll-triggered refresh.
            }
            finally
            {
                if (ReferenceEquals(_scrollViewportFoldingCts, cts))
                {
                    _scrollViewportFoldingCts = null;
                }

                cts.Dispose();
            }
        });
    }

    private void SyncEditorFromDocument()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var next = _isGitDiffCompareActive
            ? _gitDiffComparePrimaryText
            : _viewModel.SelectedDocument?.Text ?? string.Empty;
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
        var next = _isGitDiffCompareActive
            ? _gitDiffCompareSecondaryText
            : _splitDocument?.Text ?? string.Empty;
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

        var lineCount = Math.Max(1, EditorTextBox.Document?.LineCount ?? 1);
        if (_lastRenderedLineCount != lineCount || string.IsNullOrEmpty(LineNumbersTextBlock.Text))
        {
            // Render 1..N only when the line count actually changes.
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
            _lastRenderedLineCount = lineCount;
        }

        var gutterLineHeight = EditorTextBox.TextArea.TextView.DefaultLineHeight;
        if (double.IsNaN(gutterLineHeight) || gutterLineHeight <= 0)
        {
            gutterLineHeight = Math.Max(14, EditorTextBox.FontSize * 1.25);
        }

        LineNumbersTextBlock.LineHeight = gutterLineHeight;
        UpdateGitDiffMarkerOverlay(_miniMapDiffMarkers);
        SyncGutterToScroll();
    }

    private void UpdateGitDiffGutter()
    {
        if (GitDiffMarkerCanvas is null || EditorTextBox is null)
        {
            return;
        }

        if (_isGitDiffCompareActive)
        {
            CancelPendingGitDiffGutterRefresh();
            var compareLineCount = Math.Max(1, EditorTextBox.Document?.LineCount ?? 1);
            var compareMarkers = _gitDiffCompareUsesExplicitDiffLines
                ? GitDiffCompareHighlightLogic.BuildMarkers(compareLineCount, _gitDiffComparePrimaryDiffLines, '-')
                : Array.Empty<char>();
            ApplyGitDiffMarkers(compareMarkers, Guid.Empty, -1, compareLineCount, null);
            return;
        }

        var timer = Stopwatch.StartNew();
        var deferTiming = false;
        try
        {
            var selectedDoc = _viewModel.SelectedDocument;
            var textLength = selectedDoc?.Text?.Length ?? EditorTextBox.Text?.Length ?? 0;
            var currentLineCount = Math.Max(1, EditorTextBox.Document?.LineCount ?? 1);
            var currentDocId = selectedDoc?.DocumentId ?? Guid.Empty;
            var currentDocVersion = selectedDoc?.ChangeVersion ?? -1;
            var currentPath = selectedDoc?.FilePath;

            if (currentDocId == _lastGitDiffDocumentId
                && currentDocVersion == _lastGitDiffChangeVersion
                && currentLineCount == _lastGitDiffLineCount
                && string.Equals(currentPath, _lastGitDiffFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (currentLineCount <= 0)
            {
                CancelPendingGitDiffGutterRefresh();
                ApplyGitDiffMarkers(Array.Empty<char>(), currentDocId, currentDocVersion, currentLineCount, currentPath);
                return;
            }

            if (textLength > LargeFileTextLengthThreshold || currentLineCount > LargeFileGitDiffLineThreshold)
            {
                CancelPendingGitDiffGutterRefresh();
                ApplyGitDiffMarkers(Array.Empty<char>(), currentDocId, currentDocVersion, currentLineCount, currentPath);
                return;
            }

            string? badgeSnapshot = null;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                try
                {
                    badgeSnapshot = GetGitBadgeForPath(Path.GetFullPath(currentPath));
                }
                catch
                {
                    badgeSnapshot = null;
                }
            }

            deferTiming = true;
            ScheduleGitDiffGutterRefresh(
                currentPath,
                currentLineCount,
                currentDocId,
                currentDocVersion,
                badgeSnapshot,
                timer);
        }
        finally
        {
            if (!deferTiming)
            {
                timer.Stop();
                RecordDiffPerf(timer.Elapsed.TotalMilliseconds);
            }
        }
    }

    private void ScheduleGitDiffGutterRefresh(
        string? filePath,
        int lineCount,
        Guid documentId,
        long changeVersion,
        string? badgeHint,
        Stopwatch timer)
    {
        if (_gitDiffRefreshCts is not null
            && _gitDiffPendingDocumentId == documentId
            && _gitDiffPendingChangeVersion == changeVersion
            && _gitDiffPendingLineCount == lineCount
            && string.Equals(_gitDiffPendingFilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            timer.Stop();
            return;
        }

        CancelPendingGitDiffGutterRefresh();

        var cts = new CancellationTokenSource();
        _gitDiffRefreshCts = cts;
        _gitDiffPendingDocumentId = documentId;
        _gitDiffPendingChangeVersion = changeVersion;
        _gitDiffPendingLineCount = lineCount;
        _gitDiffPendingFilePath = filePath;

        _ = Task.Run(async () =>
        {
            try
            {
                var markers = BuildGitDiffMarkersForCurrentFile(filePath, lineCount, badgeHint, cts.Token);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    var selectedDoc = _viewModel.SelectedDocument;
                    var selectedDocId = selectedDoc?.DocumentId ?? Guid.Empty;
                    var selectedDocVersion = selectedDoc?.ChangeVersion ?? -1;
                    var selectedPath = selectedDoc?.FilePath;
                    if (selectedDocId != documentId
                        || selectedDocVersion != changeVersion
                        || !string.Equals(selectedPath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    ApplyGitDiffMarkers(markers, documentId, changeVersion, lineCount, filePath);
                    timer.Stop();
                    RecordDiffPerf(timer.Elapsed.TotalMilliseconds);
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when a newer editor state supersedes this request.
            }
            catch
            {
                // Ignore background git diff failures and keep UI responsive.
            }
            finally
            {
                if (ReferenceEquals(_gitDiffRefreshCts, cts))
                {
                    _gitDiffRefreshCts = null;
                    _gitDiffPendingDocumentId = Guid.Empty;
                    _gitDiffPendingChangeVersion = -1;
                    _gitDiffPendingLineCount = -1;
                    _gitDiffPendingFilePath = null;
                }

                cts.Dispose();
            }
        });
    }

    private void CancelPendingGitDiffGutterRefresh()
    {
        _gitDiffRefreshCts?.Cancel();
        _gitDiffRefreshCts?.Dispose();
        _gitDiffRefreshCts = null;
        _gitDiffPendingDocumentId = Guid.Empty;
        _gitDiffPendingChangeVersion = -1;
        _gitDiffPendingLineCount = -1;
        _gitDiffPendingFilePath = null;
    }

    private void ApplyGitDiffMarkers(char[] markers, Guid documentId, long changeVersion, int lineCount, string? filePath)
    {
        if (GitDiffMarkerCanvas is null || EditorTextBox is null)
        {
            return;
        }

        if (markers.Length == 0)
        {
            _miniMapDiffMarkers = Array.Empty<char>();
            UpdateMiniMapDiffOverlay();
            _gitDiffBackgroundRenderer?.Clear();
            _gitDiffLineColorizer?.Clear();
        }
        else
        {
            _miniMapDiffMarkers = markers;
            UpdateMiniMapDiffOverlay();
            _gitDiffBackgroundRenderer?.SetMarkers(markers);
            _gitDiffLineColorizer?.SetMarkers(markers);
        }

        UpdateGitDiffMarkerOverlay(_miniMapDiffMarkers);
        EditorTextBox.TextArea.TextView.InvalidateVisual();
        _lastGitDiffDocumentId = documentId;
        _lastGitDiffChangeVersion = changeVersion;
        _lastGitDiffLineCount = lineCount;
        _lastGitDiffFilePath = filePath;
    }

    private void UpdateGitDiffMarkerOverlay(IReadOnlyList<char> markers)
    {
        if (GitDiffMarkerCanvas is null || EditorTextBox is null)
        {
            return;
        }

        GitDiffMarkerCanvas.Children.Clear();
        ToolTip.SetTip(GitDiffMarkerCanvas, GitDiffMarkerPresentationLogic.BuildTooltip(markers));

        if (markers.Count == 0)
        {
            GitDiffMarkerCanvas.Height = Math.Max(1, EditorTextBox.Bounds.Height);
            return;
        }

        var lineHeight = EditorTextBox.TextArea.TextView.DefaultLineHeight;
        if (double.IsNaN(lineHeight) || lineHeight <= 0)
        {
            lineHeight = Math.Max(14, EditorTextBox.FontSize * 1.25);
        }

        var fontSize = Math.Clamp(EditorTextBox.FontSize - 3, 10, 16);
        var fontFamily = EditorTextBox.FontFamily;
        GitDiffMarkerCanvas.Height = Math.Max(1, lineHeight * markers.Count);

        foreach (var marker in GitDiffMarkerPresentationLogic.GetVisibleMarkers(markers))
        {
            var markerBrush = GetGitDiffMarkerBrush(marker.Kind, accent: true);
            var fillBrush = GetGitDiffMarkerBrush(marker.Kind, accent: false);
            var top = Math.Max(0, (marker.LineNumber - 1) * lineHeight);
            var barHeight = Math.Max(6, lineHeight - 2);

            var bar = new Border
            {
                Width = 4,
                Height = barHeight,
                Background = fillBrush,
                CornerRadius = new CornerRadius(2),
            };
            Canvas.SetLeft(bar, 1);
            Canvas.SetTop(bar, top + Math.Max(0, (lineHeight - barHeight) / 2));
            GitDiffMarkerCanvas.Children.Add(bar);

            var glyph = new TextBlock
            {
                Text = marker.Glyph.ToString(),
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = FontWeight.Bold,
                Foreground = markerBrush,
                Opacity = 0.95,
            };
            Canvas.SetLeft(glyph, 8);
            Canvas.SetTop(glyph, top - 1);
            GitDiffMarkerCanvas.Children.Add(glyph);
        }
    }

    private IBrush GetGitDiffMarkerBrush(char kind, bool accent)
    {
        var isLight = string.Equals(_themeMode, "Light", StringComparison.Ordinal);
        return kind switch
        {
            '+' => new SolidColorBrush(Color.Parse(accent
                ? (isLight ? "#21873C" : "#3FB950")
                : (isLight ? "#7EDB963D" : "#3FB95055"))),
            '-' => new SolidColorBrush(Color.Parse(accent
                ? (isLight ? "#C93C37" : "#F85149")
                : (isLight ? "#F299993C" : "#F8514950"))),
            '~' => new SolidColorBrush(Color.Parse(accent
                ? (isLight ? "#A66500" : "#D29922")
                : (isLight ? "#F2C94C40" : "#D2992250"))),
            _ => Brushes.Transparent,
        };
    }

    private void ApplyEditorReadOnlyModes()
    {
        if (EditorTextBox is not null)
        {
            EditorTextBox.IsReadOnly = _isGitDiffCompareActive;
        }

        if (SplitEditorTextBox is not null)
        {
            SplitEditorTextBox.IsReadOnly = _isGitDiffCompareActive;
        }
    }

    private void UpdateEditorCompareTitles()
    {
        if (PrimaryEditorTitleTextBlock is not null)
        {
            PrimaryEditorTitleTextBlock.IsVisible = _isGitDiffCompareActive;
            PrimaryEditorTitleTextBlock.Text = _gitDiffComparePrimaryTitle;
        }

        if (SplitEditorTitleTextBlock is not null)
        {
            SplitEditorTitleTextBlock.IsVisible = _isGitDiffCompareActive;
            if (_isGitDiffCompareActive)
            {
                SplitEditorTitleTextBlock.Text = _gitDiffCompareSecondaryTitle;
            }
        }
    }

    private void ActivateGitDiffCompareSession(
        string? targetPath,
        string primaryTitle,
        string primaryText,
        string secondaryTitle,
        string secondaryText,
        GitDiffCompareLineMap? explicitDiffLines = null)
    {
        if (!_isGitDiffCompareActive)
        {
            _gitDiffComparePreviousSplitViewEnabled = _isSplitViewEnabled;
            _gitDiffComparePreviousSplitDocument = _splitDocument;
            _gitDiffComparePreviousSplitCompareMode = _splitCompareMode;
        }

        _isGitDiffCompareActive = true;
        _gitDiffCompareTargetPath = targetPath;
        _gitDiffComparePrimaryTitle = primaryTitle;
        _gitDiffComparePrimaryText = primaryText ?? string.Empty;
        _gitDiffCompareSecondaryTitle = secondaryTitle;
        _gitDiffCompareSecondaryText = secondaryText ?? string.Empty;
        _gitDiffCompareUsesExplicitDiffLines = explicitDiffLines.HasValue;
        _gitDiffComparePrimaryDiffLines = explicitDiffLines?.PrimaryLines ?? Array.Empty<int>();
        _gitDiffCompareSecondaryDiffLines = explicitDiffLines?.SecondaryLines ?? Array.Empty<int>();
        _isSplitViewEnabled = true;
        _splitCompareMode = "Compare";

        SyncEditorFromDocument();
        SyncSplitEditorFromDocument();
        ApplySplitView();
        UpdateGitDiffGutter();
        UpdateGitOpenButtonState(GitChangesTreeView?.SelectedItem as GitChangeTreeNode);
        PersistState();
    }

    private void DeactivateGitDiffCompareSession()
    {
        if (!_isGitDiffCompareActive)
        {
            return;
        }

        _isGitDiffCompareActive = false;
        _gitDiffCompareTargetPath = null;
        _gitDiffComparePrimaryText = string.Empty;
        _gitDiffCompareSecondaryText = string.Empty;
        _gitDiffComparePrimaryTitle = "Git Base";
        _gitDiffCompareSecondaryTitle = "Working Tree";
        _gitDiffCompareUsesExplicitDiffLines = false;
        _gitDiffComparePrimaryDiffLines = Array.Empty<int>();
        _gitDiffCompareSecondaryDiffLines = Array.Empty<int>();
        _isSplitViewEnabled = _gitDiffComparePreviousSplitViewEnabled;
        _splitDocument = _gitDiffComparePreviousSplitDocument;
        _splitCompareMode = _gitDiffComparePreviousSplitCompareMode;
        _gitDiffComparePreviousSplitDocument = null;

        SyncEditorFromDocument();
        SyncSplitEditorFromDocument();
        ApplySplitView();
        UpdateGitDiffGutter();
        UpdateGitOpenButtonState(GitChangesTreeView?.SelectedItem as GitChangeTreeNode);
        PersistState();
    }

    private void InvalidateGitDiffGutterCache()
    {
        CancelPendingGitDiffGutterRefresh();
        _lastGitDiffDocumentId = Guid.Empty;
        _lastGitDiffChangeVersion = -1;
        _lastGitDiffLineCount = -1;
        _lastGitDiffFilePath = null;
    }

    private char[] BuildGitDiffMarkersForCurrentFile(
        string? filePath,
        int lineCount,
        string? badgeHint = null,
        CancellationToken cancellationToken = default)
    {
        var markers = Enumerable.Repeat(' ', Math.Max(1, lineCount)).ToArray();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return markers;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return markers;
        }

        var fullPath = Path.GetFullPath(filePath);
        var badge = string.IsNullOrWhiteSpace(badgeHint)
            ? GetGitBadgeForPath(fullPath)
            : badgeHint;
        if (string.Equals(badge, "A", StringComparison.Ordinal) || string.Equals(badge, "?", StringComparison.Ordinal))
        {
            Array.Fill(markers, '+');
            return markers;
        }

        // Fast-path unchanged files to avoid spawning git diff processes while switching tabs.
        // If git status cache is empty, we still fall back to git diff for correctness.
        if (string.IsNullOrWhiteSpace(badge) && _gitStatusByPath.Count > 0)
        {
            return markers;
        }

        if (!File.Exists(fullPath))
        {
            return markers;
        }

        if (cancellationToken.IsCancellationRequested)
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
        var diffResult = RunGit(repoRoot, $"diff --no-color --unified=0 HEAD -- \"{escapedPath}\"", timeoutMs: 3500, cancellationToken: cancellationToken);
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

        var timer = Stopwatch.StartNew();
        try
        {
            if (FoldingEnabledMenuItem is not null)
            {
                FoldingEnabledMenuItem.IsChecked = _isFoldingEnabled;
            }

            if (!_isFoldingEnabled)
            {
                CancelPendingFullFoldingRebuild();
                _foldingManager.Clear();
                return;
            }

            var language = _viewModel.StatusLanguage;
            if (!IsStructuralLanguage(language))
            {
                CancelPendingFullFoldingRebuild();
                _foldingManager.Clear();
                return;
            }

            var text = EditorTextBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                CancelPendingFullFoldingRebuild();
                _foldingManager.Clear();
                return;
            }

            var lineCount = Math.Max(1, EditorTextBox.Document?.LineCount ?? 1);
            var selectedDoc = _viewModel.SelectedDocument;
            var docId = selectedDoc?.DocumentId ?? Guid.Empty;
            var docVersion = selectedDoc?.ChangeVersion ?? -1;

            var shouldUseViewportFolding = lineCount > LargeFileViewportFoldingLineThreshold
                || text.Length > LargeFileTextLengthThreshold;

            if (shouldUseViewportFolding)
            {
                var (startOffset, endOffset) = GetApproxVisibleDocumentOffsetRange(ViewportFoldingPaddingLines);
                if (endOffset > startOffset && startOffset >= 0 && endOffset <= text.Length)
                {
                    var windowText = text.Substring(startOffset, endOffset - startOffset);
                    var viewportFoldings = BuildFoldings(windowText, startOffset);
                    _foldingManager.UpdateFoldings(viewportFoldings, -1);
                }
                else
                {
                    _foldingManager.Clear();
                }

                ScheduleFullFoldingRebuild(text, docId, docVersion);
                return;
            }

            CancelPendingFullFoldingRebuild();
            var foldings = BuildFoldings(text);
            _foldingManager.UpdateFoldings(foldings, -1);
        }
        finally
        {
            timer.Stop();
            RecordFoldingPerf(timer.Elapsed.TotalMilliseconds);
        }
    }

    private static bool IsStructuralLanguage(string? language)
    {
        return language is "C#" or "JavaScript" or "TypeScript" or "JSON" or "CSS" or "SQL" or "XML" or "HTML";
    }

    private void CancelPendingFullFoldingRebuild()
    {
        _fullFoldingRebuildCts?.Cancel();
        _fullFoldingRebuildCts?.Dispose();
        _fullFoldingRebuildCts = null;
        _fullFoldingRebuildDocId = Guid.Empty;
        _fullFoldingRebuildVersion = -1;
    }

    private void ScheduleFullFoldingRebuild(string text, Guid docId, long docVersion)
    {
        if (_fullFoldingRebuildCts is not null
            && _fullFoldingRebuildDocId == docId
            && _fullFoldingRebuildVersion == docVersion)
        {
            return;
        }

        CancelPendingFullFoldingRebuild();

        var cts = new CancellationTokenSource();
        _fullFoldingRebuildCts = cts;
        _fullFoldingRebuildDocId = docId;
        _fullFoldingRebuildVersion = docVersion;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FullFoldingRebuildDebounceMs, cts.Token).ConfigureAwait(false);
                var foldings = BuildFoldings(text);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested || _foldingManager is null || !_isFoldingEnabled)
                    {
                        return;
                    }

                    var selectedDoc = _viewModel.SelectedDocument;
                    var selectedDocId = selectedDoc?.DocumentId ?? Guid.Empty;
                    var selectedDocVersion = selectedDoc?.ChangeVersion ?? -1;
                    if (selectedDocId != docId || selectedDocVersion != docVersion)
                    {
                        return;
                    }

                    _foldingManager.UpdateFoldings(foldings, -1);
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when a newer edit or document selection supersedes this rebuild.
            }
            catch
            {
                // Keep editing responsive even if a background folding pass fails.
            }
            finally
            {
                if (ReferenceEquals(_fullFoldingRebuildCts, cts))
                {
                    _fullFoldingRebuildCts = null;
                    _fullFoldingRebuildDocId = Guid.Empty;
                    _fullFoldingRebuildVersion = -1;
                }

                cts.Dispose();
            }
        });
    }

    private (int startOffset, int endOffset) GetApproxVisibleDocumentOffsetRange(int paddingLines)
    {
        var text = EditorTextBox?.Text ?? string.Empty;
        var doc = EditorTextBox?.Document;
        if (doc is null)
        {
            return (0, text.Length);
        }

        var lineCount = Math.Max(1, doc.LineCount);
        var textView = EditorTextBox!.TextArea.TextView;
        var lineHeight = textView.DefaultLineHeight;
        if (double.IsNaN(lineHeight) || lineHeight <= 0)
        {
            lineHeight = Math.Max(14, EditorTextBox.FontSize * 1.25);
        }

        var verticalOffset = Math.Max(0, textView.VerticalOffset);
        var viewportHeight = textView.Bounds.Height;
        if (double.IsNaN(viewportHeight) || viewportHeight <= 0)
        {
            viewportHeight = _editorScrollViewer?.Viewport.Height ?? 0;
        }

        if (double.IsNaN(viewportHeight) || viewportHeight <= 0)
        {
            viewportHeight = lineHeight * 45;
        }

        var firstVisibleLine = Math.Max(1, (int)Math.Floor(verticalOffset / Math.Max(1, lineHeight)) + 1);
        var visibleLineCount = Math.Max(1, (int)Math.Ceiling(viewportHeight / Math.Max(1, lineHeight)));
        var firstLine = Math.Clamp(firstVisibleLine - paddingLines, 1, lineCount);
        var lastLine = Math.Clamp(firstVisibleLine + visibleLineCount + paddingLines, 1, lineCount);
        if (lastLine < firstLine)
        {
            lastLine = firstLine;
        }

        var startOffset = doc.GetLineByNumber(firstLine).Offset;
        var endOffset = doc.GetLineByNumber(lastLine).EndOffset;
        endOffset = Math.Clamp(endOffset, startOffset, text.Length);
        return (startOffset, endOffset);
    }

    private static List<NewFolding> BuildFoldings(string text, int baseOffset = 0)
    {
        var foldings = new List<NewFolding>();
        if (string.IsNullOrEmpty(text))
        {
            return foldings;
        }

        var braces = new Stack<(int Offset, int Line)>();
        var currentLine = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '{')
            {
                braces.Push((i, currentLine));
            }
            else if (ch == '}' && braces.Count > 0)
            {
                var (startOffset, startLine) = braces.Pop();
                if (currentLine > startLine)
                {
                    foldings.Add(new NewFolding(baseOffset + startOffset, baseOffset + i + 1) { Name = "{...}" });
                }
            }

            if (ch == '\n')
            {
                currentLine++;
            }
        }

        var regions = new Stack<int>();
        var lineStart = 0;
        while (lineStart < text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var lineSpan = text.AsSpan(lineStart, lineEnd - lineStart);
            var trimStart = 0;
            while (trimStart < lineSpan.Length && (lineSpan[trimStart] is ' ' or '\t'))
            {
                trimStart++;
            }

            var trimmed = lineSpan.Slice(trimStart);
            if (trimmed.StartsWith("#region".AsSpan(), StringComparison.Ordinal))
            {
                regions.Push(lineStart);
            }
            else if (trimmed.StartsWith("#endregion".AsSpan(), StringComparison.Ordinal) && regions.Count > 0)
            {
                var start = regions.Pop();
                var end = lineEnd;
                if (end > start)
                {
                    foldings.Add(new NewFolding(baseOffset + start, baseOffset + end) { Name = "#region" });
                }
            }

            if (lineEnd >= text.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        foldings.Sort((left, right) =>
        {
            var compare = left.StartOffset.CompareTo(right.StartOffset);
            return compare != 0 ? compare : left.EndOffset.CompareTo(right.EndOffset);
        });
        return foldings;
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

        if (MiniMapTextBlock is not null)
        {
            MiniMapTextBlock.FontFamily = family;
        }

        UpdateGitDiffMarkerOverlay(_miniMapDiffMarkers);
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

        ApplyFoldingMarginTheme(EditorTextBox);
        ApplyFoldingMarginTheme(SplitEditorTextBox);

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

        _gitDiffLineColorizer?.SetTheme(_themeMode);
        _gitDiffBackgroundRenderer?.SetTheme(_themeMode);
        _splitComparePrimaryColorizer?.SetTheme(_themeMode);
        _splitCompareSecondaryColorizer?.SetTheme(_themeMode);
        UpdateGitDiffMarkerOverlay(_miniMapDiffMarkers);
        UpdateSplitCompareHighlights();
        EditorTextBox?.TextArea.TextView.InvalidateVisual();
    }

    private void ApplyFoldingMarginTheme(TextEditor? editor)
    {
        if (editor?.TextArea is null)
        {
            return;
        }

        var isLight = string.Equals(_themeMode, "Light", StringComparison.Ordinal);
        var markerBrush = new SolidColorBrush(Color.Parse(isLight ? "#5A6F85" : "#4D6375"));
        var markerBackground = new SolidColorBrush(Color.Parse(isLight ? "#E8EEF5" : "#1B2732"));
        var selectedMarkerBrush = new SolidColorBrush(Color.Parse(isLight ? "#2C4C66" : "#8EB8D8"));
        var selectedMarkerBackground = new SolidColorBrush(Color.Parse(isLight ? "#D7E5F2" : "#243645"));

        // Apply as attached values as well so newly materialized folding visuals inherit
        // themed colors even when the control regenerates marker elements.
        editor.SetValue(FoldingMargin.FoldingMarkerBrushProperty, markerBrush);
        editor.SetValue(FoldingMargin.FoldingMarkerBackgroundBrushProperty, markerBackground);
        editor.SetValue(FoldingMargin.SelectedFoldingMarkerBrushProperty, selectedMarkerBrush);
        editor.SetValue(FoldingMargin.SelectedFoldingMarkerBackgroundBrushProperty, selectedMarkerBackground);
        editor.TextArea.SetValue(FoldingMargin.FoldingMarkerBrushProperty, markerBrush);
        editor.TextArea.SetValue(FoldingMargin.FoldingMarkerBackgroundBrushProperty, markerBackground);
        editor.TextArea.SetValue(FoldingMargin.SelectedFoldingMarkerBrushProperty, selectedMarkerBrush);
        editor.TextArea.SetValue(FoldingMargin.SelectedFoldingMarkerBackgroundBrushProperty, selectedMarkerBackground);

        foreach (var margin in editor.TextArea.LeftMargins.OfType<FoldingMargin>())
        {
            margin.FoldingMarkerBrush = markerBrush;
            margin.FoldingMarkerBackgroundBrush = markerBackground;
            margin.SelectedFoldingMarkerBrush = selectedMarkerBrush;
            margin.SelectedFoldingMarkerBackgroundBrush = selectedMarkerBackground;
            margin.Opacity = isLight ? 1 : 0.84;
        }
    }
}

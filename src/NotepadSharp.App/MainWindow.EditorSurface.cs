using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using NotepadSharp.App.ViewModels;

namespace NotepadSharp.App;

public partial class MainWindow
{
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
        UpdateSplitCompareHighlights();
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
            _splitComparePrimaryColorizer.Clear();
            _splitCompareSecondaryColorizer.Clear();
            EditorTextBox.TextArea.TextView.InvalidateVisual();
            SplitEditorTextBox.TextArea.TextView.InvalidateVisual();
            return;
        }

        var leftLines = NormalizeLines(EditorTextBox.Text ?? string.Empty);
        var rightLines = NormalizeLines(SplitEditorTextBox.Text ?? string.Empty);
        var (leftDiffLines, rightDiffLines) = ComputeSplitCompareDiffLines(leftLines, rightLines);

        var showDiffOnly = string.Equals(mode, "Show diff only", StringComparison.Ordinal);
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

        ApplyFoldingMarginTheme(EditorTextBox);
        ApplyFoldingMarginTheme(SplitEditorTextBox);

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
        _splitComparePrimaryColorizer?.SetTheme(_themeMode);
        _splitCompareSecondaryColorizer?.SetTheme(_themeMode);
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

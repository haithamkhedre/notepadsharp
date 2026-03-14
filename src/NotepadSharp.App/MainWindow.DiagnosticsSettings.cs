using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.CodeAnalysis.CSharp;
using NotepadSharp.App.Services;
using NotepadSharp.App.ViewModels;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private void UpdateDiagnostics()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        _diagnosticEntries.Clear();
        var text = EditorTextBox.Text ?? string.Empty;
        var lineCount = Math.Max(1, EditorTextBox.Document?.LineCount ?? 1);
        var language = _viewModel.StatusLanguage;

        if (ShouldSkipDiagnosticsForLargeFile(text, lineCount))
        {
            RenderDiagnosticsUi("Diagnostics paused for large file.");
            return;
        }

        try
        {
            switch (language)
            {
                case "C#":
                {
                    var sources = _viewModel.SelectedDocument is null
                        ? Array.Empty<CSharpDefinitionSource>()
                        : BuildOpenCSharpDefinitionSources(_viewModel.SelectedDocument, text);
                    foreach (var diag in CSharpDiagnosticsLogic.GetDiagnostics(sources))
                    {
                        _diagnosticEntries.Add(new DiagnosticEntry(diag.Severity, diag.Line, diag.Column, diag.Message));
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
                    catch (XmlException ex)
                    {
                        var diagnostic = StructuredDocumentDiagnosticLogic.FromXmlException(ex);
                        _diagnosticEntries.Add(new DiagnosticEntry("Error", diagnostic.Line, diagnostic.Column, diagnostic.Message));
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
                    catch (YamlException ex)
                    {
                        var diagnostic = StructuredDocumentDiagnosticLogic.FromYamlException(ex);
                        _diagnosticEntries.Add(new DiagnosticEntry("Error", diagnostic.Line, diagnostic.Column, diagnostic.Message));
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

                    var result = TryRunSyntaxTool("python3", "-m py_compile \"{file}\"", text, ".py");
                    AddSyntaxToolDiagnostics(result, SyntaxToolDiagnosticLogic.ParsePython);

                    break;
                }
                case "JavaScript":
                {
                    if (!ShouldRunExternalDiagnostics(text))
                    {
                        break;
                    }

                    var result = TryRunSyntaxTool("node", "--check \"{file}\"", text, ".js");
                    AddSyntaxToolDiagnostics(result, SyntaxToolDiagnosticLogic.ParseJavaScript);

                    break;
                }
                case "TypeScript":
                {
                    if (!ShouldRunExternalDiagnostics(text))
                    {
                        break;
                    }

                    var result = TryRunSyntaxTool("tsc", "--pretty false --noEmit \"{file}\"", text, ".ts");
                    AddSyntaxToolDiagnostics(result, SyntaxToolDiagnosticLogic.ParseTypeScript);

                    break;
                }
            }
        }
        catch
        {
            // Keep diagnostics best-effort and non-blocking.
        }

        RenderDiagnosticsUi();
    }

    private static bool ShouldSkipDiagnosticsForLargeFile(string text, int lineCount)
        => text.Length > 600_000 || lineCount > 14_000;

    private void RenderDiagnosticsUi(string? summaryOverride = null)
    {
        var severities = _diagnosticEntries.Select(entry => entry.Severity);
        if (StatusDiagnosticsTextBlock is not null)
        {
            StatusDiagnosticsTextBlock.Text = DiagnosticSummaryLogic.FormatStatusText(severities);
        }

        if (DiagnosticsSummaryTextBlock is not null)
        {
            DiagnosticsSummaryTextBlock.Text = summaryOverride ?? DiagnosticSummaryLogic.FormatSummaryText(_diagnosticEntries.Select(entry => entry.Severity));
        }

        if (DiagnosticsListBox is not null)
        {
            DiagnosticsListBox.ItemsSource = _diagnosticEntries.ToList();
        }
    }

    private void OnDiagnosticsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        NavigateToSelectedDiagnostic();
    }

    private void OnDiagnosticsDoubleTapped(object? sender, TappedEventArgs e)
    {
        NavigateToSelectedDiagnostic();
    }

    private void NavigateToSelectedDiagnostic()
    {
        if (EditorTextBox is null || DiagnosticsListBox?.SelectedItem is not DiagnosticEntry entry)
        {
            return;
        }

        NavigateToDiagnostic(entry);
    }

    private void OnNextDiagnosticClick(object? sender, RoutedEventArgs e)
        => NavigateDiagnostics(forward: true);

    private void OnPreviousDiagnosticClick(object? sender, RoutedEventArgs e)
        => NavigateDiagnostics(forward: false);

    private void NavigateDiagnostics(bool forward)
    {
        if (EditorTextBox is null || _diagnosticEntries.Count == 0)
        {
            return;
        }

        var caretText = EditorTextBox.Text ?? string.Empty;
        var (line, column) = GetLineColumn(caretText, EditorTextBox.CaretOffset);
        var ordered = _diagnosticEntries
            .OrderBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ToList();

        DiagnosticEntry target;
        if (forward)
        {
            target = ordered.FirstOrDefault(item => item.Line > line || (item.Line == line && item.Column > column))
                ?? ordered[0];
        }
        else
        {
            target = ordered.LastOrDefault(item => item.Line < line || (item.Line == line && item.Column < column))
                ?? ordered[^1];
        }

        if (DiagnosticsListBox is not null)
        {
            DiagnosticsListBox.SelectedItem = target;
        }

        NavigateToDiagnostic(target);
    }

    private void NavigateToDiagnostic(DiagnosticEntry entry)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        GoToLine(EditorTextBox, entry.Line, entry.Column);
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

    private void AddSyntaxToolDiagnostics(
        SyntaxToolRunResult result,
        Func<string, IReadOnlyList<SyntaxToolDiagnosticInfo>> parseDiagnostics)
    {
        if (result.Success)
        {
            return;
        }

        var diagnostics = parseDiagnostics(result.CombinedOutput);
        if (diagnostics.Count > 0)
        {
            foreach (var diagnostic in diagnostics)
            {
                _diagnosticEntries.Add(new DiagnosticEntry(
                    diagnostic.Severity,
                    diagnostic.Line,
                    diagnostic.Column,
                    diagnostic.Message));
            }

            return;
        }

        var summary = SyntaxToolDiagnosticLogic.SummarizeOutput(result.StandardOutput, result.StandardError);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            _diagnosticEntries.Add(new DiagnosticEntry("Warning", 1, 1, summary));
        }
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

        var normalizedValue = value.Trim();
        var selected = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString()?.Trim(), normalizedValue, StringComparison.OrdinalIgnoreCase));

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

}

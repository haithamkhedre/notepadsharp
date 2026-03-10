using System;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.CodeAnalysis.CSharp;
using NotepadSharp.App.ViewModels;
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

        RenderDiagnosticsUi();
    }

    private static bool ShouldSkipDiagnosticsForLargeFile(string text, int lineCount)
        => text.Length > 600_000 || lineCount > 14_000;

    private void RenderDiagnosticsUi(string? summaryOverride = null)
    {
        if (StatusDiagnosticsTextBlock is not null)
        {
            StatusDiagnosticsTextBlock.Text = $"Diagnostics: {_diagnosticEntries.Count}";
        }

        if (DiagnosticsSummaryTextBlock is not null)
        {
            DiagnosticsSummaryTextBlock.Text = summaryOverride ?? (_diagnosticEntries.Count == 0
                ? "No diagnostics."
                : $"{_diagnosticEntries.Count} diagnostics");
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

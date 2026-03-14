using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NotepadSharp.App.Dialogs;
using NotepadSharp.App.Services;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private const int MaxAiHistoryEntries = 24;

    private sealed record AiActionContext(
        string ScopeMode,
        string LanguageMode,
        string DocumentText,
        string SourceText,
        int EditStart,
        int EditLength,
        string UserPrompt,
        string WorkspaceSummary);

    private sealed record AiActionResult(
        string Title,
        string Status,
        string Output,
        bool HasEdit,
        int EditStart,
        int EditLength,
        string ReplacementText,
        string OriginalPreview,
        string ProposedPreview);

    private sealed record AiHistoryEntry(
        DateTimeOffset Timestamp,
        string Label,
        string Prompt,
        string Output,
        string Status)
    {
        public override string ToString()
        {
            var prompt = string.IsNullOrWhiteSpace(Prompt) ? "(no prompt)" : FirstLine(Prompt);
            return $"{Timestamp:HH:mm:ss}  {Label}  {prompt}";
        }
    }

    private readonly List<AiHistoryEntry> _aiHistory = new();
    private string _aiQuickCommand = SmartActionLogic.DefaultQuickCommand;
    private bool _isUpdatingAiAssistantSelectors;

    private void OnShowAiAssistantClick(object? sender, RoutedEventArgs e)
    {
        SetSidebarSection(SmartActionLogic.SidebarSectionName, persist: true);
        FocusAiPrompt();
    }

    private void OnAiScopeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingAiAssistantSelectors)
        {
            return;
        }

        if (AiScopeComboBox is null)
        {
            return;
        }

        var selected = GetComboSelection(AiScopeComboBox);
        _aiScopeMode = NormalizeAiScopeMode(selected);
        SetAiStatus($"Scope: {_aiScopeMode}");
    }

    private void OnAiQuickCommandSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingAiAssistantSelectors || AiQuickCommandComboBox is null)
        {
            return;
        }

        _aiQuickCommand = NormalizeAiQuickCommand(GetComboSelection(AiQuickCommandComboBox));
        if (string.Equals(_aiQuickCommand, SmartActionLogic.DefaultQuickCommand, StringComparison.Ordinal))
        {
            SetAiStatus("Quick mode: Auto (local intent inferred from your prompt).");
            return;
        }

        if (AiPromptTextBox is null)
        {
            return;
        }

        var existing = AiPromptTextBox.Text?.Trim() ?? string.Empty;
        var cleaned = RemoveLeadingSlashCommand(existing);
        AiPromptTextBox.Text = string.IsNullOrWhiteSpace(cleaned)
            ? _aiQuickCommand
            : $"{_aiQuickCommand} {cleaned}";
        SetAiStatus($"Quick mode: {_aiQuickCommand}");
    }

    private void OnAiProviderEnabledClick(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingAiAssistantSelectors || AiProviderEnabledCheckBox is null)
        {
            return;
        }

        _aiProviderEnabled = AiProviderEnabledCheckBox.IsChecked == true;
        UpdateAiAssistantUi();
        SetAiStatus(GetAiProviderAvailabilityState().Description);
        PersistState();
    }

    private void OnAiProviderModelTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingAiAssistantSelectors || AiProviderModelTextBox is null)
        {
            return;
        }

        _aiProviderModel = AiProviderConfigLogic.NormalizeModel(AiProviderModelTextBox.Text);
        UpdateAiAssistantUi();
        PersistState();
    }

    private void OnAiProviderEndpointTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingAiAssistantSelectors || AiProviderEndpointTextBox is null)
        {
            return;
        }

        _aiProviderEndpoint = AiProviderConfigLogic.NormalizeEndpoint(AiProviderEndpointTextBox.Text);
        UpdateAiAssistantUi();
        PersistState();
    }

    private void OnAiProviderApiKeyEnvVarTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingAiAssistantSelectors || AiProviderApiKeyEnvVarTextBox is null)
        {
            return;
        }

        _aiProviderApiKeyEnvironmentVariable = AiProviderConfigLogic.NormalizeApiKeyEnvironmentVariable(AiProviderApiKeyEnvVarTextBox.Text);
        UpdateAiAssistantUi();
        PersistState();
    }

    private async void OnAiPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return;
        }

        e.Handled = true;
        await RunAiActionAsync(SmartActionKind.Ask);
    }

    private async void OnAiAskClick(object? sender, RoutedEventArgs e)
        => await RunAiActionAsync(SmartActionKind.Ask);

    private async void OnAiExplainClick(object? sender, RoutedEventArgs e)
        => await RunAiActionAsync(SmartActionKind.Explain);

    private async void OnAiRefactorClick(object? sender, RoutedEventArgs e)
        => await RunAiActionAsync(SmartActionKind.Refactor);

    private async void OnAiFixDiagnosticsClick(object? sender, RoutedEventArgs e)
        => await RunAiActionAsync(SmartActionKind.FixDiagnostics);

    private async void OnAiGenerateTestsClick(object? sender, RoutedEventArgs e)
        => await RunAiActionAsync(SmartActionKind.GenerateTests);

    private async void OnAiCommitMessageClick(object? sender, RoutedEventArgs e)
        => await RunAiActionAsync(SmartActionKind.CommitMessage);

    private void OnAiCancelClick(object? sender, RoutedEventArgs e)
    {
        _aiRequestCts?.Cancel();
        SetAiStatus("Smart action canceled.");
    }

    private async void OnAiCopyOutputClick(object? sender, RoutedEventArgs e)
    {
        var output = AiOutputTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(output))
        {
            SetAiStatus("No smart-action output to copy.");
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            SetAiStatus("Clipboard unavailable.");
            return;
        }

        await clipboard.SetTextAsync(output);
        SetAiStatus("Smart-action output copied.");
    }

    private void OnAiInsertOutputClick(object? sender, RoutedEventArgs e)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var output = AiOutputTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(output))
        {
            SetAiStatus("No smart-action output to insert.");
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, text.Length);
        var updated = text.Insert(caret, output);
        EditorTextBox.Text = updated;
        EditorTextBox.CaretOffset = Math.Min(caret + output.Length, updated.Length);
        SetSelection(EditorTextBox.CaretOffset, EditorTextBox.CaretOffset);
        SetAiStatus("Smart-action output inserted at cursor.");
    }

    private void OnAiReplaceSelectionWithOutputClick(object? sender, RoutedEventArgs e)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var output = AiOutputTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(output))
        {
            SetAiStatus("No smart-action output to apply.");
            return;
        }

        if (EditorTextBox.SelectionLength <= 0)
        {
            SetAiStatus("Select text first, then use Replace selection.");
            return;
        }

        var start = Math.Min(EditorTextBox.SelectionStart, GetSelectionEnd());
        var end = Math.Max(EditorTextBox.SelectionStart, GetSelectionEnd());
        ApplyAiEdit(start, end - start, output, selectReplacement: true);
        SetAiStatus("Selection replaced with smart-action output.");
    }

    private void OnAiClearOutputClick(object? sender, RoutedEventArgs e)
    {
        if (AiOutputTextBox is not null)
        {
            AiOutputTextBox.Text = string.Empty;
        }

        if (AiHistoryComboBox is not null)
        {
            AiHistoryComboBox.SelectedItem = null;
        }

        SetAiStatus("Smart-action output cleared.");
    }

    private void OnAiHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingAiAssistantSelectors || AiHistoryComboBox?.SelectedItem is not AiHistoryEntry selected)
        {
            return;
        }

        if (AiPromptTextBox is not null)
        {
            AiPromptTextBox.Text = selected.Prompt;
        }

        if (AiOutputTextBox is not null)
        {
            AiOutputTextBox.Text = selected.Output;
        }

        SetAiStatus(selected.Status);
    }

    private async Task ShowAiAssistantInlineAsync()
    {
        SetSidebarSection(SmartActionLogic.SidebarSectionName, persist: true);
        if (EditorTextBox is null)
        {
            return;
        }

        if (EditorTextBox.SelectionLength > 0)
        {
            _aiScopeMode = "Selection only";
            UpdateAiScopeSelector();
            await RunAiActionAsync(SmartActionKind.Explain);
            return;
        }

        SetAiStatus(ShouldUseAiProviderForAction(SmartActionKind.Explain)
            ? "Select text first, then press Ctrl/Cmd+K for an OpenAI explain pass."
            : "Select text first, then press Ctrl/Cmd+K for a local explain pass.");
        FocusAiPrompt();
    }

    private async Task RunAiActionAsync(SmartActionKind action)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var useProvider = ShouldUseAiProviderForAction(action);
        if (useProvider)
        {
            var providerState = GetAiProviderAvailabilityState();
            if (providerState.Availability != AiProviderAvailability.Ready)
            {
                SetAiStatus(providerState.Description);
                return;
            }
        }

        if (!TryBuildAiActionContext(action, out var context, out var error))
        {
            SetAiStatus(error);
            return;
        }

        var actionToRun = action;
        if (useProvider)
        {
            context = context with { UserPrompt = RemoveLeadingSlashCommand(context.UserPrompt) };
            if (action == SmartActionKind.Ask && string.IsNullOrWhiteSpace(context.UserPrompt))
            {
                SetAiStatus("Describe what you want OpenAI to do.");
                return;
            }
        }
        else if (action == SmartActionKind.Ask)
        {
            actionToRun = ResolveAskIntent(context.UserPrompt, _aiQuickCommand, out var cleanedPrompt);
            context = context with { UserPrompt = cleanedPrompt };
        }
        else
        {
            context = context with { UserPrompt = RemoveLeadingSlashCommand(context.UserPrompt) };
        }

        _aiRequestCts?.Cancel();
        _aiRequestCts?.Dispose();
        var cts = new CancellationTokenSource();
        _aiRequestCts = cts;
        _isAiBusy = true;
        UpdateAiAssistantUi();

        try
        {
            var result = useProvider
                ? await BuildProviderAiResultAsync(actionToRun, context, cts.Token).ConfigureAwait(false)
                : await Task.Run(() => BuildLocalAiResult(actionToRun, context), cts.Token).ConfigureAwait(false);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (AiOutputTextBox is not null)
                {
                    AiOutputTextBox.Text = result.Output;
                }
                SetAiStatus(result.Status);

                if (!result.HasEdit)
                {
                    AddAiHistoryEntry(actionToRun, context, result);
                    return;
                }

                var preview = new AiPatchPreviewDialog(
                    result.Title,
                    result.Status,
                    result.OriginalPreview,
                    result.ProposedPreview);
                var apply = await preview.ShowDialog<bool>(this);
                if (!apply)
                {
                    var rejectedStatus = useProvider ? "Provider change rejected." : "Local change rejected.";
                    SetAiStatus(rejectedStatus);
                    AddAiHistoryEntry(actionToRun, context, result with
                    {
                        Status = rejectedStatus
                    });
                    return;
                }

                ApplyAiEdit(result.EditStart, result.EditLength, result.ReplacementText, selectReplacement: actionToRun != SmartActionKind.GenerateTests);
                var appliedStatus = useProvider ? "Provider change applied." : "Local change applied.";
                SetAiStatus(appliedStatus);
                AddAiHistoryEntry(actionToRun, context, result with
                {
                    Status = appliedStatus
                });
            });
        }
        catch (OperationCanceledException)
        {
            SetAiStatus("Smart action canceled.");
        }
        catch (Exception ex)
        {
            SetAiStatus($"Smart-action error: {FirstLine(ex.Message)}");
        }
        finally
        {
            if (ReferenceEquals(_aiRequestCts, cts))
            {
                _aiRequestCts = null;
            }

            cts.Dispose();
            _isAiBusy = false;
            UpdateAiAssistantUi();
        }
    }

    private bool TryBuildAiActionContext(SmartActionKind action, out AiActionContext context, out string error)
    {
        context = new AiActionContext(
            ScopeMode: _aiScopeMode,
            LanguageMode: _viewModel.StatusLanguage,
            DocumentText: string.Empty,
            SourceText: string.Empty,
            EditStart: 0,
            EditLength: 0,
            UserPrompt: string.Empty,
            WorkspaceSummary: string.Empty);
        error = string.Empty;

        if (EditorTextBox is null)
        {
            error = "Editor not ready.";
            return false;
        }

        var scopeMode = NormalizeAiScopeMode(_aiScopeMode);
        var prompt = AiPromptTextBox?.Text?.Trim() ?? string.Empty;
        var fullText = EditorTextBox.Text ?? string.Empty;
        var workspaceSummary = BuildWorkspaceSummaryForAi();
        var languageMode = _viewModel.StatusLanguage;

        if ((action is SmartActionKind.Refactor or SmartActionKind.FixDiagnostics or SmartActionKind.GenerateTests)
            && string.Equals(scopeMode, "Workspace summary", StringComparison.Ordinal))
        {
            error = "Workspace summary is read-only. Use Selection only or Current file for edits.";
            return false;
        }

        if (string.Equals(scopeMode, "Selection only", StringComparison.Ordinal))
        {
            var start = Math.Min(EditorTextBox.SelectionStart, GetSelectionEnd());
            var end = Math.Max(EditorTextBox.SelectionStart, GetSelectionEnd());
            if (end <= start || start < 0 || end > fullText.Length)
            {
                error = "Selection scope requires selected text.";
                return false;
            }

            context = new AiActionContext(
                ScopeMode: scopeMode,
                LanguageMode: languageMode,
                DocumentText: fullText,
                SourceText: fullText.Substring(start, end - start),
                EditStart: start,
                EditLength: end - start,
                UserPrompt: prompt,
                WorkspaceSummary: workspaceSummary);
            return true;
        }

        if (string.Equals(scopeMode, "Current file", StringComparison.Ordinal))
        {
            context = new AiActionContext(
                ScopeMode: scopeMode,
                LanguageMode: languageMode,
                DocumentText: fullText,
                SourceText: fullText,
                EditStart: 0,
                EditLength: fullText.Length,
                UserPrompt: prompt,
                WorkspaceSummary: workspaceSummary);
            return true;
        }

        context = new AiActionContext(
            ScopeMode: "Workspace summary",
            LanguageMode: languageMode,
            DocumentText: fullText,
            SourceText: workspaceSummary,
            EditStart: 0,
            EditLength: 0,
            UserPrompt: prompt,
            WorkspaceSummary: workspaceSummary);
        return true;
    }

    private AiActionResult BuildLocalAiResult(SmartActionKind action, AiActionContext context)
    {
        return action switch
        {
            SmartActionKind.Explain => BuildExplainResult(context),
            SmartActionKind.Refactor => BuildRefactorResult(context),
            SmartActionKind.FixDiagnostics => BuildFixDiagnosticsResult(context),
            SmartActionKind.GenerateTests => BuildGenerateTestsResult(context),
            SmartActionKind.CommitMessage => BuildCommitMessageResult(context),
            _ => BuildExplainResult(context),
        };
    }

    private async Task<AiActionResult> BuildProviderAiResultAsync(
        SmartActionKind action,
        AiActionContext context,
        CancellationToken cancellationToken)
    {
        var settings = GetAiProviderSettings();
        var response = await _openAiResponsesClient.CreateTextResponseAsync(
            settings,
            BuildAiProviderDeveloperPrompt(action),
            BuildAiProviderUserPrompt(action, context),
            cancellationToken).ConfigureAwait(false);

        return action switch
        {
            SmartActionKind.Refactor => BuildProviderScopedEditResult(
                "OpenAI Refactor Preview",
                "OpenAI refactor draft ready. Review before applying.",
                "OpenAI did not suggest refactor changes.",
                response,
                context),
            SmartActionKind.FixDiagnostics => BuildProviderScopedEditResult(
                "OpenAI Diagnostics Fix Preview",
                "OpenAI diagnostics fix draft ready. Review before applying.",
                "OpenAI did not suggest a diagnostics fix.",
                response,
                context),
            SmartActionKind.GenerateTests => BuildProviderGeneratedTestsResult(response, context),
            SmartActionKind.CommitMessage => BuildProviderReadOnlyResult(action, response),
            _ => BuildProviderReadOnlyResult(action, response),
        };
    }

    private static SmartActionKind ResolveAskIntent(string prompt, string quickCommand, out string cleanedPrompt)
        => SmartActionLogic.ResolveAskKind(prompt, quickCommand, out cleanedPrompt);

    private AiProviderSettings GetAiProviderSettings()
        => AiProviderConfigLogic.Normalize(new AiProviderSettings(
            _aiProviderEnabled,
            _aiProviderEndpoint,
            _aiProviderModel,
            _aiProviderApiKeyEnvironmentVariable));

    private AiProviderAvailabilityState GetAiProviderAvailabilityState()
        => AiProviderConfigLogic.GetAvailabilityState(GetAiProviderSettings());

    private bool ShouldUseAiProviderForAction(SmartActionKind action)
        => GetAiProviderSettings().Enabled && AiProviderConfigLogic.CanUseProviderForAction(action);

    private string BuildAiProviderDeveloperPrompt(SmartActionKind action)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are assisting inside NotepadSharp, a desktop code editor.");
        sb.AppendLine("Be concise, practical, and accurate.");
        sb.AppendLine("Use only the supplied context. If important context is missing, say what is missing.");
        sb.AppendLine("Do not claim to have edited files, run commands, or verified behavior.");
        sb.AppendLine("Follow the output contract exactly.");

        switch (action)
        {
            case SmartActionKind.Explain:
                sb.AppendLine("Focus on explaining intent, flow, edge cases, and risks in the provided code.");
                sb.AppendLine("Return plain Markdown text only.");
                break;
            case SmartActionKind.Refactor:
                sb.AppendLine("Return only the full replacement text for the provided primary context.");
                sb.AppendLine("Preserve existing behavior unless the user explicitly asked for a behavioral change.");
                sb.AppendLine("Do not wrap the replacement in Markdown fences and do not add commentary.");
                break;
            case SmartActionKind.FixDiagnostics:
                sb.AppendLine("Return only the corrected replacement text for the provided primary context.");
                sb.AppendLine("Use the listed diagnostics when they are relevant and preserve unrelated code.");
                sb.AppendLine("Do not wrap the replacement in Markdown fences and do not add commentary.");
                break;
            case SmartActionKind.GenerateTests:
                sb.AppendLine("Return only the new test code to append to the current file.");
                sb.AppendLine("Use the current language and obvious local test style. Prefer xUnit for C# when no test framework is visible.");
                sb.AppendLine("Do not wrap the tests in Markdown fences and do not add commentary.");
                break;
            case SmartActionKind.CommitMessage:
                sb.AppendLine("Return only a commit message with a concise subject line and optional body.");
                sb.AppendLine("Do not wrap the commit message in Markdown fences and do not add commentary.");
                break;
            default:
                sb.AppendLine("Answer the user's request directly and include code snippets only when they materially help.");
                sb.AppendLine("Return plain Markdown text only.");
                break;
        }

        return sb.ToString().TrimEnd();
    }

    private string BuildAiProviderUserPrompt(SmartActionKind action, AiActionContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Action: {action}");
        sb.AppendLine($"Scope: {context.ScopeMode}");
        sb.AppendLine($"Language: {context.LanguageMode}");

        if (!string.IsNullOrWhiteSpace(context.UserPrompt))
        {
            sb.AppendLine($"User request: {context.UserPrompt.Trim()}");
        }
        else if (action == SmartActionKind.Explain)
        {
            sb.AppendLine("User request: Explain the provided code and call out important behavior or risks.");
        }

        if (_diagnosticEntries.Count > 0)
        {
            sb.AppendLine("Relevant diagnostics:");
            foreach (var diagnostic in _diagnosticEntries.Take(8))
            {
                sb.AppendLine($"- {diagnostic.Severity} L{diagnostic.Line},C{diagnostic.Column}: {diagnostic.Message}");
            }
        }

        switch (action)
        {
            case SmartActionKind.Refactor:
            case SmartActionKind.FixDiagnostics:
                sb.AppendLine("Output contract: Return a full replacement for the Primary context only.");
                break;
            case SmartActionKind.GenerateTests:
                sb.AppendLine("Output contract: Return only the new tests to append.");
                break;
            case SmartActionKind.CommitMessage:
                sb.AppendLine("Output contract: Return only the commit message text.");
                break;
        }

        if (!string.Equals(context.ScopeMode, "Workspace summary", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(context.WorkspaceSummary))
        {
            sb.AppendLine();
            sb.AppendLine("Workspace summary:");
            sb.AppendLine(TrimAiProviderContext(context.WorkspaceSummary, maxChars: 2400));
        }

        sb.AppendLine();
        sb.AppendLine("Primary context:");
        sb.AppendLine(TrimAiProviderContext(context.SourceText, maxChars: 12000));
        return sb.ToString().TrimEnd();
    }

    private static AiActionResult BuildProviderReadOnlyResult(SmartActionKind action, OpenAiResponseText response)
    {
        var output = AiProviderResponseLogic.ExtractPrimaryText(response.OutputText);
        var title = action == SmartActionKind.Explain
            ? "OpenAI Explain"
            : $"OpenAI {SmartActionLogic.GetHistoryLabel(action)}";
        return new AiActionResult(
            Title: title,
            Status: $"OpenAI response generated with {response.Model}.",
            Output: string.IsNullOrWhiteSpace(output)
                ? "OpenAI returned an empty response."
                : output,
            HasEdit: false,
            EditStart: 0,
            EditLength: 0,
            ReplacementText: string.Empty,
            OriginalPreview: string.Empty,
            ProposedPreview: string.Empty);
    }

    private static AiActionResult BuildProviderScopedEditResult(
        string title,
        string readyStatus,
        string noChangeStatus,
        OpenAiResponseText response,
        AiActionContext context)
    {
        var replacement = AiProviderResponseLogic.ExtractPrimaryText(response.OutputText);
        if (string.IsNullOrWhiteSpace(replacement))
        {
            return new AiActionResult(
                Title: title,
                Status: "OpenAI returned an empty response.",
                Output: "OpenAI returned an empty response.",
                HasEdit: false,
                EditStart: 0,
                EditLength: 0,
                ReplacementText: string.Empty,
                OriginalPreview: string.Empty,
                ProposedPreview: string.Empty);
        }

        replacement = PreserveTrailingNewline(context.SourceText, replacement);
        if (string.Equals(replacement, context.SourceText, StringComparison.Ordinal))
        {
            return new AiActionResult(
                Title: title,
                Status: noChangeStatus,
                Output: noChangeStatus,
                HasEdit: false,
                EditStart: 0,
                EditLength: 0,
                ReplacementText: string.Empty,
                OriginalPreview: string.Empty,
                ProposedPreview: string.Empty);
        }

        return new AiActionResult(
            Title: title,
            Status: readyStatus,
            Output: "OpenAI edit draft prepared. Use the preview dialog to apply or reject.",
            HasEdit: true,
            EditStart: context.EditStart,
            EditLength: context.EditLength,
            ReplacementText: replacement,
            OriginalPreview: context.SourceText,
            ProposedPreview: replacement);
    }

    private static AiActionResult BuildProviderGeneratedTestsResult(OpenAiResponseText response, AiActionContext context)
    {
        var snippet = AiProviderResponseLogic.ExtractPrimaryText(response.OutputText);
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return new AiActionResult(
                Title: "OpenAI Tests",
                Status: "OpenAI returned an empty response.",
                Output: "OpenAI returned an empty response.",
                HasEdit: false,
                EditStart: 0,
                EditLength: 0,
                ReplacementText: string.Empty,
                OriginalPreview: string.Empty,
                ProposedPreview: string.Empty);
        }

        var insertion = BuildAppendText(context.DocumentText, snippet);
        return new AiActionResult(
            Title: "OpenAI Generated Tests Preview",
            Status: "OpenAI test draft ready. Review before applying.",
            Output: "OpenAI tests prepared. Use the preview dialog to apply or reject.",
            HasEdit: true,
            EditStart: context.DocumentText.Length,
            EditLength: 0,
            ReplacementText: insertion,
            OriginalPreview: "(append at end of file)",
            ProposedPreview: insertion);
    }

    private AiActionResult BuildExplainResult(AiActionContext context)
    {
        var lines = CountAiLines(context.SourceText);
        var chars = context.SourceText.Length;
        var methods = ExtractMethodNames(context.SourceText, maxCount: 8);
        var diagnostics = _diagnosticEntries.Take(5).Select(d => $"{d.Severity} L{d.Line}: {d.Message}").ToList();

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context.UserPrompt))
        {
            sb.AppendLine($"Prompt: {context.UserPrompt}");
            sb.AppendLine();
        }

        sb.AppendLine($"Scope: {context.ScopeMode}");
        sb.AppendLine($"Language: {context.LanguageMode}");
        sb.AppendLine($"Size: {lines:N0} lines, {chars:N0} chars");
        if (methods.Count > 0)
        {
            sb.AppendLine($"Detected methods: {string.Join(", ", methods)}");
        }
        else
        {
            sb.AppendLine("Detected methods: none");
        }

        if (diagnostics.Count > 0)
        {
            sb.AppendLine("Top diagnostics:");
            foreach (var item in diagnostics)
            {
                sb.AppendLine($"- {item}");
            }
        }
        else
        {
            sb.AppendLine("Diagnostics: none");
        }

        if (string.Equals(context.ScopeMode, "Workspace summary", StringComparison.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine("Workspace summary:");
            sb.AppendLine(context.WorkspaceSummary);
        }

        return new AiActionResult(
            Title: "Smart Action: Explain",
            Status: "Local explanation generated.",
            Output: sb.ToString().TrimEnd(),
            HasEdit: false,
            EditStart: 0,
            EditLength: 0,
            ReplacementText: string.Empty,
            OriginalPreview: string.Empty,
            ProposedPreview: string.Empty);
    }

    private AiActionResult BuildRefactorResult(AiActionContext context)
    {
        var formatted = TryFormatByLanguage(context.SourceText, context.LanguageMode);
        var candidate = string.IsNullOrWhiteSpace(formatted)
            ? ApplyRefactorHeuristics(context.SourceText)
            : formatted;

        if (string.Equals(candidate, context.SourceText, StringComparison.Ordinal))
        {
            return new AiActionResult(
                Title: "Smart Action: Refactor",
                Status: "No deterministic refactor changes were found.",
                Output: "No changes to apply.",
                HasEdit: false,
                EditStart: 0,
                EditLength: 0,
                ReplacementText: string.Empty,
                OriginalPreview: string.Empty,
                ProposedPreview: string.Empty);
        }

        return new AiActionResult(
            Title: "Smart Action: Refactor Preview",
            Status: "Refactor plan ready. Review and apply if it looks correct.",
            Output: "Refactor generated. Use the preview dialog to apply or reject.",
            HasEdit: true,
            EditStart: context.EditStart,
            EditLength: context.EditLength,
            ReplacementText: candidate,
            OriginalPreview: context.SourceText,
            ProposedPreview: candidate);
    }

    private AiActionResult BuildFixDiagnosticsResult(AiActionContext context)
    {
        var candidate = ApplyDiagnosticFixHeuristics(context.SourceText);
        if (string.Equals(context.LanguageMode, "C#", StringComparison.Ordinal))
        {
            var formatted = TryFormatByLanguage(candidate, "C#");
            if (!string.IsNullOrWhiteSpace(formatted))
            {
                candidate = formatted;
            }
        }

        if (string.Equals(candidate, context.SourceText, StringComparison.Ordinal))
        {
            var diagSummary = _diagnosticEntries.Count == 0
                ? "No diagnostics found."
                : $"Diagnostics detected ({_diagnosticEntries.Count}), but no safe auto-fix matched.";
            return new AiActionResult(
                Title: "Smart Action: Fix Diagnostics",
                Status: diagSummary,
                Output: diagSummary,
                HasEdit: false,
                EditStart: 0,
                EditLength: 0,
                ReplacementText: string.Empty,
                OriginalPreview: string.Empty,
                ProposedPreview: string.Empty);
        }

        return new AiActionResult(
            Title: "Smart Action: Diagnostics Fix Preview",
            Status: "Diagnostics fix draft ready. Review before applying.",
            Output: "Suggested safe fixes prepared from current diagnostics.",
            HasEdit: true,
            EditStart: context.EditStart,
            EditLength: context.EditLength,
            ReplacementText: candidate,
            OriginalPreview: context.SourceText,
            ProposedPreview: candidate);
    }

    private AiActionResult BuildGenerateTestsResult(AiActionContext context)
    {
        var methods = ExtractMethodNames(context.SourceText, maxCount: 10);
        var testSnippet = BuildGeneratedTestsSnippet(methods, context.LanguageMode, _viewModel.SelectedDocument?.DisplayName);
        if (string.IsNullOrWhiteSpace(testSnippet))
        {
            return new AiActionResult(
                Title: "Smart Action: Generate Tests",
                Status: "No testable method signatures were found in the current scope.",
                Output: "Try Selection only on a class/function block.",
                HasEdit: false,
                EditStart: 0,
                EditLength: 0,
                ReplacementText: string.Empty,
                OriginalPreview: string.Empty,
                ProposedPreview: string.Empty);
        }

        var insertion = BuildAppendText(context.DocumentText, testSnippet);
        return new AiActionResult(
            Title: "Smart Action: Generated Tests Preview",
            Status: "Generated tests will be appended to the current file.",
            Output: "Review generated tests before applying.",
            HasEdit: true,
            EditStart: context.DocumentText.Length,
            EditLength: 0,
            ReplacementText: insertion,
            OriginalPreview: "(append at end of file)",
            ProposedPreview: insertion);
    }

    private AiActionResult BuildCommitMessageResult(AiActionContext context)
    {
        var changedCount = _gitChanges.Count;
        var groupedByStatus = _gitChanges
            .GroupBy(change => NormalizeGitCode(change.Code))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        var groupedByFolder = _gitChanges
            .Select(change => change.RelativePath.Replace('\\', '/'))
            .GroupBy(path => GetRootSegment(path))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        var inferredScope = InferCommitScope(groupedByFolder.Select(g => g.Key).ToList());
        var inferredType = InferConventionalCommitType(groupedByStatus.Select(g => g.Key).ToList(), context.UserPrompt);
        var subject = changedCount == 0
            ? "chore: no pending changes"
            : $"{inferredType}({inferredScope}): update {Math.Max(1, changedCount)} file{(changedCount == 1 ? string.Empty : "s")}";

        var body = new StringBuilder();
        body.AppendLine(subject);
        body.AppendLine();
        body.AppendLine("Summary:");
        if (changedCount == 0)
        {
            body.AppendLine("- Working tree clean.");
        }
        else
        {
            foreach (var group in groupedByStatus)
            {
                body.AppendLine($"- {group.Count(),2} {DescribeGitCode(group.Key)}");
            }

            if (groupedByFolder.Count > 0)
            {
                body.AppendLine("- Top paths:");
                foreach (var folder in groupedByFolder)
                {
                    body.AppendLine($"  - {folder.Key} ({folder.Count()})");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(context.UserPrompt))
        {
            body.AppendLine();
            body.AppendLine("Prompt note:");
            body.AppendLine($"- {context.UserPrompt.Trim()}");
        }

        body.AppendLine();
        body.AppendLine("Conventional alternatives:");
        body.AppendLine($"- {subject}");
        body.AppendLine($"- chore({inferredScope}): tidy project changes");
        body.AppendLine($"- refactor({inferredScope}): reorganize editor workflows");

        return new AiActionResult(
            Title: "Smart Action: Commit Message",
            Status: changedCount == 0
                ? "No git changes found. Generated a placeholder commit message."
                : "Commit message draft generated from current git changes.",
            Output: body.ToString().TrimEnd(),
            HasEdit: false,
            EditStart: 0,
            EditLength: 0,
            ReplacementText: string.Empty,
            OriginalPreview: string.Empty,
            ProposedPreview: string.Empty);
    }

    private static string ApplyRefactorHeuristics(string text)
    {
        var updated = text;
        updated = Regex.Replace(updated, @"[ \t]+(\r?\n)", "$1");
        updated = Regex.Replace(updated, @"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(\s*\);", "var $1 = new $2();");
        return PreserveTrailingNewline(text, updated);
    }

    private static string ApplyDiagnosticFixHeuristics(string text)
    {
        var updated = text;
        var typoFixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["stativ"] = "static",
            ["pubic"] = "public",
            ["pritave"] = "private",
            ["retun"] = "return",
            ["flase"] = "false",
            ["ture"] = "true",
        };

        foreach (var (wrong, right) in typoFixes)
        {
            updated = Regex.Replace(updated, $@"\b{Regex.Escape(wrong)}\b", right, RegexOptions.IgnoreCase);
        }

        updated = Regex.Replace(updated, @"[ \t]+(\r?\n)", "$1");
        updated = Regex.Replace(updated, @"\n{3,}", "\n\n");
        return PreserveTrailingNewline(text, updated);
    }

    private static string PreserveTrailingNewline(string original, string updated)
    {
        var hadTrailingNewline = original.EndsWith("\n", StringComparison.Ordinal) || original.EndsWith("\r", StringComparison.Ordinal);
        if (hadTrailingNewline && !updated.EndsWith("\n", StringComparison.Ordinal))
        {
            return updated + "\n";
        }

        if (!hadTrailingNewline && updated.EndsWith("\n", StringComparison.Ordinal))
        {
            return updated.TrimEnd('\r', '\n');
        }

        return updated;
    }

    private static string BuildAppendText(string currentText, string addition)
    {
        if (string.IsNullOrEmpty(currentText))
        {
            return addition;
        }

        var separator = currentText.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "\n\n";
        return separator + addition;
    }

    private static int CountAiLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static List<string> ExtractMethodNames(string text, int maxCount)
    {
        var matches = Regex.Matches(
            text,
            @"\b(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?[A-Za-z_][A-Za-z0-9_<>,\[\]\?]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(");

        var names = new List<string>();
        foreach (Match match in matches)
        {
            if (!match.Success || match.Groups.Count < 2)
            {
                continue;
            }

            var name = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (names.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            names.Add(name);
            if (names.Count >= maxCount)
            {
                break;
            }
        }

        return names;
    }

    private static string BuildGeneratedTestsSnippet(IReadOnlyList<string> methodNames, string language, string? documentDisplayName)
    {
        if (!string.Equals(language, "C#", StringComparison.Ordinal))
        {
            return $"// Local smart-action test generation currently supports C# snippets best.\n// Current language: {language}";
        }

        var className = ToSafeIdentifier((documentDisplayName ?? "Generated").Replace(".cs", string.Empty, StringComparison.Ordinal), "Generated");
        var methods = methodNames.Count == 0 ? new[] { "MainFlow" } : methodNames.Take(8).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("// Smart-action generated test scaffold");
        sb.AppendLine("using Xunit;");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {className}Tests");
        sb.AppendLine("{");
        foreach (var method in methods)
        {
            var safe = ToSafeIdentifier(method, "Method");
            sb.AppendLine("    [Fact]");
            sb.AppendLine($"    public void {safe}_BasicBehavior()");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: Arrange");
            sb.AppendLine("        // TODO: Act");
            sb.AppendLine("        // TODO: Assert");
            sb.AppendLine("        Assert.True(true);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string ToSafeIdentifier(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        var candidate = sb.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallback;
        }

        if (!char.IsLetter(candidate[0]) && candidate[0] != '_')
        {
            candidate = "_" + candidate;
        }

        return candidate;
    }

    private static string TrimAiProviderContext(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(no context)";
        }

        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..Math.Max(0, maxChars)] + "\n\n[context truncated]";
    }

    private void ApplyAiEdit(int start, int length, string replacement, bool selectReplacement)
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var safeStart = Math.Clamp(start, 0, text.Length);
        var safeLength = Math.Clamp(length, 0, text.Length - safeStart);

        var updated = text.Substring(0, safeStart) + replacement + text.Substring(safeStart + safeLength);
        EditorTextBox.Text = updated;
        var caret = Math.Min(safeStart + replacement.Length, updated.Length);
        EditorTextBox.CaretOffset = caret;
        if (selectReplacement)
        {
            SetSelection(safeStart, safeStart + replacement.Length);
        }
        else
        {
            SetSelection(caret, caret);
        }

        EditorTextBox.Focus();
    }

    private string BuildWorkspaceSummaryForAi()
    {
        EnsureWorkspaceRoot();
        var sb = new StringBuilder();
        sb.AppendLine($"Workspace root: {_workspaceRoot ?? "(none)"}");
        sb.AppendLine($"Open tabs: {_viewModel.Documents.Count}");
        sb.AppendLine($"Selected language: {_viewModel.StatusLanguage}");
        sb.AppendLine($"Sidebar section: {_sidebarSection}");
        sb.AppendLine($"Diagnostics: {_diagnosticEntries.Count}");
        sb.AppendLine($"Git changes tracked: {_gitChanges.Count}");
        if (_diagnosticEntries.Count > 0)
        {
            sb.AppendLine("Top diagnostics:");
            foreach (var diag in _diagnosticEntries.Take(5))
            {
                sb.AppendLine($"- {diag.Severity} L{diag.Line},C{diag.Column}: {diag.Message}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private void AddAiHistoryEntry(SmartActionKind action, AiActionContext context, AiActionResult result)
    {
        var label = SmartActionLogic.GetHistoryLabel(action);

        var prompt = context.UserPrompt;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = $"[{context.ScopeMode}]";
        }

        var entry = new AiHistoryEntry(
            Timestamp: DateTimeOffset.Now,
            Label: label,
            Prompt: prompt,
            Output: result.Output,
            Status: result.Status);

        _aiHistory.Insert(0, entry);
        if (_aiHistory.Count > MaxAiHistoryEntries)
        {
            _aiHistory.RemoveRange(MaxAiHistoryEntries, _aiHistory.Count - MaxAiHistoryEntries);
        }

        UpdateAiHistorySelector();
    }

    private void UpdateAiAssistantUi()
    {
        _isUpdatingAiAssistantSelectors = true;
        UpdateAiProviderControls();
        UpdateAiScopeSelector();
        UpdateAiQuickCommandSelector();
        UpdateAiHistorySelector();
        _isUpdatingAiAssistantSelectors = false;

        var providerSettings = GetAiProviderSettings();
        var providerState = AiProviderConfigLogic.GetAvailabilityState(providerSettings);

        if (AiAssistantTitleTextBlock is not null)
        {
            AiAssistantTitleTextBlock.Text = providerSettings.Enabled
                ? "Smart Actions + OpenAI"
                : "Smart Actions (Local)";
        }

        if (AiAssistantModeTextBlock is not null)
        {
            AiAssistantModeTextBlock.Text = providerState.Description;
        }

        if (AiAssistantStatusTextBlock is not null
            && (string.IsNullOrWhiteSpace(AiAssistantStatusTextBlock.Text)
                || AiAssistantStatusTextBlock.Text.StartsWith("Local smart actions only", StringComparison.Ordinal)
                || AiAssistantStatusTextBlock.Text.StartsWith("Deterministic local smart actions only", StringComparison.Ordinal)
                || AiAssistantStatusTextBlock.Text.StartsWith("OpenAI is enabled", StringComparison.Ordinal)))
        {
            AiAssistantStatusTextBlock.Text = providerState.Description;
        }

        if (AiPromptTextBox is not null)
        {
            AiPromptTextBox.IsEnabled = !_isAiBusy;
            AiPromptTextBox.Watermark = providerSettings.Enabled
                ? "Ask OpenAI about the current scope..."
                : "Describe the local smart action to run...";
        }

        if (AiQuickCommandComboBox is not null)
        {
            AiQuickCommandComboBox.IsEnabled = !_isAiBusy && !providerSettings.Enabled;
        }

        if (AiScopeComboBox is not null)
        {
            AiScopeComboBox.IsEnabled = !_isAiBusy;
        }
    }

    private void UpdateAiProviderControls()
    {
        var settings = GetAiProviderSettings();

        if (AiProviderEnabledCheckBox is not null)
        {
            AiProviderEnabledCheckBox.IsChecked = settings.Enabled;
            AiProviderEnabledCheckBox.IsEnabled = !_isAiBusy;
        }

        if (AiProviderModelTextBox is not null)
        {
            AiProviderModelTextBox.Text = settings.Model;
            AiProviderModelTextBox.IsEnabled = !_isAiBusy;
        }

        if (AiProviderEndpointTextBox is not null)
        {
            AiProviderEndpointTextBox.Text = settings.Endpoint;
            AiProviderEndpointTextBox.IsEnabled = !_isAiBusy;
        }

        if (AiProviderApiKeyEnvVarTextBox is not null)
        {
            AiProviderApiKeyEnvVarTextBox.Text = settings.ApiKeyEnvironmentVariable;
            AiProviderApiKeyEnvVarTextBox.IsEnabled = !_isAiBusy;
        }
    }

    private void UpdateAiScopeSelector()
    {
        if (AiScopeComboBox is null)
        {
            return;
        }

        _aiScopeMode = NormalizeAiScopeMode(_aiScopeMode);
        SetComboSelection(AiScopeComboBox, _aiScopeMode);
    }

    private void UpdateAiQuickCommandSelector()
    {
        if (AiQuickCommandComboBox is null)
        {
            return;
        }

        _aiQuickCommand = NormalizeAiQuickCommand(_aiQuickCommand);
        SetComboSelection(AiQuickCommandComboBox, _aiQuickCommand);
    }

    private void UpdateAiHistorySelector()
    {
        if (AiHistoryComboBox is null)
        {
            return;
        }

        var selected = AiHistoryComboBox.SelectedItem as AiHistoryEntry;
        AiHistoryComboBox.ItemsSource = _aiHistory.ToList();
        if (selected is not null && _aiHistory.Contains(selected))
        {
            AiHistoryComboBox.SelectedItem = selected;
        }
        else if (_aiHistory.Count == 0)
        {
            AiHistoryComboBox.SelectedItem = null;
        }

        AiHistoryComboBox.IsEnabled = !_isAiBusy && _aiHistory.Count > 0;
    }

    private static string NormalizeAiScopeMode(string? value)
        => SmartActionLogic.NormalizeScopeMode(value);

    private static string NormalizeAiQuickCommand(string? value)
        => SmartActionLogic.NormalizeQuickCommand(value);

    private static string RemoveLeadingSlashCommand(string prompt)
        => SmartActionLogic.RemoveLeadingSlashCommand(prompt);

    private static string NormalizeGitCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "--";
        }

        var raw = code.Trim();
        if (raw.Contains('?', StringComparison.Ordinal))
        {
            return "?";
        }

        if (raw.Contains('A', StringComparison.Ordinal))
        {
            return "A";
        }

        if (raw.Contains('D', StringComparison.Ordinal))
        {
            return "D";
        }

        if (raw.Contains('R', StringComparison.Ordinal))
        {
            return "R";
        }

        if (raw.Contains('M', StringComparison.Ordinal))
        {
            return "M";
        }

        return raw;
    }

    private static string DescribeGitCode(string code)
    {
        return code switch
        {
            "A" => "added",
            "M" => "modified",
            "D" => "deleted",
            "R" => "renamed",
            "?" => "untracked",
            "--" => "other",
            _ => code,
        };
    }

    private static string GetRootSegment(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(root)";
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "(root)";
        }

        var slash = normalized.IndexOf('/');
        return slash < 0 ? normalized : normalized[..slash];
    }

    private static string InferCommitScope(IReadOnlyList<string> folders)
    {
        if (folders.Count == 0)
        {
            return "core";
        }

        var top = folders[0].ToLowerInvariant();
        if (top is "src" or "app")
        {
            return "app";
        }

        if (top is "test" or "tests")
        {
            return "tests";
        }

        if (top is "docs" or "readme")
        {
            return "docs";
        }

        return ToSafeIdentifier(top, "core").ToLowerInvariant();
    }

    private static string InferConventionalCommitType(IReadOnlyList<string> statusCodes, string prompt)
    {
        if (prompt.Contains("fix", StringComparison.OrdinalIgnoreCase)
            || statusCodes.Contains("D", StringComparer.Ordinal))
        {
            return "fix";
        }

        if (prompt.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return "test";
        }

        if (prompt.Contains("doc", StringComparison.OrdinalIgnoreCase))
        {
            return "docs";
        }

        if (prompt.Contains("refactor", StringComparison.OrdinalIgnoreCase)
            || statusCodes.Contains("R", StringComparer.Ordinal))
        {
            return "refactor";
        }

        if (statusCodes.Contains("A", StringComparer.Ordinal))
        {
            return "feat";
        }

        return "chore";
    }

    private static string GetComboSelection(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            return selectedItem.Content?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static void SetComboSelection(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private void FocusAiPrompt()
    {
        AiPromptTextBox?.Focus();
        AiPromptTextBox?.SelectAll();
    }

    private void SetAiStatus(string text)
    {
        if (AiAssistantStatusTextBlock is not null)
        {
            AiAssistantStatusTextBlock.Text = text;
        }
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var line = text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return line?.Trim() ?? string.Empty;
    }
}

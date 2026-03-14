using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NotepadSharp.App.Dialogs;
using NotepadSharp.App.Services;
using NotepadSharp.Core;

namespace NotepadSharp.App;

public partial class MainWindow
{
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

    private async void OnGoToSymbolClick(object? sender, RoutedEventArgs e)
        => await ShowDocumentSymbolsAsync();

    private async void OnGoToDefinitionClick(object? sender, RoutedEventArgs e)
        => await ShowGoToDefinitionAsync();

    private async void OnFindReferencesClick(object? sender, RoutedEventArgs e)
        => await ShowFindReferencesAsync();

    private async void OnGoToWorkspaceSymbolClick(object? sender, RoutedEventArgs e)
        => await ShowWorkspaceSymbolsAsync();

    private async void OnRenameSymbolClick(object? sender, RoutedEventArgs e)
        => await ShowRenameSymbolAsync();

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
        _commandPaletteActions["edit.ai"] = () => OnShowAiAssistantClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.ai.explain"] = () => OnAiExplainClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.ai.refactor"] = () => OnAiRefactorClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.ai.fix"] = () => OnAiFixDiagnosticsClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.ai.tests"] = () => OnAiGenerateTestsClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.ai.commit"] = () => OnAiCommitMessageClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.ai.copy"] = () => OnAiCopyOutputClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.nextDiagnostic"] = () => OnNextDiagnosticClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.previousDiagnostic"] = () => OnPreviousDiagnosticClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.nextGitChange"] = () => OnNextGitChangeClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.previousGitChange"] = () => OnPreviousGitChangeClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.searchInFiles"] = () => OnShowSearchInFilesClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.replaceInFiles"] = () => OnReplaceInFilesMenuClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.goto"] = () => OnGoToLineClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.gotoSymbol"] = () => OnGoToSymbolClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.gotoWorkspaceSymbol"] = () => OnGoToWorkspaceSymbolClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.gotoDefinition"] = () => OnGoToDefinitionClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.findReferences"] = () => OnFindReferencesClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.renameSymbol"] = () => OnRenameSymbolClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.formatDocument"] = () => OnFormatDocumentClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.formatSelection"] = () => OnFormatSelectionClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.split"] = () => OnToggleSplitViewClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.minimap"] = () => OnToggleMiniMapClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.terminal"] = () => OnToggleTerminalClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.terminalInterrupt"] = () => OnTerminalInterruptClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.maximizeEditor"] = () => OnToggleEditorMaximizeClick(this, new RoutedEventArgs());
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
        _commandPaletteActions["scm.switchBranch"] = () => OnGitSwitchBranchClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.createBranch"] = () => OnGitCreateBranchClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.renameBranch"] = () => OnGitRenameBranchClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.deleteBranch"] = () => OnGitDeleteBranchClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.stashPush"] = () => OnGitStashPushClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.stashApply"] = () => OnGitStashApplyClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.stashPop"] = () => OnGitStashPopClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.stashDrop"] = () => OnGitStashDropClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.continueOperation"] = () => OnGitContinueOperationClick(this, new RoutedEventArgs());
        _commandPaletteActions["scm.abortOperation"] = () => OnGitAbortOperationClick(this, new RoutedEventArgs());
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
            new PaletteItem("edit.ai", "Smart Actions", "Edit"),
            new PaletteItem("edit.ai.explain", "Smart Actions: Explain", "Edit"),
            new PaletteItem("edit.ai.refactor", "Smart Actions: Refactor", "Edit"),
            new PaletteItem("edit.ai.fix", "Smart Actions: Fix Diagnostics", "Edit"),
            new PaletteItem("edit.ai.tests", "Smart Actions: Generate Tests", "Edit"),
            new PaletteItem("edit.ai.commit", "Smart Actions: Commit Message", "Edit"),
            new PaletteItem("edit.ai.copy", "Smart Actions: Copy Output", "Edit"),
            new PaletteItem("edit.nextDiagnostic", "Next Diagnostic", "Edit"),
            new PaletteItem("edit.previousDiagnostic", "Previous Diagnostic", "Edit"),
            new PaletteItem("edit.nextGitChange", "Next Git Change", "Edit"),
            new PaletteItem("edit.previousGitChange", "Previous Git Change", "Edit"),
            new PaletteItem("edit.searchInFiles", "Search in Files", "Edit"),
            new PaletteItem("edit.replaceInFiles", "Replace in Files", "Edit"),
            new PaletteItem("edit.goto", "Go To Line", "Edit"),
            new PaletteItem("edit.gotoSymbol", "Go To Symbol...", "Edit"),
            new PaletteItem("edit.gotoWorkspaceSymbol", "Go To Workspace Symbol...", "Edit"),
            new PaletteItem("edit.gotoDefinition", "Go To Definition", "Edit"),
            new PaletteItem("edit.findReferences", "Find References", "Edit"),
            new PaletteItem("edit.renameSymbol", "Rename Symbol...", "Edit"),
            new PaletteItem("edit.formatDocument", "Format Document", "Format"),
            new PaletteItem("edit.formatSelection", "Format Selection", "Format"),
            new PaletteItem("view.split", "Toggle Split Editor", "View"),
            new PaletteItem("view.minimap", "Toggle Mini Map", "View"),
            new PaletteItem("view.terminal", "Toggle Shell Session", "View"),
            new PaletteItem("view.terminalInterrupt", "Interrupt Shell Command", "View"),
            new PaletteItem("view.maximizeEditor", "Toggle Maximize Editor", "View"),
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
            new PaletteItem("scm.switchBranch", "Source Control: Switch Branch...", "Git"),
            new PaletteItem("scm.createBranch", "Source Control: Create Branch...", "Git"),
            new PaletteItem("scm.renameBranch", "Source Control: Rename Current Branch...", "Git"),
            new PaletteItem("scm.deleteBranch", "Source Control: Delete Branch...", "Git"),
            new PaletteItem("scm.stashPush", "Source Control: Stash Changes...", "Git"),
            new PaletteItem("scm.stashApply", "Source Control: Apply Stash...", "Git"),
            new PaletteItem("scm.stashPop", "Source Control: Pop Stash...", "Git"),
            new PaletteItem("scm.stashDrop", "Source Control: Drop Stash...", "Git"),
            new PaletteItem("scm.continueOperation", "Source Control: Continue Active Repository Operation", "Git"),
            new PaletteItem("scm.abortOperation", "Source Control: Abort Active Repository Operation", "Git"),
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

    private async Task ShowDocumentSymbolsAsync()
    {
        if (EditorTextBox is null)
        {
            return;
        }

        var text = EditorTextBox.Text ?? string.Empty;
        var symbols = DocumentSymbolLogic.GetSymbols(text, _viewModel.StatusLanguage);
        if (symbols.Count == 0)
        {
            return;
        }

        var items = symbols
            .Select(symbol => new PaletteItem(
                $"symbol:{symbol.Line}:{symbol.Column}",
                symbol.Title,
                $"{symbol.Kind} • L{symbol.Line},C{symbol.Column} • {symbol.Description}"))
            .ToList();

        var dialog = new SelectionPaletteDialog("Go To Symbol", "Search current file symbols...", items);
        var selected = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(selected) || !selected.StartsWith("symbol:", StringComparison.Ordinal))
        {
            return;
        }

        var parts = selected["symbol:".Length..].Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var line)
            || !int.TryParse(parts[1], out var column))
        {
            return;
        }

        GoToLine(EditorTextBox, line, column);
    }

    private async Task ShowWorkspaceSymbolsAsync()
    {
        if (EditorTextBox is null || _viewModel.SelectedDocument is null)
        {
            return;
        }

        var activeDocument = _viewModel.SelectedDocument;
        var activeText = EditorTextBox.Text ?? string.Empty;
        var sources = await BuildWorkspaceSymbolSourcesAsync(activeDocument, activeText);
        if (sources.Count == 0)
        {
            return;
        }

        var symbols = WorkspaceSymbolLogic.GetSymbols(sources);
        if (symbols.Count == 0)
        {
            return;
        }

        var items = symbols
            .Select((symbol, index) => new PaletteItem(
                $"workspace-symbol:{index}",
                symbol.Title,
                $"{symbol.RelativePath} • {symbol.Kind} • L{symbol.Line},C{symbol.Column} • {symbol.Description}"))
            .ToList();

        var dialog = new SelectionPaletteDialog("Go To Workspace Symbol", "Search workspace symbols...", items);
        var selected = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(selected) || !selected.StartsWith("workspace-symbol:", StringComparison.Ordinal))
        {
            return;
        }

        if (!int.TryParse(selected["workspace-symbol:".Length..], out var selectedIndex)
            || selectedIndex < 0
            || selectedIndex >= symbols.Count)
        {
            return;
        }

        var target = symbols[selectedIndex];
        if (!string.IsNullOrWhiteSpace(target.FilePath)
            && !string.IsNullOrWhiteSpace(activeDocument.FilePath)
            && !PathsEqual(activeDocument.FilePath, target.FilePath))
        {
            await OpenFilePathAsync(target.FilePath);
        }
        else if (!string.IsNullOrWhiteSpace(target.FilePath) && string.IsNullOrWhiteSpace(activeDocument.FilePath))
        {
            await OpenFilePathAsync(target.FilePath);
        }

        if (EditorTextBox is null)
        {
            return;
        }

        GoToLine(EditorTextBox, target.Line, target.Column);
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
            var semanticSuggestions = _viewModel.SelectedDocument is null
                ? await CSharpCompletionLogic.GetSuggestionsAsync(text, caret)
                : await CSharpCompletionLogic.GetSuggestionsAsync(
                    BuildOpenCSharpDefinitionSources(_viewModel.SelectedDocument, text),
                    caret);
            foreach (var suggestion in semanticSuggestions)
            {
                suggestions.Add(suggestion);
            }

            foreach (var kw in CSharpCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else
        {
            var keywordSource = string.Equals(language, "JavaScript", StringComparison.Ordinal)
                ? JavaScriptCompletionKeywords
                : string.Equals(language, "TypeScript", StringComparison.Ordinal)
                    ? TypeScriptCompletionKeywords
                    : string.Equals(language, "Python", StringComparison.Ordinal)
                        ? PythonCompletionKeywords
                        : string.Equals(language, "SQL", StringComparison.Ordinal)
                            ? SqlCompletionKeywords
                            : Array.Empty<string>();

            foreach (var suggestion in DocumentCompletionLogic.GetSuggestions(text, language, keywordSource, prefix))
            {
                suggestions.Add(suggestion);
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

    private async Task ShowGoToDefinitionAsync()
    {
        if (EditorTextBox is null
            || _viewModel.SelectedDocument is null)
        {
            return;
        }

        var activeDocument = _viewModel.SelectedDocument;
        var activeText = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, activeText.Length);
        var language = _viewModel.StatusLanguage;

        if (string.Equals(language, "C#", StringComparison.Ordinal))
        {
            var sources = await BuildCSharpDefinitionSourcesAsync(activeDocument, activeText);
            if (sources.Count == 0)
            {
                return;
            }

            var location = await Task.Run(() => CSharpDefinitionLogic.TryResolveDefinition(sources, caret));
            if (location is null)
            {
                return;
            }

            await OpenDefinitionLocationAsync(activeDocument, location.FilePath, location.Line, location.Column);
            return;
        }

        if (!HeuristicCodeNavigationLogic.SupportsLanguage(language))
        {
            return;
        }

        var heuristicSources = await BuildHeuristicNavigationSourcesAsync(activeDocument, activeText, language);
        if (heuristicSources.Count == 0)
        {
            return;
        }

        var heuristicLocation = await Task.Run(() => HeuristicCodeNavigationLogic.TryResolveDefinition(heuristicSources, caret));
        if (heuristicLocation is null)
        {
            return;
        }

        await OpenDefinitionLocationAsync(activeDocument, heuristicLocation.FilePath, heuristicLocation.Line, heuristicLocation.Column);
    }

    private async Task ShowFindReferencesAsync()
    {
        if (EditorTextBox is null
            || _viewModel.SelectedDocument is null)
        {
            return;
        }

        var activeDocument = _viewModel.SelectedDocument;
        var activeText = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, activeText.Length);
        var language = _viewModel.StatusLanguage;

        if (string.Equals(language, "C#", StringComparison.Ordinal))
        {
            var sources = await BuildCSharpDefinitionSourcesAsync(activeDocument, activeText);
            if (sources.Count == 0)
            {
                return;
            }

            var references = await CSharpReferenceLogic.FindReferencesAsync(sources, caret);
            if (references.Count == 0)
            {
                return;
            }

            await ShowReferenceResultsAsync(
                activeDocument,
                references.Select(reference => new NavigationReferenceResult(
                    reference.FilePath,
                    reference.Line,
                    reference.Column,
                    reference.Preview,
                    reference.SymbolDisplay))
                    .ToList());
            return;
        }

        if (!HeuristicCodeNavigationLogic.SupportsLanguage(language))
        {
            return;
        }

        var heuristicSources = await BuildHeuristicNavigationSourcesAsync(activeDocument, activeText, language);
        if (heuristicSources.Count == 0)
        {
            return;
        }

        var heuristicReferences = await Task.Run(() => HeuristicCodeNavigationLogic.FindReferences(heuristicSources, caret));
        if (heuristicReferences.Count == 0)
        {
            return;
        }

        await ShowReferenceResultsAsync(
            activeDocument,
            heuristicReferences.Select(reference => new NavigationReferenceResult(
                reference.FilePath,
                reference.Line,
                reference.Column,
                reference.Preview,
                reference.SymbolDisplay))
                .ToList());
    }

    private async Task ShowRenameSymbolAsync()
    {
        if (EditorTextBox is null
            || _viewModel.SelectedDocument is null)
        {
            return;
        }

        var activeDocument = _viewModel.SelectedDocument;
        var activeText = EditorTextBox.Text ?? string.Empty;
        var caret = Math.Clamp(EditorTextBox.CaretOffset, 0, activeText.Length);
        var currentIdentifier = GetIdentifierNearCaret(activeText, caret);
        var newName = await PromptTextAsync("Rename Symbol", "New symbol name:", currentIdentifier);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, currentIdentifier, StringComparison.Ordinal))
        {
            return;
        }

        var language = _viewModel.StatusLanguage;
        if (string.Equals(language, "C#", StringComparison.Ordinal))
        {
            var sources = await BuildCSharpDefinitionSourcesAsync(activeDocument, activeText);
            if (sources.Count == 0)
            {
                return;
            }

            var result = await CSharpRenameLogic.RenameSymbolAsync(sources, caret, newName);
            if (result is null || result.Changes.Count == 0)
            {
                return;
            }

            if (!await ConfirmAsync("Rename Symbol", $"Rename '{result.SymbolDisplay}' to '{newName}' across {result.Changes.Count} file(s)?"))
            {
                return;
            }

            await ApplyCSharpRenameResultAsync(result, activeDocument);
            return;
        }

        if (!HeuristicCodeNavigationLogic.SupportsLanguage(language))
        {
            return;
        }

        var heuristicSources = await BuildHeuristicNavigationSourcesAsync(activeDocument, activeText, language);
        if (heuristicSources.Count == 0)
        {
            return;
        }

        var heuristicResult = await Task.Run(() => HeuristicRenameLogic.RenameSymbol(heuristicSources, caret, newName));
        if (heuristicResult is null || heuristicResult.Changes.Count == 0)
        {
            return;
        }

        if (!await ConfirmAsync("Rename Symbol", $"Rename '{heuristicResult.SymbolDisplay}' using heuristic matches across {heuristicResult.Changes.Count} file(s)?"))
        {
            return;
        }

        await ApplyHeuristicRenameResultAsync(heuristicResult, activeDocument);
    }

    private Task<IReadOnlyList<CSharpDefinitionSource>> BuildCSharpDefinitionSourcesAsync(TextDocument activeDocument, string activeText)
        => Task.Run<IReadOnlyList<CSharpDefinitionSource>>(() => BuildCSharpDefinitionSources(activeDocument, activeText, includeWorkspaceFiles: true));

    private IReadOnlyList<CSharpDefinitionSource> BuildOpenCSharpDefinitionSources(TextDocument activeDocument, string activeText)
        => BuildCSharpDefinitionSources(activeDocument, activeText, includeWorkspaceFiles: false);

    private IReadOnlyList<CSharpDefinitionSource> BuildCSharpDefinitionSources(TextDocument activeDocument, string activeText, bool includeWorkspaceFiles)
    {
        var sources = new List<CSharpDefinitionSource>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDefinitionSource(sources, seenPaths, activeDocument.FilePath, activeText, isActiveDocument: true);

        foreach (var document in _viewModel.Documents.Where(doc => !ReferenceEquals(doc, activeDocument)))
        {
            if (!IsCSharpSourceDocument(document.FilePath, document.Text))
            {
                continue;
            }

            AddDefinitionSource(sources, seenPaths, document.FilePath, document.Text, isActiveDocument: false);
        }

        if (!includeWorkspaceFiles || string.IsNullOrWhiteSpace(_workspaceRoot) || !Directory.Exists(_workspaceRoot))
        {
            return sources;
        }

        foreach (var filePath in EnumerateWorkspaceCSharpFiles(_workspaceRoot))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(filePath);
            }
            catch
            {
                continue;
            }

            if (!seenPaths.Add(fullPath))
            {
                continue;
            }

            try
            {
                sources.Add(new CSharpDefinitionSource(fullPath, File.ReadAllText(fullPath), IsActiveDocument: false));
            }
            catch
            {
                // Ignore unreadable files.
            }
        }

        return sources;
    }

    private Task<IReadOnlyList<WorkspaceSymbolSource>> BuildWorkspaceSymbolSourcesAsync(TextDocument activeDocument, string activeText)
    {
        return Task.Run<IReadOnlyList<WorkspaceSymbolSource>>(() =>
        {
            var sources = new List<WorkspaceSymbolSource>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddWorkspaceSymbolSource(sources, seenPaths, activeDocument.FilePath, activeText, activeDocument.DisplayName.TrimEnd('*'));

            foreach (var document in _viewModel.Documents.Where(doc => !ReferenceEquals(doc, activeDocument)))
            {
                AddWorkspaceSymbolSource(sources, seenPaths, document.FilePath, document.Text, document.DisplayName.TrimEnd('*'));
            }

            if (!string.IsNullOrWhiteSpace(_workspaceRoot) && Directory.Exists(_workspaceRoot))
            {
                foreach (var filePath in EnumerateWorkspaceSymbolFiles(_workspaceRoot))
                {
                    string fullPath;
                    try
                    {
                        fullPath = Path.GetFullPath(filePath);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!seenPaths.Add(fullPath))
                    {
                        continue;
                    }

                    try
                    {
                        var text = File.ReadAllText(fullPath);
                        var language = DetectLanguage(fullPath, text);
                        if (!SupportsWorkspaceSymbols(language))
                        {
                            continue;
                        }

                        sources.Add(new WorkspaceSymbolSource(
                            fullPath,
                            Path.GetRelativePath(_workspaceRoot, fullPath),
                            text,
                            language));
                    }
                    catch
                    {
                        // Ignore unreadable files.
                    }
                }
            }

            return sources;
        });
    }

    private Task<IReadOnlyList<HeuristicNavigationSource>> BuildHeuristicNavigationSourcesAsync(
        TextDocument activeDocument,
        string activeText,
        string language)
    {
        return Task.Run<IReadOnlyList<HeuristicNavigationSource>>(() =>
        {
            var sources = new List<HeuristicNavigationSource>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddHeuristicNavigationSource(sources, seenPaths, activeDocument.FilePath, activeText, language, isActiveDocument: true);

            foreach (var document in _viewModel.Documents.Where(doc => !ReferenceEquals(doc, activeDocument)))
            {
                var documentLanguage = DetectLanguage(document.FilePath, document.Text);
                if (!string.Equals(documentLanguage, language, StringComparison.Ordinal))
                {
                    continue;
                }

                AddHeuristicNavigationSource(
                    sources,
                    seenPaths,
                    document.FilePath,
                    document.Text,
                    documentLanguage,
                    isActiveDocument: false);
            }

            if (!string.IsNullOrWhiteSpace(_workspaceRoot) && Directory.Exists(_workspaceRoot))
            {
                foreach (var filePath in EnumerateWorkspaceSymbolFiles(_workspaceRoot))
                {
                    string fullPath;
                    try
                    {
                        fullPath = Path.GetFullPath(filePath);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!seenPaths.Add(fullPath))
                    {
                        continue;
                    }

                    try
                    {
                        var text = File.ReadAllText(fullPath);
                        var fileLanguage = DetectLanguage(fullPath, text);
                        if (!string.Equals(fileLanguage, language, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        sources.Add(new HeuristicNavigationSource(fullPath, text, fileLanguage, IsActiveDocument: false));
                    }
                    catch
                    {
                        // Ignore unreadable files.
                    }
                }
            }

            return sources;
        });
    }

    private IEnumerable<string> EnumerateWorkspaceCSharpFiles(string workspaceRoot)
    {
        var pending = new Stack<string>();
        pending.Push(workspaceRoot);

        while (pending.Count > 0)
        {
            var currentDirectory = pending.Pop();
            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                if (!WorkspaceWatcherLogic.ShouldIgnorePath(workspaceRoot, childDirectory))
                {
                    pending.Push(childDirectory);
                }
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDirectory, "*.cs");
            }
            catch
            {
                continue;
            }

            foreach (var filePath in files)
            {
                if (!WorkspaceWatcherLogic.ShouldIgnorePath(workspaceRoot, filePath))
                {
                    yield return filePath;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateWorkspaceSymbolFiles(string workspaceRoot)
    {
        var pending = new Stack<string>();
        pending.Push(workspaceRoot);

        while (pending.Count > 0)
        {
            var currentDirectory = pending.Pop();
            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                if (!WorkspaceWatcherLogic.ShouldIgnorePath(workspaceRoot, childDirectory))
                {
                    pending.Push(childDirectory);
                }
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (var filePath in files)
            {
                if (WorkspaceWatcherLogic.ShouldIgnorePath(workspaceRoot, filePath))
                {
                    continue;
                }

                var extension = Path.GetExtension(filePath);
                if (extension is ".cs" or ".csx" or ".py" or ".js" or ".mjs" or ".cjs" or ".ts" or ".tsx" or ".md" or ".markdown")
                {
                    yield return filePath;
                }
            }
        }
    }

    private void AddDefinitionSource(
        ICollection<CSharpDefinitionSource> sources,
        ISet<string> seenPaths,
        string? filePath,
        string text,
        bool isActiveDocument)
    {
        if (!isActiveDocument && !IsCSharpSourceDocument(filePath, text))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(filePath);
            }
            catch
            {
                return;
            }

            if (!seenPaths.Add(fullPath))
            {
                return;
            }

            sources.Add(new CSharpDefinitionSource(fullPath, text, isActiveDocument));
            return;
        }

        sources.Add(new CSharpDefinitionSource(string.Empty, text, isActiveDocument));
    }

    private bool IsCSharpSourceDocument(string? filePath, string? text)
        => string.Equals(DetectLanguage(filePath, text), "C#", StringComparison.Ordinal);

    private void AddWorkspaceSymbolSource(
        ICollection<WorkspaceSymbolSource> sources,
        ISet<string> seenPaths,
        string? filePath,
        string text,
        string fallbackDisplayName)
    {
        var language = DetectLanguage(filePath, text);
        if (!SupportsWorkspaceSymbols(language))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            sources.Add(new WorkspaceSymbolSource(string.Empty, fallbackDisplayName, text, language));
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch
        {
            return;
        }

        if (!seenPaths.Add(fullPath))
        {
            return;
        }

        var relativePath = !string.IsNullOrWhiteSpace(_workspaceRoot) && Directory.Exists(_workspaceRoot)
            ? Path.GetRelativePath(_workspaceRoot, fullPath)
            : Path.GetFileName(fullPath);
        sources.Add(new WorkspaceSymbolSource(fullPath, relativePath, text, language));
    }

    private void AddHeuristicNavigationSource(
        ICollection<HeuristicNavigationSource> sources,
        ISet<string> seenPaths,
        string? filePath,
        string text,
        string language,
        bool isActiveDocument)
    {
        if (!HeuristicCodeNavigationLogic.SupportsLanguage(language))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            sources.Add(new HeuristicNavigationSource(string.Empty, text, language, isActiveDocument));
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch
        {
            return;
        }

        if (!seenPaths.Add(fullPath))
        {
            return;
        }

        sources.Add(new HeuristicNavigationSource(fullPath, text, language, isActiveDocument));
    }

    private static bool SupportsWorkspaceSymbols(string language)
        => language is "C#" or "Python" or "JavaScript" or "TypeScript" or "Markdown";

    private async Task OpenDefinitionLocationAsync(TextDocument activeDocument, string filePath, int line, int column)
    {
        if (!string.IsNullOrWhiteSpace(filePath)
            && !string.Equals(activeDocument.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            await OpenFilePathAsync(filePath);
        }

        if (EditorTextBox is null)
        {
            return;
        }

        GoToLine(EditorTextBox, line, column);
    }

    private async Task ShowReferenceResultsAsync(
        TextDocument activeDocument,
        IReadOnlyList<NavigationReferenceResult> references)
    {
        var items = references
            .Select((reference, index) => new PaletteItem(
                $"reference:{index}",
                $"{Path.GetFileName(string.IsNullOrWhiteSpace(reference.FilePath) ? activeDocument.FilePath ?? "Untitled" : reference.FilePath)}:{reference.Line}:{reference.Column}",
                $"{reference.SymbolDisplay} • {reference.Preview}"))
            .ToList();

        var dialog = new SelectionPaletteDialog("Find References", "Search references...", items);
        var selected = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(selected) || !selected.StartsWith("reference:", StringComparison.Ordinal))
        {
            return;
        }

        if (!int.TryParse(selected["reference:".Length..], out var selectedIndex)
            || selectedIndex < 0
            || selectedIndex >= references.Count)
        {
            return;
        }

        var target = references[selectedIndex];
        await OpenDefinitionLocationAsync(activeDocument, target.FilePath, target.Line, target.Column);
    }

    private async Task ApplyCSharpRenameResultAsync(CSharpRenameResult result, TextDocument activeDocument)
    {
        var touchedFileBackedDocument = false;

        foreach (var change in result.Changes)
        {
            var openDocument = ResolveRenameTargetDocument(change, activeDocument);
            if (openDocument is not null)
            {
                openDocument.Text = change.UpdatedText;
                touchedFileBackedDocument |= !string.IsNullOrWhiteSpace(openDocument.FilePath);
                continue;
            }

            if (string.IsNullOrWhiteSpace(change.FilePath) || !File.Exists(change.FilePath))
            {
                continue;
            }

            await ApplyRenameToFileAsync(change.FilePath, change.UpdatedText);
            touchedFileBackedDocument = true;
        }

        if (ReferenceEquals(activeDocument, _viewModel.SelectedDocument))
        {
            SyncEditorFromDocument();
        }

        if (ReferenceEquals(activeDocument, _splitDocument))
        {
            SyncSplitEditorFromDocument();
        }

        if (touchedFileBackedDocument)
        {
            InvalidateGitStatusCache();
            UpdateGitPanel(forceRefresh: true);
            InvalidateGitDiffGutterCache();
            UpdateGitDiffGutter();
        }

        PersistState();
    }

    private async Task ApplyHeuristicRenameResultAsync(HeuristicRenameResult result, TextDocument activeDocument)
    {
        var touchedFileBackedDocument = false;

        foreach (var change in result.Changes)
        {
            var openDocument = ResolveHeuristicRenameTargetDocument(change, activeDocument);
            if (openDocument is not null)
            {
                openDocument.Text = change.UpdatedText;
                touchedFileBackedDocument |= !string.IsNullOrWhiteSpace(openDocument.FilePath);
                continue;
            }

            if (string.IsNullOrWhiteSpace(change.FilePath) || !File.Exists(change.FilePath))
            {
                continue;
            }

            await ApplyRenameToFileAsync(change.FilePath, change.UpdatedText);
            touchedFileBackedDocument = true;
        }

        if (ReferenceEquals(activeDocument, _viewModel.SelectedDocument))
        {
            SyncEditorFromDocument();
        }

        if (ReferenceEquals(activeDocument, _splitDocument))
        {
            SyncSplitEditorFromDocument();
        }

        if (touchedFileBackedDocument)
        {
            InvalidateGitStatusCache();
            UpdateGitPanel(forceRefresh: true);
            InvalidateGitDiffGutterCache();
            UpdateGitDiffGutter();
        }

        PersistState();
    }

    private TextDocument? ResolveRenameTargetDocument(CSharpRenameChange change, TextDocument activeDocument)
    {
        if (string.IsNullOrWhiteSpace(change.FilePath))
        {
            return change.IsActiveDocument ? activeDocument : null;
        }

        return _viewModel.Documents.FirstOrDefault(document =>
            !string.IsNullOrWhiteSpace(document.FilePath)
            && PathsEqual(document.FilePath!, change.FilePath));
    }

    private TextDocument? ResolveHeuristicRenameTargetDocument(HeuristicRenameChange change, TextDocument activeDocument)
    {
        if (string.IsNullOrWhiteSpace(change.FilePath))
        {
            return change.IsActiveDocument ? activeDocument : null;
        }

        return _viewModel.Documents.FirstOrDefault(document =>
            !string.IsNullOrWhiteSpace(document.FilePath)
            && PathsEqual(document.FilePath!, change.FilePath));
    }

    private async Task ApplyRenameToFileAsync(string filePath, string updatedText)
    {
        await using var input = new FileStream(
            filePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite,
                Options = FileOptions.SequentialScan,
                BufferSize = 64 * 1024,
            });

        var document = await _fileService.LoadAsync(input, filePath: filePath);
        document.Text = updatedText;
        await _fileService.SaveToFileAsync(document, filePath);
    }

    private string GetIdentifierNearCaret(string text, int caretOffset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var left = Math.Clamp(caretOffset, 0, text.Length);
        var right = left;

        while (left > 0 && IsIdentifierChar(text[left - 1]))
        {
            left--;
        }

        while (right < text.Length && IsIdentifierChar(text[right]))
        {
            right++;
        }

        return right > left ? text[left..right] : string.Empty;
    }

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private sealed record NavigationReferenceResult(
        string FilePath,
        int Line,
        int Column,
        string Preview,
        string SymbolDisplay);
}

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
        _commandPaletteActions["edit.searchInFiles"] = () => OnShowSearchInFilesClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.replaceInFiles"] = () => OnReplaceInFilesMenuClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.goto"] = () => OnGoToLineClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.formatDocument"] = () => OnFormatDocumentClick(this, new RoutedEventArgs());
        _commandPaletteActions["edit.formatSelection"] = () => OnFormatSelectionClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.split"] = () => OnToggleSplitViewClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.minimap"] = () => OnToggleMiniMapClick(this, new RoutedEventArgs());
        _commandPaletteActions["view.terminal"] = () => OnToggleTerminalClick(this, new RoutedEventArgs());
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
            new PaletteItem("edit.searchInFiles", "Search in Files", "Edit"),
            new PaletteItem("edit.replaceInFiles", "Replace in Files", "Edit"),
            new PaletteItem("edit.goto", "Go To Line", "Edit"),
            new PaletteItem("edit.formatDocument", "Format Document", "Format"),
            new PaletteItem("edit.formatSelection", "Format Selection", "Format"),
            new PaletteItem("view.split", "Toggle Split Editor", "View"),
            new PaletteItem("view.minimap", "Toggle Mini Map", "View"),
            new PaletteItem("view.terminal", "Toggle Terminal", "View"),
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
            foreach (var kw in CSharpCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else if (string.Equals(language, "JavaScript", StringComparison.Ordinal))
        {
            foreach (var kw in JavaScriptCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else if (string.Equals(language, "TypeScript", StringComparison.Ordinal))
        {
            foreach (var kw in TypeScriptCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else if (string.Equals(language, "Python", StringComparison.Ordinal))
        {
            foreach (var kw in PythonCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }
        else if (string.Equals(language, "SQL", StringComparison.Ordinal))
        {
            foreach (var kw in SqlCompletionKeywords)
            {
                suggestions.Add(kw);
            }
        }

        foreach (Match m in Regex.Matches(text, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
        {
            if (!m.Success || m.Length < 2)
            {
                continue;
            }

            suggestions.Add(m.Value);
            if (suggestions.Count > 3000)
            {
                break;
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

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';
}

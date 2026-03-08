using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NotepadSharp.App;

public partial class MainWindow
{
    private void OnShowSearchInFilesClick(object? sender, RoutedEventArgs e)
    {
        SetSidebarSection("Search", persist: true);

        if (SearchInFilesTextBox is not null)
        {
            if (string.IsNullOrWhiteSpace(SearchInFilesTextBox.Text) && !string.IsNullOrWhiteSpace(_viewModel.FindText))
            {
                SearchInFilesTextBox.Text = _viewModel.FindText;
            }

            SearchInFilesTextBox.Focus();
            SearchInFilesTextBox.CaretIndex = SearchInFilesTextBox.Text?.Length ?? 0;
        }
    }

    private void OnReplaceInFilesMenuClick(object? sender, RoutedEventArgs e)
    {
        OnShowSearchInFilesClick(sender, e);
        if (ReplaceInFilesTextBox is not null)
        {
            ReplaceInFilesTextBox.Focus();
            ReplaceInFilesTextBox.CaretIndex = ReplaceInFilesTextBox.Text?.Length ?? 0;
        }
    }

    private void OnSearchInFilesClick(object? sender, RoutedEventArgs e)
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Select a workspace folder first.";
            }

            return;
        }

        var query = SearchInFilesTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Type a search query.";
            }

            return;
        }

        var useRegex = SearchInFilesRegexCheckBox?.IsChecked == true;
        var matchCase = SearchInFilesCaseCheckBox?.IsChecked == true;
        var regex = BuildSearchRegex(query, useRegex, matchCase);
        if (regex is null)
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Invalid regex pattern.";
            }

            return;
        }

        var (scanned, matchedFiles) = ScanWorkspaceMatches(regex, maxMatches: 1500);
        RebuildSearchTree();

        if (SearchInFilesSummaryTextBlock is not null)
        {
            SearchInFilesSummaryTextBlock.Text = $"{_searchResultItems.Count} matches in {matchedFiles.Count} files (scanned {scanned}).";
        }
    }

    private (int scanned, HashSet<string> matchedFiles) ScanWorkspaceMatches(Regex regex, int maxMatches)
    {
        _searchResultItems.Clear();
        var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        foreach (var file in EnumerateWorkspaceFiles(_workspaceRoot!))
        {
            scanned++;
            var lineNo = 0;
            IEnumerable<string> lines;
            try
            {
                lines = File.ReadLines(file);
            }
            catch
            {
                continue;
            }

            foreach (var raw in lines)
            {
                lineNo++;
                foreach (Match m in regex.Matches(raw))
                {
                    if (!m.Success)
                    {
                        continue;
                    }

                    _searchResultItems.Add(new SearchResultItem(
                        file,
                        ToRelativePath(_workspaceRoot!, file),
                        lineNo,
                        m.Index + 1,
                        Math.Max(1, m.Length),
                        raw.Trim()));

                    matchedFiles.Add(file);
                    if (_searchResultItems.Count >= maxMatches)
                    {
                        break;
                    }
                }

                if (_searchResultItems.Count >= maxMatches)
                {
                    break;
                }
            }

            if (_searchResultItems.Count >= maxMatches)
            {
                break;
            }
        }

        return (scanned, matchedFiles);
    }

    private void RebuildSearchTree(Dictionary<string, string>? replacementPreviewByKey = null)
    {
        _searchTreeRootNodes.Clear();
        if (_searchResultItems.Count == 0)
        {
            if (SearchInFilesResultsTreeView is not null)
            {
                SearchInFilesResultsTreeView.ItemsSource = Array.Empty<SearchTreeNode>();
            }
            return;
        }

        var grouped = _searchResultItems
            .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var relative = ToRelativePath(_workspaceRoot ?? string.Empty, group.Key);
            var fileNode = new SearchTreeNode
            {
                DisplayText = $"{relative}  ({group.Count()} matches)",
            };

            foreach (var item in group.OrderBy(i => i.Line).ThenBy(i => i.Column))
            {
                var key = $"{item.FilePath}|{item.Line}|{item.Column}|{item.Length}";
                var preview = replacementPreviewByKey is not null && replacementPreviewByKey.TryGetValue(key, out var replaced)
                    ? $"{item.Line}:{item.Column}  {item.Preview}  =>  {replaced}"
                    : $"{item.Line}:{item.Column}  {item.Preview}";

                fileNode.Children.Add(new SearchTreeNode
                {
                    DisplayText = preview,
                    Location = new SearchResultLocation
                    {
                        FilePath = item.FilePath,
                        Line = item.Line,
                        Column = item.Column,
                        Length = item.Length,
                    },
                });
            }

            _searchTreeRootNodes.Add(fileNode);
        }

        if (SearchInFilesResultsTreeView is not null)
        {
            SearchInFilesResultsTreeView.ItemsSource = _searchTreeRootNodes.ToList();
        }
    }

    private void OnPreviewReplaceInFilesClick(object? sender, RoutedEventArgs e)
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Select a workspace folder first.";
            }

            return;
        }

        var query = SearchInFilesTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Type a search query before preview.";
            }

            return;
        }

        var replacement = ReplaceInFilesTextBox?.Text ?? string.Empty;
        var useRegex = SearchInFilesRegexCheckBox?.IsChecked == true;
        var matchCase = SearchInFilesCaseCheckBox?.IsChecked == true;
        var regex = BuildSearchRegex(query, useRegex, matchCase);
        if (regex is null)
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Invalid regex pattern.";
            }

            return;
        }

        var (_, matchedFiles) = ScanWorkspaceMatches(regex, maxMatches: 1200);
        var previewMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in _searchResultItems)
        {
            try
            {
                var replaced = regex.Replace(item.Preview, replacement);
                var key = $"{item.FilePath}|{item.Line}|{item.Column}|{item.Length}";
                previewMap[key] = replaced;
            }
            catch
            {
                // ignore bad replacement expression
            }
        }

        RebuildSearchTree(previewMap);
        if (SearchInFilesSummaryTextBlock is not null)
        {
            SearchInFilesSummaryTextBlock.Text = $"Previewing {_searchResultItems.Count} replacements in {matchedFiles.Count} files.";
        }
    }

    private async void OnReplaceInFilesClick(object? sender, RoutedEventArgs e)
    {
        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Select a workspace folder first.";
            }

            return;
        }

        var query = SearchInFilesTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Type a search query before replace.";
            }

            return;
        }

        var replacement = ReplaceInFilesTextBox?.Text ?? string.Empty;
        var useRegex = SearchInFilesRegexCheckBox?.IsChecked == true;
        var matchCase = SearchInFilesCaseCheckBox?.IsChecked == true;
        var regex = BuildSearchRegex(query, useRegex, matchCase);
        if (regex is null)
        {
            if (SearchInFilesSummaryTextBlock is not null)
            {
                SearchInFilesSummaryTextBlock.Text = "Invalid regex pattern.";
            }

            return;
        }

        var replacements = 0;
        var changed = new List<string>();
        foreach (var file in EnumerateWorkspaceFiles(_workspaceRoot!))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            var localCount = 0;
            var updated = regex.Replace(text, _ =>
            {
                localCount++;
                return replacement;
            });

            if (localCount <= 0 || string.Equals(updated, text, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                File.WriteAllText(file, updated);
                replacements += localCount;
                changed.Add(file);
            }
            catch
            {
                // Ignore locked/unwritable files.
            }
        }

        foreach (var path in changed)
        {
            var doc = _viewModel.Documents.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.FilePath)
                && string.Equals(Path.GetFullPath(d.FilePath!), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

            if (doc is null || doc.IsDirty)
            {
                continue;
            }

            await ReloadFromDiskAsync(doc);
        }

        if (SearchInFilesSummaryTextBlock is not null)
        {
            SearchInFilesSummaryTextBlock.Text = $"Replaced {replacements} matches across {changed.Count} files.";
        }

        OnSearchInFilesClick(sender, e);
    }

    private static Regex? BuildSearchRegex(string query, bool useRegex, bool matchCase)
    {
        try
        {
            var pattern = useRegex ? query : Regex.Escape(query);
            var options = RegexOptions.Compiled;
            if (!matchCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            return new Regex(pattern, options);
        }
        catch
        {
            return null;
        }
    }

    private async void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SearchInFilesResultsTreeView?.SelectedItem is not SearchTreeNode node || node.Location is null)
        {
            return;
        }

        await OpenSearchResultAsync(node.Location);
    }

    private async Task OpenSearchResultAsync(SearchResultLocation item)
    {
        await OpenFilePathAsync(item.FilePath);
        if (EditorTextBox is null)
        {
            return;
        }

        GoToLine(EditorTextBox, item.Line, item.Column);
        var start = EditorTextBox.CaretOffset;
        var end = Math.Min(start + Math.Max(1, item.Length), (EditorTextBox.Text ?? string.Empty).Length);
        SetSelection(start, end);
    }

}

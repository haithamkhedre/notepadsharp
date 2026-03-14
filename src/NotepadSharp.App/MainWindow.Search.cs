using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NotepadSharp.App.Services;

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

    private async void OnSearchInFilesClick(object? sender, RoutedEventArgs e)
    {
        if (!TryBuildSearchRequest(maxMatches: 1500, out var regex, out var error))
        {
            SetSearchInFilesSummary(error);
            return;
        }

        var result = await RunSearchOperationAsync(
            "Searching",
            cancellationToken => _workspaceSearchService.SearchAsync(_workspaceRoot!, regex!, 1500, progress =>
            {
                Dispatcher.UIThread.Post(() => SetSearchInFilesSummary(FormatSearchProgressText("Searching", progress)));
            }, cancellationToken));

        if (result is null)
        {
            return;
        }

        ApplySearchResults(result);
        RebuildSearchTree();
        SetSearchInFilesSummary(FormatSearchCompletedText(result));
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

    private async void OnPreviewReplaceInFilesClick(object? sender, RoutedEventArgs e)
    {
        if (!TryBuildSearchRequest(maxMatches: 1200, out var regex, out var error))
        {
            SetSearchInFilesSummary(error);
            return;
        }

        var replacement = ReplaceInFilesTextBox?.Text ?? string.Empty;
        var result = await RunSearchOperationAsync(
            "Previewing",
            cancellationToken => _workspaceSearchService.SearchAsync(_workspaceRoot!, regex!, 1200, progress =>
            {
                Dispatcher.UIThread.Post(() => SetSearchInFilesSummary(FormatSearchProgressText("Previewing", progress)));
            }, cancellationToken));

        if (result is null)
        {
            return;
        }

        ApplySearchResults(result);
        var previewMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in _searchResultItems)
        {
            try
            {
                var replaced = regex!.Replace(item.Preview, replacement);
                var key = $"{item.FilePath}|{item.Line}|{item.Column}|{item.Length}";
                previewMap[key] = replaced;
            }
            catch
            {
                // ignore bad replacement expression
            }
        }

        RebuildSearchTree(previewMap);
        var previewSkipText = result.SkippedFileCount > 0
            ? $"; skipped {result.SkippedFileCount} large/binary files"
            : string.Empty;
        SetSearchInFilesSummary($"Previewing {_searchResultItems.Count} replacements in {result.MatchedFileCount} files{previewSkipText}.");
    }

    private async void OnReplaceInFilesClick(object? sender, RoutedEventArgs e)
    {
        if (!TryBuildSearchRequest(maxMatches: 0, out var regex, out var error))
        {
            SetSearchInFilesSummary(error);
            return;
        }

        var replacement = ReplaceInFilesTextBox?.Text ?? string.Empty;
        var result = await RunSearchOperationAsync(
            "Replacing",
            cancellationToken => _workspaceSearchService.ReplaceAsync(_workspaceRoot!, regex!, replacement, progress =>
            {
                var skipText = progress.SkippedFileCount > 0
                    ? $", {progress.SkippedFileCount} large/binary files skipped"
                    : string.Empty;
                Dispatcher.UIThread.Post(() => SetSearchInFilesSummary(
                    $"Replacing... {progress.ScannedFileCount} files scanned, {progress.ReplacementCount} replacements in {progress.FilesChanged} files{skipText}."));
            }, cancellationToken));

        if (result is not WorkspaceReplaceResult replaceResult)
        {
            return;
        }

        foreach (var path in replaceResult.ChangedFiles)
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

        SetSearchInFilesSummary(FormatReplaceCompletedText(replaceResult));

        OnSearchInFilesClick(sender, e);
    }

    private void OnCancelSearchInFilesClick(object? sender, RoutedEventArgs e)
    {
        _searchInFilesCts?.Cancel();
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

    private bool TryBuildSearchRequest(int maxMatches, out Regex? regex, out string error)
    {
        regex = null;
        error = string.Empty;

        EnsureWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            error = "Select a workspace folder first.";
            return false;
        }

        var query = SearchInFilesTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            error = maxMatches switch
            {
                1200 => "Type a search query before preview.",
                0 => "Type a search query before replace.",
                _ => "Type a search query.",
            };
            return false;
        }

        var useRegex = SearchInFilesRegexCheckBox?.IsChecked == true;
        var matchCase = SearchInFilesCaseCheckBox?.IsChecked == true;
        regex = BuildSearchRegex(query, useRegex, matchCase);
        if (regex is null)
        {
            error = "Invalid regex pattern.";
            return false;
        }

        return true;
    }

    private async Task<T?> RunSearchOperationAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> operation)
    {
        _searchInFilesCts?.Cancel();
        _searchInFilesCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchInFilesCts = cts;
        _isSearchInFilesBusy = true;
        UpdateSearchInFilesUi();

        try
        {
            return await operation(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_searchInFilesCts, cts))
            {
                SetSearchInFilesSummary($"{operationName} canceled.");
            }

            return default;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_searchInFilesCts, cts))
            {
                SetSearchInFilesSummary($"{operationName} failed: {FirstLine(ex.Message)}");
            }

            return default;
        }
        finally
        {
            if (ReferenceEquals(_searchInFilesCts, cts))
            {
                _searchInFilesCts = null;
                _isSearchInFilesBusy = false;
                UpdateSearchInFilesUi();
            }

            cts.Dispose();
        }
    }

    private void ApplySearchResults(WorkspaceSearchResult result)
    {
        _searchResultItems.Clear();
        _searchResultItems.AddRange(result.Matches);
    }

    private string FormatSearchCompletedText(WorkspaceSearchResult result)
    {
        var suffix = result.HitMatchLimit ? " Search limit reached." : string.Empty;
        return $"{result.Matches.Count} matches in {result.MatchedFileCount} files ({FormatSearchScanDetails(result.ScannedFileCount, result.SkippedFileCount)}).{suffix}";
    }

    private static string FormatSearchProgressText(string operationName, WorkspaceSearchProgress progress)
    {
        var limitText = progress.HitMatchLimit ? " limit reached" : string.Empty;
        var skipText = progress.SkippedFileCount > 0
            ? $", {progress.SkippedFileCount} large/binary files skipped"
            : string.Empty;
        return $"{operationName}... {progress.ScannedFileCount} files scanned, {progress.MatchCount} matches in {progress.MatchedFileCount} files{skipText}{limitText}.";
    }

    private static string FormatReplaceCompletedText(WorkspaceReplaceResult result)
        => $"Replaced {result.ReplacementCount} matches across {result.FilesChanged} files ({FormatSearchScanDetails(result.ScannedFileCount, result.SkippedFileCount)}).";

    private static string FormatSearchScanDetails(int scannedFileCount, int skippedFileCount)
        => skippedFileCount > 0
            ? $"scanned {scannedFileCount}, skipped {skippedFileCount} large/binary files"
            : $"scanned {scannedFileCount}";

    private void SetSearchInFilesSummary(string text)
    {
        if (SearchInFilesSummaryTextBlock is not null)
        {
            SearchInFilesSummaryTextBlock.Text = text;
        }
    }

    private void UpdateSearchInFilesUi()
    {
        if (SearchInFilesTextBox is not null)
        {
            SearchInFilesTextBox.IsEnabled = !_isSearchInFilesBusy;
        }

        if (ReplaceInFilesTextBox is not null)
        {
            ReplaceInFilesTextBox.IsEnabled = !_isSearchInFilesBusy;
        }

        if (SearchInFilesCaseCheckBox is not null)
        {
            SearchInFilesCaseCheckBox.IsEnabled = !_isSearchInFilesBusy;
        }

        if (SearchInFilesRegexCheckBox is not null)
        {
            SearchInFilesRegexCheckBox.IsEnabled = !_isSearchInFilesBusy;
        }

        if (SearchInFilesSearchButton is not null)
        {
            SearchInFilesSearchButton.IsEnabled = !_isSearchInFilesBusy;
        }

        if (SearchInFilesPreviewButton is not null)
        {
            SearchInFilesPreviewButton.IsEnabled = !_isSearchInFilesBusy;
        }

        if (SearchInFilesReplaceButton is not null)
        {
            SearchInFilesReplaceButton.IsEnabled = !_isSearchInFilesBusy;
        }

        if (SearchInFilesCancelButton is not null)
        {
            SearchInFilesCancelButton.IsEnabled = _isSearchInFilesBusy;
        }
    }

}

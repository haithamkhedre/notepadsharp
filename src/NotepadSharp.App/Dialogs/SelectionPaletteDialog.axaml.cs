using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;

namespace NotepadSharp.App.Dialogs;

public partial class SelectionPaletteDialog : Window
{
    private readonly List<PaletteItem> _allItems;
    private readonly ObservableCollection<PaletteItem> _filteredItems = new();

    public SelectionPaletteDialog()
        : this("Command Palette", "Type to filter...", Array.Empty<PaletteItem>())
    {
    }

    public SelectionPaletteDialog(string title, string placeholder, IEnumerable<PaletteItem> items)
    {
        InitializeComponent();

        Title = title;
        SearchTextBox.Watermark = placeholder;

        _allItems = items
            .Where(i => i is not null)
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        ItemsListBox.ItemsSource = _filteredItems;
        SearchTextBox.TextChanged += (_, __) => ApplyFilter();

        Opened += (_, __) =>
        {
            ApplyFilter();
            SearchTextBox.Focus();
            SearchTextBox.CaretIndex = SearchTextBox.Text?.Length ?? 0;
        };

        KeyDown += OnDialogKeyDown;
        ItemsListBox.DoubleTapped += (_, __) => ConfirmSelection();
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            MoveSelection(+1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_filteredItems.Count == 0)
        {
            return;
        }

        var current = ItemsListBox.SelectedItem as PaletteItem;
        var idx = current is null ? -1 : _filteredItems.IndexOf(current);
        var next = idx + delta;
        if (next < 0)
        {
            next = 0;
        }

        if (next >= _filteredItems.Count)
        {
            next = _filteredItems.Count - 1;
        }

        ItemsListBox.SelectedItem = _filteredItems[next];
        ItemsListBox.ScrollIntoView(_filteredItems[next]);
    }

    private void ConfirmSelection()
    {
        if (ItemsListBox.SelectedItem is PaletteItem item)
        {
            Close(item.Id);
            return;
        }

        if (_filteredItems.Count > 0)
        {
            Close(_filteredItems[0].Id);
            return;
        }

        Close(null);
    }

    private void ApplyFilter()
    {
        var q = SearchTextBox.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(q)
            ? _allItems
            : _allItems.Where(i => i.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        _filteredItems.Clear();
        foreach (var item in filtered)
        {
            _filteredItems.Add(item);
        }

        HintTextBlock.Text = _filteredItems.Count == 0
            ? "No results"
            : $"{_filteredItems.Count} result(s) | Enter: select | Esc: cancel";

        ItemsListBox.SelectedItem = _filteredItems.Count > 0 ? _filteredItems[0] : null;
    }
}

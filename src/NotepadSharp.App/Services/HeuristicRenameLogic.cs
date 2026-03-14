using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NotepadSharp.App.Services;

public sealed record HeuristicRenameChange(
    string FilePath,
    string UpdatedText,
    bool IsActiveDocument);

public sealed record HeuristicRenameResult(
    string SymbolDisplay,
    IReadOnlyList<HeuristicRenameChange> Changes);

public static class HeuristicRenameLogic
{
    public static HeuristicRenameResult? RenameSymbol(
        IReadOnlyList<HeuristicNavigationSource> sources,
        int caretOffset,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (sources.Count == 0)
        {
            return null;
        }

        var activeSource = sources.FirstOrDefault(source => source.IsActiveDocument) ?? sources[0];
        if (!HeuristicCodeNavigationLogic.SupportsLanguage(activeSource.Language))
        {
            return null;
        }

        var activeText = activeSource.Text ?? string.Empty;
        var clampedCaretOffset = Math.Clamp(caretOffset, 0, activeText.Length);
        var currentIdentifier = GetIdentifierNearCaret(activeText, clampedCaretOffset);
        if (string.IsNullOrWhiteSpace(currentIdentifier)
            || string.Equals(currentIdentifier, newName, StringComparison.Ordinal))
        {
            return null;
        }

        var definition = HeuristicCodeNavigationLogic.TryResolveDefinition(sources, clampedCaretOffset);
        var references = HeuristicCodeNavigationLogic.FindReferences(sources, clampedCaretOffset, maxCount: int.MaxValue);
        var locations = new List<HeuristicRenameLocation>();

        if (definition is not null)
        {
            locations.Add(new HeuristicRenameLocation(definition.FilePath, definition.Line, definition.Column));
        }

        locations.AddRange(references.Select(reference => new HeuristicRenameLocation(reference.FilePath, reference.Line, reference.Column)));
        if (locations.Count == 0)
        {
            return null;
        }

        var changes = new List<HeuristicRenameChange>();
        foreach (var source in sources.Where(source => string.Equals(source.Language, activeSource.Language, StringComparison.Ordinal)))
        {
            var updatedText = ApplyRename(source.Text ?? string.Empty, locations, source.FilePath, currentIdentifier, newName);
            if (string.Equals(updatedText, source.Text ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            changes.Add(new HeuristicRenameChange(source.FilePath, updatedText, source.IsActiveDocument));
        }

        if (changes.Count == 0)
        {
            return null;
        }

        return new HeuristicRenameResult(definition?.SymbolDisplay ?? currentIdentifier, changes);
    }

    private static string ApplyRename(
        string text,
        IReadOnlyList<HeuristicRenameLocation> locations,
        string filePath,
        string oldName,
        string newName)
    {
        var lineStarts = BuildLineStartOffsets(text);
        var offsets = locations
            .Where(location => string.Equals(location.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .Select(location => TryGetOffset(lineStarts, text, location.Line, location.Column))
            .Where(offset => offset >= 0 && HasIdentifierAt(text, offset, oldName))
            .Distinct()
            .OrderByDescending(offset => offset)
            .ToList();

        if (offsets.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);
        foreach (var offset in offsets)
        {
            builder.Remove(offset, oldName.Length);
            builder.Insert(offset, newName);
        }

        return builder.ToString();
    }

    private static List<int> BuildLineStartOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                offsets.Add(index + 1);
            }
            else if (text[index] == '\n')
            {
                offsets.Add(index + 1);
            }
        }

        return offsets;
    }

    private static int TryGetOffset(IReadOnlyList<int> lineStarts, string text, int line, int column)
    {
        if (line <= 0 || line > lineStarts.Count || column <= 0)
        {
            return -1;
        }

        var offset = lineStarts[line - 1] + column - 1;
        return offset >= 0 && offset <= text.Length ? offset : -1;
    }

    private static bool HasIdentifierAt(string text, int offset, string identifier)
    {
        if (offset < 0 || offset + identifier.Length > text.Length)
        {
            return false;
        }

        if (!string.Equals(text.Substring(offset, identifier.Length), identifier, StringComparison.Ordinal))
        {
            return false;
        }

        var hasLeftBoundary = offset == 0 || !IsIdentifierChar(text[offset - 1]);
        var hasRightBoundary = offset + identifier.Length >= text.Length || !IsIdentifierChar(text[offset + identifier.Length]);
        return hasLeftBoundary && hasRightBoundary;
    }

    private static string GetIdentifierNearCaret(string text, int caretOffset)
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

    private static bool IsIdentifierChar(char ch)
        => char.IsLetterOrDigit(ch) || ch == '_';

    private sealed record HeuristicRenameLocation(string FilePath, int Line, int Column);
}

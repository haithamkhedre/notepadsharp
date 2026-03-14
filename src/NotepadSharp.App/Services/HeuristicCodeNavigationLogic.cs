using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NotepadSharp.App.Services;

public sealed record HeuristicNavigationSource(
    string FilePath,
    string Text,
    string Language,
    bool IsActiveDocument);

public sealed record HeuristicDefinitionLocation(
    string FilePath,
    int Line,
    int Column,
    string SymbolDisplay);

public sealed record HeuristicReferenceLocation(
    string FilePath,
    int Line,
    int Column,
    string Preview,
    string SymbolDisplay);

public static class HeuristicCodeNavigationLogic
{
    public static bool SupportsLanguage(string language)
        => language is "JavaScript" or "TypeScript" or "Python";

    public static HeuristicDefinitionLocation? TryResolveDefinition(
        IReadOnlyList<HeuristicNavigationSource> sources,
        int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return null;
        }

        var activeSource = sources.FirstOrDefault(source => source.IsActiveDocument) ?? sources[0];
        if (!SupportsLanguage(activeSource.Language))
        {
            return null;
        }

        var activeText = activeSource.Text ?? string.Empty;
        var clampedCaretOffset = Math.Clamp(caretOffset, 0, activeText.Length);
        var identifier = GetIdentifierNearCaret(activeText, clampedCaretOffset);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var caretLine = GetLineColumnFromOffset(activeText, clampedCaretOffset).Line;
        var relevantSources = sources
            .Where(source => string.Equals(source.Language, activeSource.Language, StringComparison.Ordinal))
            .ToList();

        var candidate = relevantSources
            .SelectMany(source => DocumentSymbolLogic.GetSymbols(source.Text ?? string.Empty, source.Language)
                .Where(symbol => string.Equals(symbol.Title, identifier, StringComparison.Ordinal))
                .Select(symbol => new
                {
                    Source = source,
                    Symbol = symbol,
                    IsSameFile = IsSameSource(activeSource, source),
                    IsPreferredDirection = IsSameSource(activeSource, source) && symbol.Line <= caretLine,
                    Distance = IsSameSource(activeSource, source) ? Math.Abs(symbol.Line - caretLine) : int.MaxValue,
                }))
            .OrderBy(candidateInfo => candidateInfo.IsSameFile ? 0 : 1)
            .ThenBy(candidateInfo => candidateInfo.IsPreferredDirection ? 0 : 1)
            .ThenBy(candidateInfo => candidateInfo.Distance)
            .ThenBy(candidateInfo => candidateInfo.Symbol.Line)
            .ThenBy(candidateInfo => candidateInfo.Symbol.Column)
            .ThenBy(candidateInfo => candidateInfo.Source.FilePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (candidate is null)
        {
            return null;
        }

        return new HeuristicDefinitionLocation(
            candidate.Source.FilePath,
            candidate.Symbol.Line,
            candidate.Symbol.Column,
            $"{candidate.Symbol.Kind} {candidate.Symbol.Title}");
    }

    public static IReadOnlyList<HeuristicReferenceLocation> FindReferences(
        IReadOnlyList<HeuristicNavigationSource> sources,
        int caretOffset,
        int maxCount = 200)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return Array.Empty<HeuristicReferenceLocation>();
        }

        var activeSource = sources.FirstOrDefault(source => source.IsActiveDocument) ?? sources[0];
        if (!SupportsLanguage(activeSource.Language))
        {
            return Array.Empty<HeuristicReferenceLocation>();
        }

        var activeText = activeSource.Text ?? string.Empty;
        var clampedCaretOffset = Math.Clamp(caretOffset, 0, activeText.Length);
        var identifier = GetIdentifierNearCaret(activeText, clampedCaretOffset);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Array.Empty<HeuristicReferenceLocation>();
        }

        maxCount = Math.Max(1, maxCount);
        var definition = TryResolveDefinition(sources, clampedCaretOffset);
        var relevantSources = sources
            .Where(source => string.Equals(source.Language, activeSource.Language, StringComparison.Ordinal))
            .OrderBy(source => IsSameSource(activeSource, source) ? 0 : 1)
            .ThenBy(source => source.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matcher = new Regex($@"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])");
        var results = new List<HeuristicReferenceLocation>();

        foreach (var source in relevantSources)
        {
            var lines = NormalizeLines(source.Text ?? string.Empty);
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                foreach (Match match in matcher.Matches(line))
                {
                    var lineNumber = lineIndex + 1;
                    var columnNumber = match.Index + 1;
                    if (definition is not null
                        && string.Equals(definition.FilePath, source.FilePath, StringComparison.OrdinalIgnoreCase)
                        && definition.Line == lineNumber
                        && definition.Column == columnNumber)
                    {
                        continue;
                    }

                    results.Add(new HeuristicReferenceLocation(
                        FilePath: source.FilePath,
                        Line: lineNumber,
                        Column: columnNumber,
                        Preview: line.Trim(),
                        SymbolDisplay: definition?.SymbolDisplay ?? identifier));

                    if (results.Count >= maxCount)
                    {
                        return results;
                    }
                }
            }
        }

        return results;
    }

    private static bool IsSameSource(HeuristicNavigationSource left, HeuristicNavigationSource right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.FilePath) && !string.IsNullOrWhiteSpace(right.FilePath))
        {
            return string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        return left.IsActiveDocument && right.IsActiveDocument;
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

    private static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private static (int Line, int Column) GetLineColumnFromOffset(string text, int offset)
    {
        var clampedOffset = Math.Clamp(offset, 0, text.Length);
        var line = 1;
        var column = 1;

        for (var index = 0; index < clampedOffset; index++)
        {
            if (text[index] == '\r')
            {
                line++;
                column = 1;
                if (index + 1 < clampedOffset && text[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            if (text[index] == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return (line, column);
    }

    private static IReadOnlyList<string> NormalizeLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
}

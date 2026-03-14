using System;
using System.Collections.Generic;
using System.Linq;

namespace NotepadSharp.App.Services;

public sealed record WorkspaceSymbolSource(
    string FilePath,
    string RelativePath,
    string Text,
    string Language);

public sealed record WorkspaceSymbolInfo(
    string FilePath,
    string RelativePath,
    string Title,
    string Kind,
    int Line,
    int Column,
    string Description);

public static class WorkspaceSymbolLogic
{
    public static IReadOnlyList<WorkspaceSymbolInfo> GetSymbols(IEnumerable<WorkspaceSymbolSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return sources
            .SelectMany(source => DocumentSymbolLogic.GetSymbols(source.Text, source.Language)
                .Select(symbol => new WorkspaceSymbolInfo(
                    FilePath: source.FilePath,
                    RelativePath: source.RelativePath,
                    Title: symbol.Title,
                    Kind: symbol.Kind,
                    Line: symbol.Line,
                    Column: symbol.Column,
                    Description: symbol.Description)))
            .OrderBy(symbol => symbol.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(symbol => symbol.Line)
            .ThenBy(symbol => symbol.Column)
            .ToList();
    }
}

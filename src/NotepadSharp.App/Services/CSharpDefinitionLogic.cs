using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace NotepadSharp.App.Services;

public sealed record CSharpDefinitionSource(string FilePath, string Text, bool IsActiveDocument);

public sealed record CSharpDefinitionLocation(
    string FilePath,
    int Line,
    int Column,
    string SymbolDisplay);

public static class CSharpDefinitionLogic
{
    public static CSharpDefinitionLocation? TryResolveDefinition(
        IReadOnlyList<CSharpDefinitionSource> sources,
        int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return null;
        }

        try
        {
            var activeSource = sources.FirstOrDefault(source => source.IsActiveDocument) ?? sources[0];
            var activeTextLength = activeSource.Text?.Length ?? 0;
            var clampedCaretOffset = Math.Clamp(caretOffset, 0, activeTextLength);

            var syntaxTreeSources = sources
                .Select((source, index) => new
                {
                    Source = source,
                    Tree = CSharpSyntaxTree.ParseText(
                        SourceText.From(source.Text ?? string.Empty),
                        path: string.IsNullOrWhiteSpace(source.FilePath) ? $"untitled-{index}.cs" : source.FilePath),
                })
                .ToList();
            var syntaxTrees = syntaxTreeSources.Select(item => item.Tree).ToList();
            var activeIndex = Array.FindIndex(sources.ToArray(), source => source.IsActiveDocument);
            if (activeIndex < 0)
            {
                activeIndex = 0;
            }

            var compilation = CSharpCompilation.Create(
                "NotepadSharp.Definitions",
                syntaxTrees,
                CSharpCompletionLogic.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var activeTree = syntaxTrees[activeIndex];
            var semanticModel = compilation.GetSemanticModel(activeTree, ignoreAccessibility: true);
            var root = activeTree.GetRoot();
            var token = root.FindToken(Math.Max(0, clampedCaretOffset > 0 ? clampedCaretOffset - 1 : 0));

            foreach (var candidate in token.Parent?.AncestorsAndSelf() ?? Enumerable.Empty<SyntaxNode>())
            {
                var symbol = semanticModel.GetSymbolInfo(candidate).Symbol
                    ?? semanticModel.GetSymbolInfo(candidate).CandidateSymbols.FirstOrDefault()
                    ?? semanticModel.GetDeclaredSymbol(candidate);
                if (symbol is null)
                {
                    continue;
                }

                var location = symbol.Locations.FirstOrDefault(item => item.IsInSource);
                if (location is null)
                {
                    continue;
                }

                var span = location.GetLineSpan();
                var resolvedFilePath = syntaxTreeSources
                    .FirstOrDefault(item => ReferenceEquals(item.Tree, location.SourceTree))
                    ?.Source.FilePath
                    ?? string.Empty;

                return new CSharpDefinitionLocation(
                    resolvedFilePath,
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1,
                    symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

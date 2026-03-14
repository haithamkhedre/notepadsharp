using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NotepadSharp.App.Services;

public sealed record CSharpDiagnosticInfo(string Severity, int Line, int Column, string Message);

public static class CSharpDiagnosticsLogic
{
    public static IReadOnlyList<CSharpDiagnosticInfo> GetDiagnostics(string sourceText, int maxCount = 100)
        => GetDiagnostics(
            new[]
            {
                new CSharpDefinitionSource(string.Empty, sourceText, IsActiveDocument: true),
            },
            maxCount);

    public static IReadOnlyList<CSharpDiagnosticInfo> GetDiagnostics(
        IReadOnlyList<CSharpDefinitionSource> sources,
        int maxCount = 100)
    {
        ArgumentNullException.ThrowIfNull(sources);
        maxCount = Math.Max(1, maxCount);
        if (sources.Count == 0)
        {
            return Array.Empty<CSharpDiagnosticInfo>();
        }

        try
        {
            var syntaxTrees = sources
                .Select((source, index) => new
                {
                    Source = source,
                    Tree = CSharpSyntaxTree.ParseText(
                        source.Text ?? string.Empty,
                        path: string.IsNullOrWhiteSpace(source.FilePath) ? $"untitled-{index}.cs" : source.FilePath),
                })
                .ToList();
            var activeTree = syntaxTrees.FirstOrDefault(item => item.Source.IsActiveDocument)?.Tree ?? syntaxTrees[0].Tree;
            var compilation = CSharpCompilation.Create(
                "NotepadSharp.Diagnostics",
                syntaxTrees.Select(item => item.Tree),
                CSharpCompletionLogic.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return compilation
                .GetDiagnostics()
                .Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
                .Where(d => d.Location == Location.None || ReferenceEquals(d.Location.SourceTree, activeTree))
                .Take(maxCount)
                .Select(diag =>
                {
                    var span = diag.Location.GetLineSpan();
                    var line = span.StartLinePosition.Line + 1;
                    var col = span.StartLinePosition.Character + 1;
                    var severity = diag.Severity == DiagnosticSeverity.Error ? "Error" : "Warning";
                    return new CSharpDiagnosticInfo(severity, line, col, diag.GetMessage());
                })
                .ToList();
        }
        catch
        {
            return Array.Empty<CSharpDiagnosticInfo>();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace NotepadSharp.App.Services;

public sealed record CSharpReferenceLocation(
    string FilePath,
    int Line,
    int Column,
    string Preview,
    string SymbolDisplay);

public static class CSharpReferenceLogic
{
    public static async Task<IReadOnlyList<CSharpReferenceLocation>> FindReferencesAsync(
        IReadOnlyList<CSharpDefinitionSource> sources,
        int caretOffset,
        int maxCount = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return Array.Empty<CSharpReferenceLocation>();
        }

        maxCount = Math.Max(1, maxCount);

        try
        {
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution
                .AddProject(projectId, "NotepadSharp.References", "NotepadSharp.References", LanguageNames.CSharp)
                .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .WithProjectMetadataReferences(projectId, CSharpCompletionLogic.GetMetadataReferences());

            var documentIds = new Dictionary<DocumentId, string>(capacity: sources.Count);
            DocumentId? activeDocumentId = null;

            for (var index = 0; index < sources.Count; index++)
            {
                var source = sources[index];
                var path = string.IsNullOrWhiteSpace(source.FilePath) ? $"untitled-{index}.cs" : source.FilePath;
                var name = string.IsNullOrWhiteSpace(source.FilePath) ? $"Untitled{index + 1}.cs" : System.IO.Path.GetFileName(source.FilePath);
                var documentId = DocumentId.CreateNewId(projectId);
                solution = solution.AddDocument(documentId, name, SourceText.From(source.Text ?? string.Empty), filePath: path);
                documentIds[documentId] = source.FilePath;

                if (source.IsActiveDocument)
                {
                    activeDocumentId = documentId;
                }
            }

            activeDocumentId ??= documentIds.Keys.FirstOrDefault();
            if (activeDocumentId is null)
            {
                return Array.Empty<CSharpReferenceLocation>();
            }

            var activeDocument = solution.GetDocument(activeDocumentId);
            if (activeDocument is null)
            {
                return Array.Empty<CSharpReferenceLocation>();
            }

            var activeText = await activeDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var clampedCaretOffset = Math.Clamp(caretOffset, 0, activeText.Length);
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(activeDocument, clampedCaretOffset, cancellationToken).ConfigureAwait(false);
            if (symbol is null)
            {
                return Array.Empty<CSharpReferenceLocation>();
            }

            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
            var results = new List<CSharpReferenceLocation>();

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var document = solution.GetDocument(location.Document.Id);
                    if (document is null)
                    {
                        continue;
                    }

                    var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    var lineSpan = location.Location.GetLineSpan();
                    var lineIndex = lineSpan.StartLinePosition.Line;
                    var preview = lineIndex >= 0 && lineIndex < text.Lines.Count
                        ? text.Lines[lineIndex].ToString().Trim()
                        : string.Empty;

                    results.Add(new CSharpReferenceLocation(
                        FilePath: documentIds.TryGetValue(document.Id, out var originalPath) ? originalPath : document.FilePath ?? string.Empty,
                        Line: lineIndex + 1,
                        Column: lineSpan.StartLinePosition.Character + 1,
                        Preview: preview,
                        SymbolDisplay: symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));

                    if (results.Count >= maxCount)
                    {
                        return results;
                    }
                }
            }

            return results;
        }
        catch
        {
            return Array.Empty<CSharpReferenceLocation>();
        }
    }
}

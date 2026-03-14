using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace NotepadSharp.App.Services;

public sealed record CSharpRenameChange(
    string FilePath,
    string UpdatedText,
    bool IsActiveDocument);

public sealed record CSharpRenameResult(
    string SymbolDisplay,
    IReadOnlyList<CSharpRenameChange> Changes);

public static class CSharpRenameLogic
{
    public static async Task<CSharpRenameResult?> RenameSymbolAsync(
        IReadOnlyList<CSharpDefinitionSource> sources,
        int caretOffset,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (sources.Count == 0)
        {
            return null;
        }

        try
        {
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution
                .AddProject(projectId, "NotepadSharp.Rename", "NotepadSharp.Rename", LanguageNames.CSharp)
                .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .WithProjectMetadataReferences(projectId, CSharpCompletionLogic.GetMetadataReferences());

            var documentIds = new Dictionary<DocumentId, CSharpDefinitionSource>(capacity: sources.Count);
            DocumentId? activeDocumentId = null;

            for (var index = 0; index < sources.Count; index++)
            {
                var source = sources[index];
                var path = string.IsNullOrWhiteSpace(source.FilePath) ? $"untitled-{index}.cs" : source.FilePath;
                var name = string.IsNullOrWhiteSpace(source.FilePath) ? $"Untitled{index + 1}.cs" : System.IO.Path.GetFileName(source.FilePath);
                var documentId = DocumentId.CreateNewId(projectId);
                solution = solution.AddDocument(documentId, name, SourceText.From(source.Text ?? string.Empty), filePath: path);
                documentIds[documentId] = source;

                if (source.IsActiveDocument)
                {
                    activeDocumentId = documentId;
                }
            }

            activeDocumentId ??= documentIds.Keys.FirstOrDefault();
            if (activeDocumentId is null)
            {
                return null;
            }

            var activeDocument = solution.GetDocument(activeDocumentId);
            if (activeDocument is null)
            {
                return null;
            }

            var activeText = await activeDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var clampedCaretOffset = Math.Clamp(caretOffset, 0, activeText.Length);
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(activeDocument, clampedCaretOffset, cancellationToken).ConfigureAwait(false);
            if (symbol is null)
            {
                return null;
            }

            var renamedSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                new SymbolRenameOptions(
                    RenameOverloads: false,
                    RenameInStrings: false,
                    RenameInComments: false,
                    RenameFile: false),
                newName,
                cancellationToken).ConfigureAwait(false);

            var changes = new List<CSharpRenameChange>();
            foreach (var pair in documentIds)
            {
                var oldDocument = solution.GetDocument(pair.Key);
                var newDocument = renamedSolution.GetDocument(pair.Key);
                if (oldDocument is null || newDocument is null)
                {
                    continue;
                }

                var originalText = await oldDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var updatedText = await newDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(originalText.ToString(), updatedText.ToString(), StringComparison.Ordinal))
                {
                    continue;
                }

                changes.Add(new CSharpRenameChange(
                    pair.Value.FilePath,
                    updatedText.ToString(),
                    pair.Value.IsActiveDocument));
            }

            return changes.Count == 0
                ? null
                : new CSharpRenameResult(
                    symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    changes);
        }
        catch
        {
            return null;
        }
    }
}

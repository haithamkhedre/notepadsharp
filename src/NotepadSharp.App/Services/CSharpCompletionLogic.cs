using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NotepadSharp.App.Services;

public static class CSharpCompletionLogic
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> DefaultMetadataReferences = new(BuildDefaultMetadataReferences);

    internal static IReadOnlyList<MetadataReference> GetMetadataReferences()
        => DefaultMetadataReferences.Value;

    public static Task<IReadOnlyList<string>> GetSuggestionsAsync(
        string sourceText,
        int caretOffset,
        CancellationToken cancellationToken = default)
        => GetSuggestionsAsync(
            new[]
            {
                new CSharpDefinitionSource(string.Empty, sourceText, IsActiveDocument: true),
            },
            caretOffset,
            cancellationToken);

    public static Task<IReadOnlyList<string>> GetSuggestionsAsync(
        IReadOnlyList<CSharpDefinitionSource> sources,
        int caretOffset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var syntaxTrees = sources
                .Select((source, index) => new
                {
                    Source = source,
                    Tree = CSharpSyntaxTree.ParseText(
                        SourceText.From(source.Text ?? string.Empty),
                        path: string.IsNullOrWhiteSpace(source.FilePath) ? $"untitled-{index}.cs" : source.FilePath,
                        cancellationToken: cancellationToken),
                })
                .ToList();
            var activeSource = syntaxTrees.FirstOrDefault(item => item.Source.IsActiveDocument) ?? syntaxTrees[0];
            var clampedCaretOffset = Math.Clamp(caretOffset, 0, activeSource.Source.Text?.Length ?? 0);
            var compilation = CSharpCompilation.Create(
                "NotepadSharp.IntelliSense",
                syntaxTrees.Select(item => item.Tree),
                DefaultMetadataReferences.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(activeSource.Tree, ignoreAccessibility: true);
            var root = activeSource.Tree.GetRoot(cancellationToken);

            var memberSuggestions = GetMemberAccessSuggestions(root, semanticModel, clampedCaretOffset);
            if (memberSuggestions.Count > 0)
            {
                return Task.FromResult<IReadOnlyList<string>>(memberSuggestions);
            }

            var suggestions = semanticModel.LookupSymbols(clampedCaretOffset)
                .Select(symbol => symbol.Name)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(text => text, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(suggestions);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }

    private static IReadOnlyList<string> GetMemberAccessSuggestions(SyntaxNode root, SemanticModel semanticModel, int caretOffset)
    {
        var token = root.FindToken(Math.Max(0, caretOffset - 1));
        var memberAccess = token.Parent?
            .AncestorsAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(access => access.Name.SpanStart <= caretOffset);
        if (memberAccess is null)
        {
            return Array.Empty<string>();
        }

        var prefix = memberAccess.Name.Identifier.ValueText;
        var containerSymbol = GetMemberContainerSymbol(memberAccess.Expression, semanticModel);
        if (containerSymbol is null)
        {
            return Array.Empty<string>();
        }

        return containerSymbol
            .GetMembers()
            .Where(CanSuggestMember)
            .Select(symbol => symbol.Name)
            .Where(name => string.IsNullOrWhiteSpace(prefix) || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static INamespaceOrTypeSymbol? GetMemberContainerSymbol(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type is INamespaceOrTypeSymbol typedSymbol)
        {
            return typedSymbol;
        }

        if (typeInfo.ConvertedType is INamespaceOrTypeSymbol convertedTypeSymbol)
        {
            return convertedTypeSymbol;
        }

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        return symbol switch
        {
            INamespaceOrTypeSymbol namespaceOrTypeSymbol => namespaceOrTypeSymbol,
            ILocalSymbol localSymbol when localSymbol.Type is INamespaceOrTypeSymbol localType => localType,
            IParameterSymbol parameterSymbol when parameterSymbol.Type is INamespaceOrTypeSymbol parameterType => parameterType,
            IPropertySymbol propertySymbol when propertySymbol.Type is INamespaceOrTypeSymbol propertyType => propertyType,
            IFieldSymbol fieldSymbol when fieldSymbol.Type is INamespaceOrTypeSymbol fieldType => fieldType,
            _ => null,
        };
    }

    private static bool CanSuggestMember(ISymbol symbol)
        => symbol.Kind is SymbolKind.Method
            or SymbolKind.Property
            or SymbolKind.Field
            or SymbolKind.Event
            or SymbolKind.NamedType;

    private static IReadOnlyList<MetadataReference> BuildDefaultMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            };
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
    }
}

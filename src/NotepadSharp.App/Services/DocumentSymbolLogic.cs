using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NotepadSharp.App.Services;

public sealed record DocumentSymbolInfo(
    string Title,
    string Kind,
    int Line,
    int Column,
    string Description);

public static class DocumentSymbolLogic
{
    public static IReadOnlyList<DocumentSymbolInfo> GetSymbols(string text, string language)
        => language switch
        {
            "C#" => GetCSharpSymbols(text),
            "Python" => GetRegexSymbols(
                text,
                new RegexSymbolPattern(@"^\s*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\(|:)", "Class"),
                new RegexSymbolPattern(@"^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", "Function")),
            "JavaScript" => GetRegexSymbols(
                text,
                new RegexSymbolPattern(@"^\s*(?:export\s+)?(?:default\s+)?class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", "Class"),
                new RegexSymbolPattern(@"^\s*(?:export\s+)?(?:async\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", "Function"),
                new RegexSymbolPattern(@"^\s*(?:export\s+)?(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)\s*=>", "Function")),
            "TypeScript" => GetRegexSymbols(
                text,
                new RegexSymbolPattern(@"^\s*(?:export\s+)?(?:default\s+)?class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", "Class"),
                new RegexSymbolPattern(@"^\s*(?:export\s+)?interface\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", "Interface"),
                new RegexSymbolPattern(@"^\s*(?:export\s+)?type\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", "Type"),
                new RegexSymbolPattern(@"^\s*(?:export\s+)?enum\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b", "Enum"),
                new RegexSymbolPattern(@"^\s*(?:export\s+)?(?:async\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", "Function"),
                new RegexSymbolPattern(@"^\s*(?:export\s+)?(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)\s*=>", "Function")),
            "Markdown" => GetRegexSymbols(text, new RegexSymbolPattern(@"^\s*#{1,6}\s+(?<name>.+)$", "Heading")),
            _ => Array.Empty<DocumentSymbolInfo>(),
        };

    private static IReadOnlyList<DocumentSymbolInfo> GetCSharpSymbols(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<DocumentSymbolInfo>();
        }

        try
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(text);
            var root = syntaxTree.GetRoot();
            return root
                .DescendantNodes()
                .Select(node => CreateCSharpSymbolInfo(node, syntaxTree))
                .Where(symbol => symbol is not null)
                .Cast<DocumentSymbolInfo>()
                .OrderBy(symbol => symbol.Line)
                .ThenBy(symbol => symbol.Column)
                .ToList();
        }
        catch
        {
            return Array.Empty<DocumentSymbolInfo>();
        }
    }

    private static DocumentSymbolInfo? CreateCSharpSymbolInfo(SyntaxNode node, SyntaxTree syntaxTree)
    {
        return node switch
        {
            FileScopedNamespaceDeclarationSyntax namespaceNode => CreateSymbolInfo(namespaceNode.Name.ToString(), "Namespace", namespaceNode, syntaxTree, string.Empty),
            NamespaceDeclarationSyntax namespaceNode => CreateSymbolInfo(namespaceNode.Name.ToString(), "Namespace", namespaceNode, syntaxTree, string.Empty),
            ClassDeclarationSyntax classNode => CreateSymbolInfo(classNode.Identifier.ValueText, "Class", classNode, syntaxTree, BuildContainerName(classNode)),
            RecordDeclarationSyntax recordNode => CreateSymbolInfo(recordNode.Identifier.ValueText, "Record", recordNode, syntaxTree, BuildContainerName(recordNode)),
            StructDeclarationSyntax structNode => CreateSymbolInfo(structNode.Identifier.ValueText, "Struct", structNode, syntaxTree, BuildContainerName(structNode)),
            InterfaceDeclarationSyntax interfaceNode => CreateSymbolInfo(interfaceNode.Identifier.ValueText, "Interface", interfaceNode, syntaxTree, BuildContainerName(interfaceNode)),
            EnumDeclarationSyntax enumNode => CreateSymbolInfo(enumNode.Identifier.ValueText, "Enum", enumNode, syntaxTree, BuildContainerName(enumNode)),
            ConstructorDeclarationSyntax ctorNode => CreateSymbolInfo($"{ctorNode.Identifier.ValueText}()", "Constructor", ctorNode, syntaxTree, BuildContainerName(ctorNode)),
            MethodDeclarationSyntax methodNode => CreateSymbolInfo($"{methodNode.Identifier.ValueText}()", "Method", methodNode, syntaxTree, BuildContainerName(methodNode)),
            PropertyDeclarationSyntax propertyNode => CreateSymbolInfo(propertyNode.Identifier.ValueText, "Property", propertyNode, syntaxTree, BuildContainerName(propertyNode)),
            EventDeclarationSyntax eventNode => CreateSymbolInfo(eventNode.Identifier.ValueText, "Event", eventNode, syntaxTree, BuildContainerName(eventNode)),
            FieldDeclarationSyntax fieldNode => fieldNode.Declaration.Variables.Count == 1
                ? CreateSymbolInfo(fieldNode.Declaration.Variables[0].Identifier.ValueText, "Field", fieldNode, syntaxTree, BuildContainerName(fieldNode))
                : null,
            _ => null,
        };
    }

    private static DocumentSymbolInfo CreateSymbolInfo(string title, string kind, SyntaxNode node, SyntaxTree syntaxTree, string description)
    {
        var span = syntaxTree.GetLineSpan(node.Span);
        return new DocumentSymbolInfo(
            Title: title,
            Kind: kind,
            Line: span.StartLinePosition.Line + 1,
            Column: span.StartLinePosition.Character + 1,
            Description: string.IsNullOrWhiteSpace(description) ? kind : $"{kind} in {description}");
    }

    private static string BuildContainerName(SyntaxNode node)
    {
        var containerParts = node.Ancestors()
            .Select(ancestor => ancestor switch
            {
                BaseNamespaceDeclarationSyntax namespaceNode => namespaceNode.Name.ToString(),
                TypeDeclarationSyntax typeNode => typeNode.Identifier.ValueText,
                _ => null,
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Reverse()
            .ToList();

        return string.Join(".", containerParts);
    }

    private static IReadOnlyList<DocumentSymbolInfo> GetRegexSymbols(string text, params RegexSymbolPattern[] patterns)
    {
        if (string.IsNullOrWhiteSpace(text) || patterns.Length == 0)
        {
            return Array.Empty<DocumentSymbolInfo>();
        }

        var symbols = new List<DocumentSymbolInfo>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var compiledPatterns = patterns
            .Select(pattern => (Regex: new Regex(pattern.Pattern, RegexOptions.Multiline), pattern.Kind))
            .ToList();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            foreach (var (regex, kind) in compiledPatterns)
            {
                var match = regex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var title = match.Groups["name"].Value.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var column = Math.Max(1, match.Groups["name"].Index + 1);
                symbols.Add(new DocumentSymbolInfo(title, kind, index + 1, column, kind));
            }
        }

        return symbols
            .OrderBy(symbol => symbol.Line)
            .ThenBy(symbol => symbol.Column)
            .ThenBy(symbol => symbol.Title, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record RegexSymbolPattern(string Pattern, string Kind);
}

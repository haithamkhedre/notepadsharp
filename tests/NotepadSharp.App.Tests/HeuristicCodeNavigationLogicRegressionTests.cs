using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class HeuristicCodeNavigationLogicRegressionTests
{
    [Fact]
    public void TryResolveDefinition_PrefersSameFileJavaScriptSymbol()
    {
        const string activeSource = """
            export function helper() {
                return "local";
            }

            export function run() {
                return helper();
            }
            """;
        const string workspaceSource = """
            export function helper() {
                return "workspace";
            }
            """;

        var caretOffset = activeSource.LastIndexOf("helper", StringComparison.Ordinal) + "helper".Length;
        var location = HeuristicCodeNavigationLogic.TryResolveDefinition(
            new[]
            {
                new HeuristicNavigationSource("/workspace/app.js", activeSource, "JavaScript", IsActiveDocument: true),
                new HeuristicNavigationSource("/workspace/shared.js", workspaceSource, "JavaScript", IsActiveDocument: false),
            },
            caretOffset);

        Assert.NotNull(location);
        Assert.Equal("/workspace/app.js", location!.FilePath);
        Assert.Equal(1, location.Line);
        Assert.Equal(17, location.Column);
        Assert.Equal("Function helper", location.SymbolDisplay);
    }

    [Fact]
    public void TryResolveDefinition_FindsCrossFilePythonDefinition()
    {
        const string activeSource = """
            value = helper()
            """;
        const string helperSource = """
            def helper():
                return 1
            """;

        var caretOffset = activeSource.IndexOf("helper", StringComparison.Ordinal) + "helper".Length;
        var location = HeuristicCodeNavigationLogic.TryResolveDefinition(
            new[]
            {
                new HeuristicNavigationSource("/workspace/runner.py", activeSource, "Python", IsActiveDocument: true),
                new HeuristicNavigationSource("/workspace/helpers.py", helperSource, "Python", IsActiveDocument: false),
            },
            caretOffset);

        Assert.NotNull(location);
        Assert.Equal("/workspace/helpers.py", location!.FilePath);
        Assert.Equal(1, location.Line);
        Assert.Equal(5, location.Column);
        Assert.Equal("Function helper", location.SymbolDisplay);
    }

    [Fact]
    public void FindReferences_ExcludesDefinitionAndFindsCrossFileTypeScriptUsages()
    {
        const string definitionSource = """
            export function slugify(value: string) {
                return value.trim().toLowerCase();
            }
            """;
        const string usageSource = """
            const primary = slugify("Hello");
            const secondary = slugify("World");
            """;

        var caretOffset = definitionSource.IndexOf("slugify", StringComparison.Ordinal) + "slugify".Length;
        var references = HeuristicCodeNavigationLogic.FindReferences(
            new[]
            {
                new HeuristicNavigationSource("/workspace/slug.ts", definitionSource, "TypeScript", IsActiveDocument: true),
                new HeuristicNavigationSource("/workspace/page.ts", usageSource, "TypeScript", IsActiveDocument: false),
            },
            caretOffset);

        Assert.Equal(2, references.Count);
        Assert.Contains(references, item => item.FilePath == "/workspace/page.ts" && item.Line == 1 && item.Column == 17);
        Assert.Contains(references, item => item.FilePath == "/workspace/page.ts" && item.Line == 2 && item.Column == 19);
        Assert.DoesNotContain(references, item => item.FilePath == "/workspace/slug.ts" && item.Line == 1 && item.Column == 17);
    }
}

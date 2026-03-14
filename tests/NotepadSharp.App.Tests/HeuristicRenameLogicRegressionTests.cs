using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class HeuristicRenameLogicRegressionTests
{
    [Fact]
    public void RenameSymbol_RenamesJavaScriptSymbolAcrossFiles()
    {
        const string helperSource = """
            export function helper() {
                return "ready";
            }
            """;
        const string runnerSource = """
            import { helper } from "./helper.js";

            export function run() {
                return helper();
            }
            """;

        var caretOffset = helperSource.IndexOf("helper", StringComparison.Ordinal) + "helper".Length;
        var result = HeuristicRenameLogic.RenameSymbol(
            new[]
            {
                new HeuristicNavigationSource("/workspace/helper.js", helperSource, "JavaScript", IsActiveDocument: true),
                new HeuristicNavigationSource("/workspace/runner.js", runnerSource, "JavaScript", IsActiveDocument: false),
            },
            caretOffset,
            "prepare");

        Assert.NotNull(result);
        Assert.Contains(result!.Changes, item => item.FilePath == "/workspace/helper.js" && item.UpdatedText.Contains("function prepare()", StringComparison.Ordinal));
        Assert.Contains(result.Changes, item => item.FilePath == "/workspace/runner.js" && item.UpdatedText.Contains("import { prepare }", StringComparison.Ordinal));
        Assert.Contains(result.Changes, item => item.FilePath == "/workspace/runner.js" && item.UpdatedText.Contains("return prepare();", StringComparison.Ordinal));
    }

    [Fact]
    public void RenameSymbol_RenamesUntitledPythonSymbolInActiveBuffer()
    {
        const string activeSource = """
            def helper():
                return 1

            value = helper()
            """;

        var caretOffset = activeSource.IndexOf("helper", StringComparison.Ordinal) + "helper".Length;
        var result = HeuristicRenameLogic.RenameSymbol(
            new[]
            {
                new HeuristicNavigationSource(string.Empty, activeSource, "Python", IsActiveDocument: true),
            },
            caretOffset,
            "build_value");

        Assert.NotNull(result);
        Assert.Single(result!.Changes);
        Assert.Equal(string.Empty, result.Changes[0].FilePath);
        Assert.Contains("def build_value()", result.Changes[0].UpdatedText, StringComparison.Ordinal);
        Assert.Contains("value = build_value()", result.Changes[0].UpdatedText, StringComparison.Ordinal);
    }

    [Fact]
    public void RenameSymbol_ReturnsNullForUnsupportedLanguage()
    {
        var result = HeuristicRenameLogic.RenameSymbol(
            new[]
            {
                new HeuristicNavigationSource("/workspace/readme.md", "# helper", "Markdown", IsActiveDocument: true),
            },
            caretOffset: 3,
            newName: "updated");

        Assert.Null(result);
    }
}

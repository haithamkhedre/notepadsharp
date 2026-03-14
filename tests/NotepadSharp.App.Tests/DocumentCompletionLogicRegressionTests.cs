using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class DocumentCompletionLogicRegressionTests
{
    [Fact]
    public void GetSuggestions_ForPython_IncludesClassAndFunctionSymbols()
    {
        const string source = """
            class Worker:
                pass

            def build_worker():
                pass
            """;

        var suggestions = DocumentCompletionLogic.GetSuggestions(
            source,
            "Python",
            new[] { "class", "def", "return" },
            prefix: "b");

        Assert.Contains("build_worker", suggestions);
        Assert.DoesNotContain("Worker", suggestions);
    }

    [Fact]
    public void GetSuggestions_ForTypeScript_IncludesStructuredSymbols()
    {
        const string source = """
            export interface WorkerOptions {
              name: string;
            }

            export const buildWorker = () => ({});
            """;

        var suggestions = DocumentCompletionLogic.GetSuggestions(
            source,
            "TypeScript",
            new[] { "interface", "type", "const" },
            prefix: "W");

        Assert.Contains("WorkerOptions", suggestions);
    }
}

using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class WorkspaceSymbolLogicRegressionTests
{
    [Fact]
    public void GetSymbols_AggregatesSymbolsAcrossFiles()
    {
        var symbols = WorkspaceSymbolLogic.GetSymbols(
            new[]
            {
                new WorkspaceSymbolSource(
                    "/workspace/Runner.cs",
                    "Runner.cs",
                    """
                    class Runner
                    {
                        void Run()
                        {
                        }
                    }
                    """,
                    "C#"),
                new WorkspaceSymbolSource(
                    "/workspace/notes.md",
                    "notes.md",
                    """
                    # Heading
                    """,
                    "Markdown"),
            });

        Assert.Contains(symbols, symbol => symbol.RelativePath == "Runner.cs" && symbol.Title == "Runner");
        Assert.Contains(symbols, symbol => symbol.RelativePath == "Runner.cs" && symbol.Title == "Run()");
        Assert.Contains(symbols, symbol => symbol.RelativePath == "notes.md" && symbol.Title == "Heading");
    }
}

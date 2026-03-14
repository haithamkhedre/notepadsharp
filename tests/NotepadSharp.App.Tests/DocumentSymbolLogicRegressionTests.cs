using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class DocumentSymbolLogicRegressionTests
{
    [Fact]
    public void GetSymbols_ForCSharp_ReturnsTypeAndMemberSymbols()
    {
        const string source = """
            namespace Demo;

            public sealed class Worker
            {
                public string Name { get; set; } = "";

                public void Run()
                {
                }
            }
            """;

        var symbols = DocumentSymbolLogic.GetSymbols(source, "C#");

        Assert.Contains(symbols, symbol => symbol.Kind == "Class" && symbol.Title == "Worker");
        Assert.Contains(symbols, symbol => symbol.Kind == "Property" && symbol.Title == "Name");
        Assert.Contains(symbols, symbol => symbol.Kind == "Method" && symbol.Title == "Run()");
    }

    [Fact]
    public void GetSymbols_ForPython_UsesRegexFallback()
    {
        const string source = """
            class Worker:
                pass

            def outer():
                pass

            async def load_data():
                pass
            """;

        var symbols = DocumentSymbolLogic.GetSymbols(source, "Python");

        Assert.Collection(
            symbols,
            first => Assert.Equal("Worker", first.Title),
            second => Assert.Equal("outer", second.Title),
            third => Assert.Equal("load_data", third.Title));
    }

    [Fact]
    public void GetSymbols_ForTypeScript_ReturnsTypesAndArrowFunctions()
    {
        const string source = """
            export interface WorkerOptions {
              name: string;
            }

            export type WorkerState = "idle" | "running";

            export enum WorkerMode {
              Idle,
            }

            export const buildWorker = async () => {
              return {};
            };
            """;

        var symbols = DocumentSymbolLogic.GetSymbols(source, "TypeScript");

        Assert.Contains(symbols, symbol => symbol.Kind == "Interface" && symbol.Title == "WorkerOptions");
        Assert.Contains(symbols, symbol => symbol.Kind == "Type" && symbol.Title == "WorkerState");
        Assert.Contains(symbols, symbol => symbol.Kind == "Enum" && symbol.Title == "WorkerMode");
        Assert.Contains(symbols, symbol => symbol.Kind == "Function" && symbol.Title == "buildWorker");
    }
}

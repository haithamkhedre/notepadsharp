using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class CSharpDefinitionLogicRegressionTests
{
    [Fact]
    public void TryResolveDefinition_FindsWorkspaceDefinitionInAnotherFile()
    {
        const string activeSource = """
            namespace Demo;

            public sealed class Runner
            {
                public void Run()
                {
                    var worker = new Worker();
                    worker.Execute();
                }
            }
            """;
        const string workerSource = """
            namespace Demo;

            public sealed class Worker
            {
                public void Execute()
                {
                }
            }
            """;

        var caretOffset = activeSource.IndexOf("Execute", StringComparison.Ordinal) + "Execute".Length;
        var location = CSharpDefinitionLogic.TryResolveDefinition(
            new[]
            {
                new CSharpDefinitionSource("/workspace/Runner.cs", activeSource, IsActiveDocument: true),
                new CSharpDefinitionSource("/workspace/Worker.cs", workerSource, IsActiveDocument: false),
            },
            caretOffset);

        Assert.NotNull(location);
        Assert.Equal("/workspace/Worker.cs", location!.FilePath);
        Assert.Equal(5, location.Line);
        Assert.Equal(17, location.Column);
        Assert.Contains("Execute", location.SymbolDisplay);
    }

    [Fact]
    public void TryResolveDefinition_PreservesUntitledActiveBufferPath()
    {
        const string activeSource = """
            class Sample
            {
                void Run()
                {
                    Helper();
                }

                void Helper()
                {
                }
            }
            """;

        var caretOffset = activeSource.IndexOf("Helper();", StringComparison.Ordinal) + "Helper".Length;
        var location = CSharpDefinitionLogic.TryResolveDefinition(
            new[]
            {
                new CSharpDefinitionSource(string.Empty, activeSource, IsActiveDocument: true),
            },
            caretOffset);

        Assert.NotNull(location);
        Assert.Equal(string.Empty, location!.FilePath);
        Assert.Equal(8, location.Line);
        Assert.Equal(10, location.Column);
    }
}

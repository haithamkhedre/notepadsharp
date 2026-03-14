using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class CSharpReferenceLogicRegressionTests
{
    [Fact]
    public async Task FindReferencesAsync_FindsCrossFileReferences()
    {
        const string workerSource = """
            namespace Demo;

            public sealed class Worker
            {
                public void Execute()
                {
                }
            }
            """;
        const string runnerSource = """
            namespace Demo;

            public sealed class Runner
            {
                public void Run(Worker worker)
                {
                    worker.Execute();
                }
            }
            """;
        const string secondRunnerSource = """
            namespace Demo;

            public static class Runner2
            {
                public static void Run(Worker worker)
                {
                    worker.Execute();
                }
            }
            """;

        var caretOffset = workerSource.IndexOf("Execute", StringComparison.Ordinal) + "Execute".Length;
        var references = await CSharpReferenceLogic.FindReferencesAsync(
            new[]
            {
                new CSharpDefinitionSource("/workspace/Worker.cs", workerSource, IsActiveDocument: true),
                new CSharpDefinitionSource("/workspace/Runner.cs", runnerSource, IsActiveDocument: false),
                new CSharpDefinitionSource("/workspace/Runner2.cs", secondRunnerSource, IsActiveDocument: false),
            },
            caretOffset);

        Assert.Equal(2, references.Count);
        Assert.Contains(references, item => item.FilePath == "/workspace/Runner.cs" && item.Line == 7);
        Assert.Contains(references, item => item.FilePath == "/workspace/Runner2.cs" && item.Line == 7);
    }
}

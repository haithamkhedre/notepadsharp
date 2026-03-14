using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class CSharpRenameLogicRegressionTests
{
    [Fact]
    public async Task RenameSymbolAsync_RenamesAcrossOpenFiles()
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

        var caretOffset = workerSource.IndexOf("Execute", StringComparison.Ordinal) + "Execute".Length;
        var result = await CSharpRenameLogic.RenameSymbolAsync(
            new[]
            {
                new CSharpDefinitionSource("/workspace/Worker.cs", workerSource, IsActiveDocument: true),
                new CSharpDefinitionSource("/workspace/Runner.cs", runnerSource, IsActiveDocument: false),
            },
            caretOffset,
            "RunWork");

        Assert.NotNull(result);
        Assert.Contains(result!.Changes, item => item.FilePath == "/workspace/Worker.cs" && item.UpdatedText.Contains("RunWork", StringComparison.Ordinal));
        Assert.Contains(result.Changes, item => item.FilePath == "/workspace/Runner.cs" && item.UpdatedText.Contains("worker.RunWork();", StringComparison.Ordinal));
    }
}

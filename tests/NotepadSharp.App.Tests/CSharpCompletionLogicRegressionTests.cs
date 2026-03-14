using System;
using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class CSharpCompletionLogicRegressionTests
{
    [Fact]
    public async Task GetSuggestionsAsync_ReturnsConsoleMemberCompletions()
    {
        const string source = """
            using System;

            class Sample
            {
                void Test()
                {
                    Console.Wri
                }
            }
            """;
        var caretOffset = source.IndexOf("Wri", StringComparison.Ordinal) + "Wri".Length;

        var suggestions = await CSharpCompletionLogic.GetSuggestionsAsync(source, caretOffset);

        Assert.Contains("WriteLine", suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_WithSourceSet_IncludesTypesFromOtherOpenFiles()
    {
        const string activeSource = """
            namespace Demo;

            public sealed class Runner
            {
                public Wor
            }
            """;
        const string workerSource = """
            namespace Demo;

            public sealed class Worker
            {
            }
            """;
        var caretOffset = activeSource.IndexOf("Wor", StringComparison.Ordinal) + "Wor".Length;

        var suggestions = await CSharpCompletionLogic.GetSuggestionsAsync(
            new[]
            {
                new CSharpDefinitionSource("/workspace/Runner.cs", activeSource, IsActiveDocument: true),
                new CSharpDefinitionSource("/workspace/Worker.cs", workerSource, IsActiveDocument: false),
            },
            caretOffset);

        Assert.Contains("Worker", suggestions);
    }
}

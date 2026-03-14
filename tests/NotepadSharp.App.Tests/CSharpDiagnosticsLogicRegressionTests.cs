using System;
using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class CSharpDiagnosticsLogicRegressionTests
{
    [Fact]
    public void GetDiagnostics_IncludesSemanticErrors()
    {
        const string source = """
            using System;

            class Sample
            {
                void Test()
                {
                    Console.WriteLine(missingValue);
                }
            }
            """;

        var diagnostics = CSharpDiagnosticsLogic.GetDiagnostics(source);

        var diagnostic = Assert.Single(diagnostics, item => item.Message.Contains("missingValue", StringComparison.Ordinal));
        Assert.Equal("Error", diagnostic.Severity);
    }

    [Fact]
    public void GetDiagnostics_WithSourceSet_DoesNotReportKnownTypeFromAnotherFile()
    {
        const string activeSource = """
            namespace Demo;

            public sealed class Runner
            {
                public Worker Create()
                {
                    return new Worker();
                }
            }
            """;
        const string workerSource = """
            namespace Demo;

            public sealed class Worker
            {
            }
            """;

        var diagnostics = CSharpDiagnosticsLogic.GetDiagnostics(
            new[]
            {
                new CSharpDefinitionSource("/workspace/Runner.cs", activeSource, IsActiveDocument: true),
                new CSharpDefinitionSource("/workspace/Worker.cs", workerSource, IsActiveDocument: false),
            });

        Assert.DoesNotContain(diagnostics, item => item.Message.Contains("Worker", StringComparison.Ordinal));
    }
}

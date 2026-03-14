using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class AiProviderResponseLogicRegressionTests
{
    [Fact]
    public void ExtractPrimaryText_UsesFencedCodeBodyWhenPresent()
    {
        const string response = """
            ```csharp
            public sealed class Demo
            {
            }
            ```
            """;

        var text = AiProviderResponseLogic.ExtractPrimaryText(response);

        Assert.Equal(
            """
            public sealed class Demo
            {
            }
            """,
            text);
    }

    [Fact]
    public void ExtractPrimaryText_FallsBackToTrimmedPlainText()
    {
        var text = AiProviderResponseLogic.ExtractPrimaryText("  feat(editor): improve search summaries  ");

        Assert.Equal("feat(editor): improve search summaries", text);
    }
}

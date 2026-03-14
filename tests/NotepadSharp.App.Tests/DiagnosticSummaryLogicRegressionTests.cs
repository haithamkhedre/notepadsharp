using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class DiagnosticSummaryLogicRegressionTests
{
    [Fact]
    public void FormatStatusText_IncludesErrorAndWarningBreakdown()
    {
        var status = DiagnosticSummaryLogic.FormatStatusText(new[] { "Error", "Warning", "Warning" });

        Assert.Equal("Diagnostics: 3 (1E/2W)", status);
    }

    [Fact]
    public void FormatSummaryText_UsesReadablePluralization()
    {
        var summary = DiagnosticSummaryLogic.FormatSummaryText(new[] { "Error", "Warning", "Warning" });

        Assert.Equal("1 error, 2 warnings", summary);
    }
}

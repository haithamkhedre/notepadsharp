using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class GitDiffMarkerPresentationLogicRegressionTests
{
    [Fact]
    public void GetVisibleMarkers_FiltersUnchangedLinesAndKeepsLineNumbers()
    {
        var markers = GitDiffMarkerPresentationLogic.GetVisibleMarkers(new[] { ' ', '+', ' ', '~', '-', ' ' });

        Assert.Equal(
            new[]
            {
                new GitDiffMarkerPresentation(2, '+', '+'),
                new GitDiffMarkerPresentation(4, '~', '~'),
                new GitDiffMarkerPresentation(5, '-', '-'),
            },
            markers);
    }

    [Fact]
    public void Summarize_CountsAddedModifiedAndDeletedLines()
    {
        var summary = GitDiffMarkerPresentationLogic.Summarize(new[] { '+', '+', '~', '-', ' ', '~' });

        Assert.Equal(new GitDiffMarkerSummary(2, 2, 1), summary);
        Assert.True(summary.HasChanges);
    }

    [Fact]
    public void BuildTooltip_FormatsHumanReadableSummary()
    {
        var tooltip = GitDiffMarkerPresentationLogic.BuildTooltip(new[] { '+', '~', '-', '-', ' ' });

        Assert.Equal("Git line changes: 1 added | 1 modified | 2 deleted.", tooltip);
    }
}

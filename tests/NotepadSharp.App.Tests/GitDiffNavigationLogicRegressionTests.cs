using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class GitDiffNavigationLogicRegressionTests
{
    [Fact]
    public void GetChangeAnchorLines_ReturnsStartOfEachChangeBlock()
    {
        var anchors = GitDiffNavigationLogic.GetChangeAnchorLines(new[] { ' ', '+', '+', ' ', '~', '~', ' ', '-', ' ' });

        Assert.Equal(new[] { 2, 5, 8 }, anchors);
    }

    [Fact]
    public void GetTargetLine_WrapsWhenNavigatingPastEnd()
    {
        var target = GitDiffNavigationLogic.GetTargetLine(new[] { ' ', '+', '+', ' ', '~', '~', ' ', '-', ' ' }, currentLine: 8, forward: true);

        Assert.Equal(2, target);
    }
}

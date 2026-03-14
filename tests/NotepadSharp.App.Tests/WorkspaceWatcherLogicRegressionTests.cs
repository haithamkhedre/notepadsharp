using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class WorkspaceWatcherLogicRegressionTests
{
    [Theory]
    [InlineData("/repo/.git/index", true)]
    [InlineData("/repo/src/bin/Debug/app.dll", true)]
    [InlineData("/repo/src/obj/project.assets.json", true)]
    [InlineData("/repo/.DS_Store", true)]
    [InlineData("/repo/src/Program.cs", false)]
    [InlineData("/repo/README.md", false)]
    public void ShouldIgnorePath_FiltersNoiseUnderWorkspace(string changedPath, bool expected)
    {
        var actual = WorkspaceWatcherLogic.ShouldIgnorePath("/repo", changedPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShouldIgnorePath_IgnoresPathsOutsideWorkspace()
    {
        Assert.True(WorkspaceWatcherLogic.ShouldIgnorePath("/repo", "/other/place/file.txt"));
    }
}

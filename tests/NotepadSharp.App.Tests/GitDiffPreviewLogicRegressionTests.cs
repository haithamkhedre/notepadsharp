using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class GitDiffPreviewLogicRegressionTests
{
    [Fact]
    public void BuildDiffArguments_ForUnstagedRoot_OmitsCachedAndPath()
    {
        var arguments = GitDiffPreviewLogic.BuildDiffArguments(GitChangeSection.Unstaged, string.Empty);

        Assert.Equal(new[] { "diff", "--no-color", "--unified=3" }, arguments);
    }

    [Fact]
    public void BuildDiffArguments_ForStagedFile_AddsCachedAndPathspec()
    {
        var arguments = GitDiffPreviewLogic.BuildDiffArguments(GitChangeSection.Staged, "src/MainWindow.cs");

        Assert.Equal(
            new[] { "diff", "--no-color", "--unified=3", "--cached", "--", "src/MainWindow.cs" },
            arguments);
    }

    [Fact]
    public void TrimPreview_AppendsTruncationMarker()
    {
        var preview = GitDiffPreviewLogic.TrimPreview(new string('a', 12), maxChars: 5);

        Assert.Equal("aaaaa\n\n[preview truncated]", preview);
    }

    [Fact]
    public void BuildUntrackedPreview_AddsHeaderBeforeContent()
    {
        var preview = GitDiffPreviewLogic.BuildUntrackedPreview("notes/todo.txt", "line 1\nline 2", maxChars: 200);

        Assert.StartsWith("Untracked file: notes/todo.txt", preview);
        Assert.Contains("line 1", preview);
    }
}

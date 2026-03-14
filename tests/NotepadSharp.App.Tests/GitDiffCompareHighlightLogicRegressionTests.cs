using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class GitDiffCompareHighlightLogicRegressionTests
{
    [Fact]
    public void BuildLineMapFromPatch_TracksPrimaryAndSecondaryHunksSeparately()
    {
        var patch = """
            diff --git a/sample.txt b/sample.txt
            @@ -3,2 +3,3 @@
            -before one
            -before two
            +after one
            +after two
            +after three
            """;

        var lineMap = GitDiffCompareHighlightLogic.BuildLineMapFromPatch(patch, primaryLineCount: 8, secondaryLineCount: 9);

        Assert.Equal(new[] { 3, 4 }, lineMap.PrimaryLines);
        Assert.Equal(new[] { 3, 4, 5 }, lineMap.SecondaryLines);
        Assert.True(lineMap.HasChanges);
    }

    [Fact]
    public void BuildLineMap_ForUntrackedFile_MarksEntireSecondaryDocument()
    {
        var lineMap = GitDiffCompareHighlightLogic.BuildLineMap(
            GitChangeSection.Unstaged,
            "?",
            string.Empty,
            primaryLineCount: 1,
            secondaryLineCount: 4);

        Assert.Empty(lineMap.PrimaryLines);
        Assert.Equal(new[] { 1, 2, 3, 4 }, lineMap.SecondaryLines);
    }

    [Fact]
    public void BuildLineMap_ForDeletedFile_MarksEntirePrimaryDocument()
    {
        var lineMap = GitDiffCompareHighlightLogic.BuildLineMap(
            GitChangeSection.Staged,
            "D",
            string.Empty,
            primaryLineCount: 3,
            secondaryLineCount: 1);

        Assert.Equal(new[] { 1, 2, 3 }, lineMap.PrimaryLines);
        Assert.Empty(lineMap.SecondaryLines);
    }

    [Fact]
    public void BuildMarkers_PaintsRequestedDiffKindAtMappedLines()
    {
        var markers = GitDiffCompareHighlightLogic.BuildMarkers(5, new[] { 2, 5 }, '-');

        Assert.Equal(new[] { ' ', '-', ' ', ' ', '-' }, markers);
    }

    [Fact]
    public void BuildPatchArguments_ForStagedDiff_IncludesCachedFlag()
    {
        var arguments = GitDiffCompareHighlightLogic.BuildPatchArguments(GitChangeSection.Staged, "src/Main.cs");

        Assert.Equal(new[] { "diff", "--no-color", "--unified=0", "--cached", "--", "src/Main.cs" }, arguments);
    }
}

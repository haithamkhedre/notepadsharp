using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class GitDiffCompareLogicRegressionTests
{
    [Fact]
    public void BuildPlan_ForUntrackedWorkingTree_UsesEmptyBaseAndWorkingTree()
    {
        var plan = GitDiffCompareLogic.BuildPlan(GitChangeSection.Unstaged, "?");

        Assert.Equal(GitDiffCompareSourceKind.Empty, plan.Primary.Kind);
        Assert.Equal("Base: Empty", plan.Primary.Title);
        Assert.Equal(GitDiffCompareSourceKind.WorkingTree, plan.Secondary.Kind);
        Assert.Equal("Working Tree", plan.Secondary.Title);
    }

    [Fact]
    public void BuildPlan_ForStagedFile_UsesHeadAndIndex()
    {
        var plan = GitDiffCompareLogic.BuildPlan(GitChangeSection.Staged, "M");

        Assert.Equal(new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "HEAD", "HEAD"), plan.Primary);
        Assert.Equal(new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Staged Snapshot", ":"), plan.Secondary);
    }

    [Fact]
    public void BuildPlan_ForConflict_UsesOursAndTheirsStages()
    {
        var plan = GitDiffCompareLogic.BuildPlan(GitChangeSection.Conflicts, "UU");

        Assert.Equal(new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Ours", ":2"), plan.Primary);
        Assert.Equal(new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Theirs", ":3"), plan.Secondary);
    }

    [Fact]
    public void BuildObjectExpression_FormatsPathRevisionPairs()
    {
        var expression = GitDiffCompareLogic.BuildObjectExpression(":2", @"src\MainWindow.cs");

        Assert.Equal(":2:src/MainWindow.cs", expression);
    }

    [Fact]
    public void BuildObjectExpression_ForIndexSnapshot_UsesSingleColonPrefix()
    {
        var expression = GitDiffCompareLogic.BuildObjectExpression(":", "src/MainWindow.cs");

        Assert.Equal(":src/MainWindow.cs", expression);
    }
}

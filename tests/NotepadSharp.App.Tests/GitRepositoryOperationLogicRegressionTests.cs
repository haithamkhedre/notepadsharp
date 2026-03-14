using System;
using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class GitRepositoryOperationLogicRegressionTests
{
    [Fact]
    public void DetectState_PrefersRebaseWhenRebaseDirectoryExists()
    {
        var state = GitRepositoryOperationLogic.DetectState(
            "/repo/.git",
            fileExists: _ => false,
            directoryExists: path => path.EndsWith("rebase-merge", StringComparison.Ordinal));

        Assert.NotNull(state);
        Assert.Equal(GitRepositoryOperationKind.Rebase, state!.Kind);
        Assert.Equal("Continue Rebase", state.ContinueButtonLabel);
        Assert.Equal("Abort Rebase", state.AbortButtonLabel);
    }

    [Fact]
    public void DetectState_RecognizesMergeHead()
    {
        var state = GitRepositoryOperationLogic.DetectState(
            "/repo/.git",
            fileExists: path => path.EndsWith("MERGE_HEAD", StringComparison.Ordinal),
            directoryExists: _ => false);

        Assert.NotNull(state);
        Assert.Equal(GitRepositoryOperationKind.Merge, state!.Kind);
        Assert.Equal(new[] { "merge", "--abort" }, state.AbortArguments);
    }

    [Fact]
    public void DetectState_ReturnsNullWhenNoOperationMarkersExist()
    {
        var state = GitRepositoryOperationLogic.DetectState(
            "/repo/.git",
            fileExists: _ => false,
            directoryExists: _ => false);

        Assert.Null(state);
    }

    [Fact]
    public void BuildState_UsesBisectResetArguments()
    {
        var state = GitRepositoryOperationLogic.BuildState(GitRepositoryOperationKind.Bisect);

        Assert.Null(state.ContinueButtonLabel);
        Assert.Equal("Reset Bisect", state.AbortButtonLabel);
        Assert.Equal(new[] { "bisect", "reset" }, state.AbortArguments);
    }
}

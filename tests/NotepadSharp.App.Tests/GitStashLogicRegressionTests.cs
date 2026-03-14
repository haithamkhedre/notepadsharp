using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class GitStashLogicRegressionTests
{
    [Fact]
    public void ParseEntries_ExtractsReferenceAndSummary()
    {
        var entries = GitStashLogic.ParseEntries(
            "stash@{0}: On main: WIP parser\nstash@{1}: On feature/git: save before rebase\n");

        Assert.Collection(
            entries,
            first =>
            {
                Assert.Equal("stash@{0}", first.Reference);
                Assert.Equal("On main: WIP parser", first.Summary);
            },
            second =>
            {
                Assert.Equal("stash@{1}", second.Reference);
                Assert.Equal("On feature/git: save before rebase", second.Summary);
            });
    }

    [Fact]
    public void BuildPushArguments_IncludesMessageWhenProvided()
    {
        var arguments = GitStashLogic.BuildPushArguments("save before switch");

        Assert.Equal(
            new[] { "stash", "push", "--include-untracked", "-m", "save before switch" },
            arguments);
    }

    [Fact]
    public void BuildApplyArguments_TargetsSpecificReference()
    {
        var arguments = GitStashLogic.BuildApplyArguments("stash@{2}");

        Assert.Equal(new[] { "stash", "apply", "stash@{2}" }, arguments);
    }

    [Fact]
    public void BuildPopArguments_TargetsSpecificReference()
    {
        var arguments = GitStashLogic.BuildPopArguments("stash@{1}");

        Assert.Equal(new[] { "stash", "pop", "stash@{1}" }, arguments);
    }

    [Fact]
    public void BuildDropArguments_TargetsSpecificReference()
    {
        var arguments = GitStashLogic.BuildDropArguments("stash@{1}");

        Assert.Equal(new[] { "stash", "drop", "stash@{1}" }, arguments);
    }

    [Fact]
    public void TryParseSelection_ExtractsStashReference()
    {
        var parsed = GitStashLogic.TryParseSelection("stash:stash@{3}", out var stashReference);

        Assert.True(parsed);
        Assert.Equal("stash@{3}", stashReference);
    }

    [Fact]
    public void IndicatesConflictOrPartialApply_DetectsConflictMarkers()
    {
        Assert.True(GitStashLogic.IndicatesConflictOrPartialApply("Auto-merging Program.cs", "CONFLICT (content): Merge conflict in Program.cs"));
        Assert.False(GitStashLogic.IndicatesConflictOrPartialApply("Applied cleanly", string.Empty));
    }
}

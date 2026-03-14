using System.Linq;
using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class GitBranchLogicRegressionTests
{
    [Fact]
    public void ParseBranchNames_TrimsDeduplicatesAndSorts()
    {
        var branches = GitBranchLogic.ParseBranchNames(" feature/zeta \nmain\n\nMAIN\nfeature/alpha\r\n");

        Assert.Equal(new[] { "feature/alpha", "feature/zeta", "main" }, branches);
    }

    [Fact]
    public void BuildBranchChoices_PutsCurrentBranchFirst()
    {
        var choices = GitBranchLogic.BuildBranchChoices(
            new[] { "release", "main", "feature/search" },
            "main");

        Assert.Equal("main", choices[0].Name);
        Assert.True(choices[0].IsCurrent);
        Assert.False(choices[0].IsRemote);
        Assert.Contains(choices, c => c.Name == "feature/search" && !c.IsCurrent);
    }

    [Fact]
    public void BuildSwitchChoices_AppendsRemoteBranchesWithoutLocalTrackingBranch()
    {
        var choices = GitBranchLogic.BuildSwitchChoices(
            new[] { "main", "release" },
            new[] { "origin/main", "origin/feature/search", "origin/HEAD" },
            "main");

        Assert.Equal("main", choices[0].Name);
        var remoteChoice = Assert.Single(choices, choice => choice.IsRemote);
        Assert.Equal("origin/feature/search", remoteChoice.Name);
    }

    [Fact]
    public void GetRemoteTrackingBranchName_StripsRemotePrefix()
    {
        Assert.Equal("feature/search", GitBranchLogic.GetRemoteTrackingBranchName("origin/feature/search"));
    }

    [Fact]
    public void FormatBranchLabel_UsesDetachedCommitWhenNeeded()
    {
        Assert.Equal("Branch: HEAD (abc1234)", GitBranchLogic.FormatBranchLabel(null, "abc1234"));
    }

    [Fact]
    public void TryParseAheadBehindCounts_MapsRevListOutput()
    {
        var parsed = GitBranchLogic.TryParseAheadBehindCounts("3\t5", out var ahead, out var behind);

        Assert.True(parsed);
        Assert.Equal(5, ahead);
        Assert.Equal(3, behind);
    }

    [Fact]
    public void FormatBranchLabel_AppendsTrackingCounts()
    {
        Assert.Equal("Branch: main \u21912 \u21931", GitBranchLogic.FormatBranchLabel("main", ahead: 2, behind: 1));
    }

    [Fact]
    public void NormalizeBranchName_ReturnsNullForWhitespace()
    {
        Assert.Null(GitBranchLogic.NormalizeBranchName("   "));
    }

    [Fact]
    public void BuildDeleteChoices_ExcludesCurrentBranch()
    {
        var choices = GitBranchLogic.BuildDeleteChoices(
            new[] { "main", "feature/search", "release" },
            "main");

        Assert.DoesNotContain(choices, choice => choice.Name == "main");
        Assert.Equal(new[] { "feature/search", "release" }, choices.Select(choice => choice.Name));
    }

    [Fact]
    public void BuildRenameArguments_TargetCurrentBranchRename()
    {
        var arguments = GitBranchLogic.BuildRenameArguments("feature/renamed");

        Assert.Equal(new[] { "branch", "-m", "feature/renamed" }, arguments);
    }

    [Fact]
    public void BuildDeleteArguments_UsesForceFlagWhenRequested()
    {
        var arguments = GitBranchLogic.BuildDeleteArguments("feature/search", force: true);

        Assert.Equal(new[] { "branch", "-D", "feature/search" }, arguments);
    }

    [Fact]
    public void ShouldOfferForceDelete_DetectsUnmergedBranchMessage()
    {
        Assert.True(GitBranchLogic.ShouldOfferForceDelete("error: The branch 'feature/search' is not fully merged."));
        Assert.False(GitBranchLogic.ShouldOfferForceDelete("error: branch not found"));
    }
}

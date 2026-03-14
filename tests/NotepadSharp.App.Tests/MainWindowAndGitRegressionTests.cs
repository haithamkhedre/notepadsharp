using System;
using System.IO;
using System.Linq;
using NotepadSharp.App;
using NotepadSharp.App.Services;
using NotepadSharp.App.ViewModels;
using NotepadSharp.Core;

namespace NotepadSharp.App.Tests;

public class MainWindowViewModelRegressionTests
{
    [Fact]
    public void NewDocument_AddsAndSelectsNewTab()
    {
        var vm = new MainWindowViewModel();
        var before = vm.Documents.Count;

        vm.NewDocument();

        Assert.Equal(before + 1, vm.Documents.Count);
        Assert.Same(vm.Documents.Last(), vm.SelectedDocument);
        Assert.Equal("Untitled", vm.SelectedDocument?.DisplayName);
    }

    [Fact]
    public void AddRecentFile_DeduplicatesAndCapsAtTen()
    {
        var vm = new MainWindowViewModel();
        for (var i = 1; i <= 12; i++)
        {
            vm.AddRecentFile($"/tmp/notepadsharp-{i}.txt");
        }

        Assert.Equal(10, vm.RecentFiles.Count);
        Assert.Equal(Path.GetFullPath("/tmp/notepadsharp-12.txt"), vm.RecentFiles[0]);

        vm.AddRecentFile("/tmp/notepadsharp-10.txt");
        Assert.Equal(10, vm.RecentFiles.Count);
        Assert.Equal(Path.GetFullPath("/tmp/notepadsharp-10.txt"), vm.RecentFiles[0]);
    }

    [Fact]
    public void GetSessionFilePaths_NormalizesAndDeduplicatesCaseInsensitive()
    {
        var vm = new MainWindowViewModel();
        vm.Documents.Clear();

        var first = TextDocument.CreateNew();
        first.FilePath = "./src/../src/Sample.cs";

        var second = TextDocument.CreateNew();
        second.FilePath = "./src/SAMPLE.cs";

        var third = TextDocument.CreateNew();
        third.FilePath = "./README.md";

        vm.Documents.Add(first);
        vm.Documents.Add(second);
        vm.Documents.Add(third);

        var sessionFiles = vm.GetSessionFilePaths();

        Assert.Equal(2, sessionFiles.Length);
        Assert.Contains(Path.GetFullPath("./src/Sample.cs"), sessionFiles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath("./README.md"), sessionFiles, StringComparer.OrdinalIgnoreCase);
    }
}

public class GitStatusLogicRegressionTests
{
    [Theory]
    [InlineData("M ", "M")]
    [InlineData("A ", "A")]
    [InlineData("R ", "R")]
    [InlineData("??", null)]
    [InlineData("  ", null)]
    public void GetStagedCode_ParsesPorcelainXy(string xy, string? expected)
        => Assert.Equal(expected, GitStatusLogic.GetStagedCode(xy));

    [Theory]
    [InlineData(" M", "M")]
    [InlineData("AM", "M")]
    [InlineData("??", "?")]
    [InlineData("A ", null)]
    [InlineData("!!", null)]
    public void GetUnstagedCode_ParsesPorcelainXy(string xy, string? expected)
        => Assert.Equal(expected, GitStatusLogic.GetUnstagedCode(xy));

    [Fact]
    public void BuildGitChangeTree_GroupsDirectoryAndAddsSectionRoots()
    {
        var repoRoot = Path.GetFullPath("/tmp/notepadsharp-repo");
        var staged = new[]
        {
            new GitChangeEntryModel
            {
                Code = "M",
                RelativePath = "src/MainWindow.cs",
                FullPath = Path.Combine(repoRoot, "src", "MainWindow.cs"),
            },
            new GitChangeEntryModel
            {
                Code = "A",
                RelativePath = "README.md",
                FullPath = Path.Combine(repoRoot, "README.md"),
            },
        };

        var unstaged = new[]
        {
            new GitChangeEntryModel
            {
                Code = "D",
                RelativePath = "src/OldFile.cs",
                FullPath = Path.Combine(repoRoot, "src", "OldFile.cs"),
            },
        };

        var roots = GitStatusLogic.BuildGitChangeTree(
            repoRoot,
            Array.Empty<GitChangeEntryModel>(),
            staged,
            unstaged);
        
        Assert.Equal(2, roots.Count);
        Assert.Equal("Staged (2)", roots[0].Name);
        Assert.Equal("Changes (1)", roots[1].Name);
        Assert.Equal(GitChangeSection.Staged, roots[0].Section);
        Assert.Equal(GitChangeSection.Unstaged, roots[1].Section);

        var stagedDirectory = roots[0].Children.FirstOrDefault(n => n.IsDirectory && n.Name == "src");
        Assert.NotNull(stagedDirectory);
        Assert.Equal(GitChangeSection.Staged, stagedDirectory!.Section);
        Assert.Contains(stagedDirectory!.Children, n => !n.IsDirectory && n.Name == "MainWindow.cs" && n.Status == "M");
        Assert.Contains(roots[0].Children, n => !n.IsDirectory && n.Name == "README.md" && n.Status == "A");
        Assert.Contains(stagedDirectory.Children, n => !n.IsDirectory && n.RelativePath == "src/MainWindow.cs" && n.Section == GitChangeSection.Staged);

        var unstagedDirectory = roots[1].Children.FirstOrDefault(n => n.IsDirectory && n.Name == "src");
        Assert.NotNull(unstagedDirectory);
        Assert.Equal(GitChangeSection.Unstaged, unstagedDirectory!.Section);
        Assert.Contains(unstagedDirectory!.Children, n => !n.IsDirectory && n.Name == "OldFile.cs" && n.Status == "D");
        Assert.Contains(unstagedDirectory.Children, n => !n.IsDirectory && n.RelativePath == "src/OldFile.cs" && n.Section == GitChangeSection.Unstaged);
    }

    [Fact]
    public void BuildGitChangeTree_PutsConflictsInDedicatedSection()
    {
        var repoRoot = Path.GetFullPath("/tmp/notepadsharp-repo-conflicts");
        var conflicts = new[]
        {
            new GitChangeEntryModel
            {
                Code = "UU",
                RelativePath = "src/MergeTarget.cs",
                FullPath = Path.Combine(repoRoot, "src", "MergeTarget.cs"),
            },
        };

        var roots = GitStatusLogic.BuildGitChangeTree(
            repoRoot,
            conflicts,
            Array.Empty<GitChangeEntryModel>(),
            Array.Empty<GitChangeEntryModel>());

        Assert.Equal(3, roots.Count);
        Assert.Equal("Conflicts (1)", roots[0].Name);
        Assert.Equal(GitChangeSection.Conflicts, roots[0].Section);
        var conflictDirectory = roots[0].Children.Single();
        Assert.Equal("src", conflictDirectory.Name);
        Assert.Equal(GitChangeSection.Conflicts, conflictDirectory.Section);
        Assert.Contains(conflictDirectory.Children, n => !n.IsDirectory && n.Status == "UU" && n.CanStage);
    }

    [Fact]
    public void BuildGitChangeTree_AddsPlaceholderWhenSectionIsEmpty()
    {
        var repoRoot = Path.GetFullPath("/tmp/notepadsharp-repo-empty");

        var roots = GitStatusLogic.BuildGitChangeTree(
            repoRoot,
            Array.Empty<GitChangeEntryModel>(),
            Array.Empty<GitChangeEntryModel>(),
            Array.Empty<GitChangeEntryModel>());

        Assert.Equal(2, roots.Count);
        Assert.Equal("(no staged files)", roots[0].Children.Single().Name);
        Assert.Equal("(working tree clean)", roots[1].Children.Single().Name);
        Assert.True(roots[0].Children.Single().IsPlaceholder);
        Assert.True(roots[1].Children.Single().IsPlaceholder);
    }

    [Theory]
    [InlineData("M", GitChangeSection.Unstaged, GitDiscardActionKind.RestoreWorkingTree)]
    [InlineData("D", GitChangeSection.Unstaged, GitDiscardActionKind.RestoreWorkingTree)]
    [InlineData("?", GitChangeSection.Unstaged, GitDiscardActionKind.DeleteUntracked)]
    [InlineData("M", GitChangeSection.Staged, GitDiscardActionKind.None)]
    [InlineData("UU", GitChangeSection.Conflicts, GitDiscardActionKind.None)]
    [InlineData(null, GitChangeSection.Unstaged, GitDiscardActionKind.None)]
    public void GetDiscardAction_ClassifiesUnstagedFileOperations(string? status, GitChangeSection section, GitDiscardActionKind expected)
    {
        var actual = GitStatusLogic.GetDiscardAction(status, section);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("UU", true)]
    [InlineData("AU", true)]
    [InlineData(" M", false)]
    [InlineData("??", false)]
    public void IsConflictXy_RecognizesUnmergedStatuses(string xy, bool expected)
        => Assert.Equal(expected, GitStatusLogic.IsConflictXy(xy));

    [Fact]
    public void GitChangeTreeNode_ConflictFileExposesResolutionActions()
    {
        var node = new GitChangeTreeNode
        {
            Name = "MergeTarget.cs",
            FullPath = "/tmp/notepadsharp-repo/src/MergeTarget.cs",
            RelativePath = "src/MergeTarget.cs",
            IsDirectory = false,
            Section = GitChangeSection.Conflicts,
            Status = "UU",
        };

        Assert.True(node.CanAcceptConflictSide);
        Assert.True(node.CanStage);
        Assert.Equal("Stage Resolved", node.StageMenuHeader);
        Assert.False(node.CanDiscard);
    }
}

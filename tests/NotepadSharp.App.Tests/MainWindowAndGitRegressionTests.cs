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

        var roots = GitStatusLogic.BuildGitChangeTree(repoRoot, staged, unstaged);

        Assert.Equal(2, roots.Count);
        Assert.Equal("Staged (2)", roots[0].Name);
        Assert.Equal("Changes (1)", roots[1].Name);

        var stagedDirectory = roots[0].Children.FirstOrDefault(n => n.IsDirectory && n.Name == "src");
        Assert.NotNull(stagedDirectory);
        Assert.Contains(stagedDirectory!.Children, n => !n.IsDirectory && n.Name == "MainWindow.cs" && n.Status == "M");
        Assert.Contains(roots[0].Children, n => !n.IsDirectory && n.Name == "README.md" && n.Status == "A");

        var unstagedDirectory = roots[1].Children.FirstOrDefault(n => n.IsDirectory && n.Name == "src");
        Assert.NotNull(unstagedDirectory);
        Assert.Contains(unstagedDirectory!.Children, n => !n.IsDirectory && n.Name == "OldFile.cs" && n.Status == "D");
    }

    [Fact]
    public void BuildGitChangeTree_AddsPlaceholderWhenSectionIsEmpty()
    {
        var repoRoot = Path.GetFullPath("/tmp/notepadsharp-repo-empty");

        var roots = GitStatusLogic.BuildGitChangeTree(
            repoRoot,
            Array.Empty<GitChangeEntryModel>(),
            Array.Empty<GitChangeEntryModel>());

        Assert.Equal(2, roots.Count);
        Assert.Equal("(no staged files)", roots[0].Children.Single().Name);
        Assert.Equal("(working tree clean)", roots[1].Children.Single().Name);
    }
}

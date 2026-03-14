using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class ShellSessionMetadataLogicRegressionTests
{
    [Theory]
    [InlineData("zsh", "cd ..")]
    [InlineData("bash", "pushd src")]
    [InlineData("cmd", "popd")]
    [InlineData("cmd", "chdir ..")]
    [InlineData("zsh", "mkdir sandbox && cd sandbox")]
    public void CommandCanChangeWorkingDirectory_ReturnsTrueForNavigationCommands(string shellName, string command)
    {
        Assert.True(ShellSessionMetadataLogic.CommandCanChangeWorkingDirectory(shellName, command));
    }

    [Theory]
    [InlineData("zsh", "echo cd ..")]
    [InlineData("bash", "git status")]
    [InlineData("cmd", "dir")]
    [InlineData("cmd", "echo pushd tmp")]
    public void CommandCanChangeWorkingDirectory_ReturnsFalseForOtherCommands(string shellName, string command)
    {
        Assert.False(ShellSessionMetadataLogic.CommandCanChangeWorkingDirectory(shellName, command));
    }

    [Fact]
    public void BuildWorkingDirectoryProbeCommand_UsesCmdEchoOnWindowsShell()
    {
        var command = ShellSessionMetadataLogic.BuildWorkingDirectoryProbeCommand("cmd", "abc123");

        Assert.Equal("echo __NOTEPADSHARP_CWD__:abc123:%CD%", command);
    }

    [Fact]
    public void BuildWorkingDirectoryProbeCommand_UsesPrintfOnPosixShells()
    {
        var command = ShellSessionMetadataLogic.BuildWorkingDirectoryProbeCommand("zsh", "abc123");

        Assert.Equal("printf '__NOTEPADSHARP_CWD__:abc123:%s\\n' \"$PWD\"", command);
    }

    [Fact]
    public void BuildStatusProbeCommand_UsesStatusMarkerForCmd()
    {
        var command = ShellSessionMetadataLogic.BuildStatusProbeCommand("cmd", "abc123");

        Assert.Equal("echo __NOTEPADSHARP_STATUS__:abc123:%ERRORLEVEL%:%CD%", command);
    }

    [Fact]
    public void BuildStatusProbeCommand_UsesStatusMarkerForPosixShells()
    {
        var command = ShellSessionMetadataLogic.BuildStatusProbeCommand("zsh", "abc123");

        Assert.Equal("printf '__NOTEPADSHARP_STATUS__:abc123:%s:%s\\n' \"$?\" \"$PWD\"", command);
    }

    [Fact]
    public void ProcessOutputChunk_StripsMarkerLineAndReturnsWorkingDirectoryAndExitCode()
    {
        var processed = ShellSessionMetadataLogic.ProcessOutputChunk(
            string.Empty,
            "listing\n__NOTEPADSHARP_STATUS__:abc123:0:/tmp/work\n",
            "abc123");

        Assert.Equal("listing\n", processed.VisibleText);
        Assert.Equal(string.Empty, processed.PendingPartialLine);
        Assert.Equal("/tmp/work", processed.WorkingDirectory);
        Assert.Equal(0, processed.ExitCode);
    }

    [Fact]
    public void ProcessOutputChunk_HandlesSplitMarkerAcrossChunks()
    {
        var first = ShellSessionMetadataLogic.ProcessOutputChunk(
            string.Empty,
            "first line\n__NOTEPADSHARP_STATUS__:abc",
            "abc123");
        var second = ShellSessionMetadataLogic.ProcessOutputChunk(
            first.PendingPartialLine,
            "123:17:/tmp/next\nsecond line\n",
            "abc123");

        Assert.Equal("first line\n", first.VisibleText);
        Assert.Null(first.WorkingDirectory);
        Assert.Null(first.ExitCode);
        Assert.Equal("second line\n", second.VisibleText);
        Assert.Equal(string.Empty, second.PendingPartialLine);
        Assert.Equal("/tmp/next", second.WorkingDirectory);
        Assert.Equal(17, second.ExitCode);
    }

    [Fact]
    public void ProcessOutputChunk_ParsesWindowsDriveLetterWorkingDirectory()
    {
        var processed = ShellSessionMetadataLogic.ProcessOutputChunk(
            string.Empty,
            "__NOTEPADSHARP_STATUS__:abc123:1:C:\\workspace\\repo\n",
            "abc123");

        Assert.Equal("C:\\workspace\\repo", processed.WorkingDirectory);
        Assert.Equal(1, processed.ExitCode);
    }
}

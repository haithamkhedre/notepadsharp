using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class ShellSessionStatusLogicRegressionTests
{
    [Fact]
    public void FormatStateText_ReportsNoActiveSession()
    {
        var text = ShellSessionStatusLogic.FormatStateText(
            hasActiveSession: false,
            commandInProgress: false,
            pendingCommand: null,
            lastExitCode: null);

        Assert.Equal("No active shell session. Send a command to start one.", text);
    }

    [Fact]
    public void FormatStateText_ReportsRunningCommand()
    {
        var text = ShellSessionStatusLogic.FormatStateText(
            hasActiveSession: true,
            commandInProgress: true,
            pendingCommand: "dotnet test --nologo",
            lastExitCode: null);

        Assert.Equal("Running: dotnet test --nologo. Use Interrupt to stop it.", text);
    }

    [Fact]
    public void FormatStateText_ReportsLastExitCodeWhenIdle()
    {
        var text = ShellSessionStatusLogic.FormatStateText(
            hasActiveSession: true,
            commandInProgress: false,
            pendingCommand: null,
            lastExitCode: 2);

        Assert.Equal("Idle. Last command exited with 2.", text);
    }

    [Fact]
    public void GetInputWatermark_ReportsRunningState()
    {
        var text = ShellSessionStatusLogic.GetInputWatermark(
            hasActiveSession: true,
            isStartingSession: false,
            commandInProgress: true);

        Assert.Equal("Foreground command still running. Wait or use Interrupt...", text);
    }

    [Fact]
    public void SummarizeCommand_TruncatesLongCommands()
    {
        var summary = ShellSessionStatusLogic.SummarizeCommand(new string('x', 90));

        Assert.EndsWith("...", summary, StringComparison.Ordinal);
        Assert.True(summary.Length <= 72);
    }
}

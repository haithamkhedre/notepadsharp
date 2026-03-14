using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class CommandRunnerHistoryLogicRegressionTests
{
    [Fact]
    public void NormalizeHistory_TrimsDeduplicatesAndCaps()
    {
        var history = CommandRunnerHistoryLogic.NormalizeHistory(
            new[] { " git status ", "", "git status", "dotnet test", "git diff" },
            maxEntries: 2);

        Assert.Equal(new[] { "git status", "dotnet test" }, history);
    }

    [Fact]
    public void RecordCommand_PutsNewestCommandFirstAndRemovesDuplicate()
    {
        var history = CommandRunnerHistoryLogic.RecordCommand(
            new[] { "git status", "dotnet test", "git diff" },
            "dotnet test",
            maxEntries: 5);

        Assert.Equal(new[] { "dotnet test", "git status", "git diff" }, history);
    }
}

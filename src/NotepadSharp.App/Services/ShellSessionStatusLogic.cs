using System;

namespace NotepadSharp.App.Services;

public static class ShellSessionStatusLogic
{
    private const int MaxCommandSummaryLength = 72;
    private const string DefaultWatermark = "Send a command to the current shell session...";
    private const string StartingWatermark = "Starting shell session...";
    private const string RunningWatermark = "Foreground command still running. Wait or use Interrupt...";

    public static string FormatStateText(
        bool hasActiveSession,
        bool commandInProgress,
        string? pendingCommand,
        int? lastExitCode)
    {
        if (!hasActiveSession)
        {
            return "No active shell session. Send a command to start one.";
        }

        if (commandInProgress)
        {
            var commandText = SummarizeCommand(pendingCommand);
            return string.IsNullOrWhiteSpace(commandText)
                ? "Running foreground command. Use Interrupt to stop it."
                : $"Running: {commandText}. Use Interrupt to stop it.";
        }

        return lastExitCode switch
        {
            0 => "Idle. Last command exited with 0.",
            int exitCode => $"Idle. Last command exited with {exitCode}.",
            _ => "Idle. Shell session is ready.",
        };
    }

    public static string GetInputWatermark(bool hasActiveSession, bool isStartingSession, bool commandInProgress)
    {
        if (isStartingSession)
        {
            return StartingWatermark;
        }

        if (commandInProgress)
        {
            return RunningWatermark;
        }

        return hasActiveSession ? DefaultWatermark : "Send a command to start the shell session...";
    }

    public static string SummarizeCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var singleLine = command
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (singleLine.Length <= MaxCommandSummaryLength)
        {
            return singleLine;
        }

        return singleLine[..(MaxCommandSummaryLength - 3)].TrimEnd() + "...";
    }
}

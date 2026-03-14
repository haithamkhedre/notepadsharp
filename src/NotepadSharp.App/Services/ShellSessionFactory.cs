using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NotepadSharp.App.Services;

public static class ShellSessionFactory
{
    public static ShellSessionHandle Start(ShellCommandInvocation invocation, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (OperatingSystem.IsWindows() && invocation.UsesPty)
        {
            try
            {
                return WindowsPseudoConsoleSession.Start(invocation, workingDirectory);
            }
            catch
            {
                var fallbackInvocation = invocation with { UsesPty = false };
                return StartDirectProcess(
                    fallbackInvocation,
                    workingDirectory,
                    "Windows PTY startup failed. Using a direct shell session instead.");
            }
        }

        return StartDirectProcess(invocation, workingDirectory);
    }

    private static ShellSessionHandle StartDirectProcess(
        ShellCommandInvocation invocation,
        string workingDirectory,
        string? startupNote = null)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = invocation.FileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in invocation.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        ApplySessionEnvironment(process.StartInfo.Environment, invocation);

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start shell session: {invocation.DisplayName}");
        }

        return new ShellSessionHandle(
            process,
            invocation.DisplayName,
            invocation.UsesPty,
            process.StandardInput,
            new[] { process.StandardOutput, process.StandardError },
            startupNote);
    }

    private static void ApplySessionEnvironment(IDictionary<string, string?> environment, ShellCommandInvocation invocation)
    {
        if (invocation.UsesPty)
        {
            environment["TERM"] = "xterm-256color";
            environment["COLORTERM"] = "truecolor";
            environment.Remove("NO_COLOR");
            environment.Remove("CLICOLOR");
        }
        else
        {
            environment["TERM"] = "dumb";
            environment["NO_COLOR"] = "1";
            environment["CLICOLOR"] = "0";
            environment.Remove("COLORTERM");
        }

        if (invocation.UsesPty && string.Equals(invocation.DisplayName, "bash", StringComparison.Ordinal))
        {
            environment["BASH_SILENCE_DEPRECATION_WARNING"] = "1";
        }
    }
}

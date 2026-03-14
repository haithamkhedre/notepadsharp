using System;
using System.Linq;
using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class ShellCommandLogicRegressionTests
{
    [Fact]
    public void BuildCommandInvocation_UsesCmdOnWindows()
    {
        var invocation = ShellCommandLogic.BuildCommandInvocation(
            "dir",
            ShellPlatform.Windows,
            @"C:\Windows\System32\cmd.exe");

        Assert.Equal(@"C:\Windows\System32\cmd.exe", invocation.FileName);
        Assert.Equal("cmd", invocation.DisplayName);
        Assert.Equal(new[] { "/d", "/c", "dir" }, invocation.Arguments);
        Assert.False(invocation.UsesPty);
    }

    [Fact]
    public void BuildCommandInvocation_UsesPreferredPosixShellWhenItExists()
    {
        var invocation = ShellCommandLogic.BuildCommandInvocation(
            "pwd",
            ShellPlatform.Posix,
            "/bin/zsh",
            path => string.Equals(path, "/bin/zsh", StringComparison.Ordinal));

        Assert.Equal("/bin/zsh", invocation.FileName);
        Assert.Equal("zsh", invocation.DisplayName);
        Assert.Equal(new[] { "-lc", "pwd" }, invocation.Arguments);
        Assert.False(invocation.UsesPty);
    }

    [Fact]
    public void BuildCommandInvocation_FallsBackToAvailablePosixShell()
    {
        var invocation = ShellCommandLogic.BuildCommandInvocation(
            "pwd",
            ShellPlatform.Posix,
            "/bin/fish",
            path => string.Equals(path, "/bin/sh", StringComparison.Ordinal));

        Assert.Equal("/bin/sh", invocation.FileName);
        Assert.Equal("sh", invocation.DisplayName);
        Assert.Equal(new[] { "-lc", "pwd" }, invocation.Arguments);
        Assert.False(invocation.UsesPty);
    }

    [Fact]
    public void BuildSessionInvocation_UsesPersistentWindowsPseudoConsoleWhenSupported()
    {
        var invocation = ShellCommandLogic.BuildSessionInvocation(
            ShellPlatform.Windows,
            @"C:\Windows\System32\cmd.exe",
            windowsVersion: new Version(10, 0, 17763));

        Assert.Equal(@"C:\Windows\System32\cmd.exe", invocation.FileName);
        Assert.Equal("cmd", invocation.DisplayName);
        Assert.Equal(new[] { "/Q", "/K" }, invocation.Arguments);
        Assert.True(invocation.UsesPty);
    }

    [Fact]
    public void BuildSessionInvocation_FallsBackToDirectWindowsShellWhenPseudoConsoleIsUnavailable()
    {
        var invocation = ShellCommandLogic.BuildSessionInvocation(
            ShellPlatform.Windows,
            @"C:\Windows\System32\cmd.exe",
            windowsVersion: new Version(10, 0, 17134));

        Assert.Equal(@"C:\Windows\System32\cmd.exe", invocation.FileName);
        Assert.Equal("cmd", invocation.DisplayName);
        Assert.Equal(new[] { "/Q", "/K" }, invocation.Arguments);
        Assert.False(invocation.UsesPty);
    }

    [Fact]
    public void BuildSessionInvocation_DisablesWindowsPseudoConsoleWhenFlagIsSet()
    {
        var invocation = ShellCommandLogic.BuildSessionInvocation(
            ShellPlatform.Windows,
            @"C:\Windows\System32\cmd.exe",
            windowsVersion: new Version(10, 0, 19045),
            disableWindowsPseudoConsoleFlag: "1");

        Assert.False(invocation.UsesPty);
    }

    [Fact]
    public void BuildSessionInvocation_UsesScriptWrappedPtyForZshWhenAvailable()
    {
        var invocation = ShellCommandLogic.BuildSessionInvocation(
            ShellPlatform.Posix,
            "/bin/zsh",
            path => string.Equals(path, "/bin/zsh", StringComparison.Ordinal)
                || string.Equals(path, "/usr/bin/script", StringComparison.Ordinal));

        Assert.Equal("/usr/bin/script", invocation.FileName);
        Assert.Equal("zsh", invocation.DisplayName);
        Assert.True(invocation.UsesPty);
        Assert.Equal(GetExpectedScriptArguments("/bin/zsh", "-f"), invocation.Arguments);
    }

    [Fact]
    public void BuildSessionInvocation_UsesScriptWrappedPtyForBashWhenAvailable()
    {
        var invocation = ShellCommandLogic.BuildSessionInvocation(
            ShellPlatform.Posix,
            "/bin/bash",
            path => string.Equals(path, "/bin/bash", StringComparison.Ordinal)
                || string.Equals(path, "/usr/bin/script", StringComparison.Ordinal));

        Assert.Equal("/usr/bin/script", invocation.FileName);
        Assert.Equal("bash", invocation.DisplayName);
        Assert.True(invocation.UsesPty);
        Assert.Equal(GetExpectedScriptArguments("/bin/bash", "--noprofile", "--norc"), invocation.Arguments);
    }

    [Fact]
    public void BuildSessionInvocation_FallsBackToDirectShellWhenScriptIsUnavailable()
    {
        var invocation = ShellCommandLogic.BuildSessionInvocation(
            ShellPlatform.Posix,
            "/bin/zsh",
            path => string.Equals(path, "/bin/zsh", StringComparison.Ordinal));

        Assert.Equal("/bin/zsh", invocation.FileName);
        Assert.Equal("zsh", invocation.DisplayName);
        Assert.Empty(invocation.Arguments);
        Assert.False(invocation.UsesPty);
    }

    [Fact]
    public void BuildWindowsCommandLine_QuotesExecutableAndArgumentsWithSpaces()
    {
        var commandLine = ShellCommandLogic.BuildWindowsCommandLine(
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            new[] { "-NoLogo", "-Command", "Write-Output \"hello world\"" });

        Assert.Equal(
            "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo -Command \"Write-Output \\\"hello world\\\"\"",
            commandLine);
    }

    private static string[] GetExpectedScriptArguments(string shellPath, params string[] shellArguments)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new[] { "-q", "/dev/null", shellPath }.Concat(shellArguments).ToArray();
        }

        return new[] { "-q", "-c", GetExpectedLinuxCommand(shellPath, shellArguments), "/dev/null" };
    }

    private static string GetExpectedLinuxCommand(string shellPath, params string[] shellArguments)
        => string.Join(' ', new[] { shellPath }.Concat(shellArguments).Select(QuotePosixArgument));

    private static string QuotePosixArgument(string value)
        => $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NotepadSharp.App.Services;

public enum ShellPlatform
{
    Windows,
    Posix,
}

public sealed record ShellCommandInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    string DisplayName,
    bool UsesPty = false);

public static class ShellCommandLogic
{
    private static readonly Version MinimumWindowsPseudoConsoleVersion = new(10, 0, 17763);

    public static ShellCommandInvocation BuildCommandInvocation(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return OperatingSystem.IsWindows()
            ? BuildCommandInvocation(command, ShellPlatform.Windows, Environment.GetEnvironmentVariable("ComSpec"))
            : BuildCommandInvocation(command, ShellPlatform.Posix, Environment.GetEnvironmentVariable("SHELL"));
    }

    public static ShellCommandInvocation BuildCommandInvocation(
        string command,
        ShellPlatform platform,
        string? preferredShellPath,
        Func<string, bool>? fileExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return ResolveShell(platform, preferredShellPath, fileExists) switch
        {
            var shell when platform == ShellPlatform.Windows => new ShellCommandInvocation(
                shell.FileName,
                new[] { "/d", "/c", command },
                shell.DisplayName),
            var shell => new ShellCommandInvocation(
                shell.FileName,
                new[] { "-lc", command },
                shell.DisplayName),
        };
    }

    public static ShellCommandInvocation BuildSessionInvocation()
        => OperatingSystem.IsWindows()
            ? BuildSessionInvocation(
                ShellPlatform.Windows,
                Environment.GetEnvironmentVariable("ComSpec"),
                windowsVersion: Environment.OSVersion.Version,
                disableWindowsPseudoConsoleFlag: Environment.GetEnvironmentVariable("NOTEPADSHARP_DISABLE_CONPTY"))
            : BuildSessionInvocation(ShellPlatform.Posix, Environment.GetEnvironmentVariable("SHELL"));

    public static ShellCommandInvocation BuildSessionInvocation(
        ShellPlatform platform,
        string? preferredShellPath,
        Func<string, bool>? fileExists = null,
        Version? windowsVersion = null,
        string? disableWindowsPseudoConsoleFlag = null)
    {
        fileExists ??= File.Exists;

        var shell = ResolveShell(platform, preferredShellPath, fileExists);
        var usesWindowsPseudoConsole = platform == ShellPlatform.Windows
            && SupportsWindowsPseudoConsole(windowsVersion, disableWindowsPseudoConsoleFlag);
        return shell switch
            {
            _ when platform == ShellPlatform.Windows => new ShellCommandInvocation(
                shell.FileName,
                new[] { "/Q", "/K" },
                shell.DisplayName,
                UsesPty: usesWindowsPseudoConsole),
            _ when TryResolvePosixScriptPath(fileExists) is { } scriptPath => new ShellCommandInvocation(
                scriptPath,
                BuildPosixPtyArguments(shell.FileName, shell.DisplayName),
                shell.DisplayName,
                UsesPty: true),
            _ => new ShellCommandInvocation(
                shell.FileName,
                Array.Empty<string>(),
                shell.DisplayName),
        };
    }

    public static string GetDefaultShellDisplayName()
    {
        var platform = OperatingSystem.IsWindows() ? ShellPlatform.Windows : ShellPlatform.Posix;
        var preferredShell = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("ComSpec")
            : Environment.GetEnvironmentVariable("SHELL");

        return ResolveShell(platform, preferredShell).DisplayName;
    }

    public static bool SupportsWindowsPseudoConsole()
        => OperatingSystem.IsWindows()
            && SupportsWindowsPseudoConsole(
                Environment.OSVersion.Version,
                Environment.GetEnvironmentVariable("NOTEPADSHARP_DISABLE_CONPTY"));

    public static bool SupportsWindowsPseudoConsole(Version? windowsVersion, string? disableWindowsPseudoConsoleFlag = null)
    {
        if (IsTruthyEnvironmentFlag(disableWindowsPseudoConsoleFlag))
        {
            return false;
        }

        return windowsVersion is not null
            && windowsVersion >= MinimumWindowsPseudoConsoleVersion;
    }

    public static string BuildWindowsCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var commandParts = new List<string> { QuoteWindowsArgument(fileName) };
        if (arguments is not null)
        {
            commandParts.AddRange(arguments.Select(QuoteWindowsArgument));
        }

        return string.Join(' ', commandParts);
    }

    private static (string FileName, string DisplayName) ResolveShell(
        ShellPlatform platform,
        string? preferredShellPath,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        if (platform == ShellPlatform.Windows)
        {
            var windowsShellPath = string.IsNullOrWhiteSpace(preferredShellPath) ? "cmd.exe" : preferredShellPath;
            return (windowsShellPath, GetShellDisplayName(windowsShellPath));
        }

        var posixShellPath = ResolvePosixShell(preferredShellPath, fileExists);
        return (posixShellPath, GetShellDisplayName(posixShellPath));
    }

    private static string ResolvePosixShell(string? preferredShellPath, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(preferredShellPath) && fileExists(preferredShellPath))
        {
            return preferredShellPath;
        }

        var candidates = OperatingSystem.IsMacOS()
            ? new[] { "/bin/zsh", "/bin/bash", "/bin/sh" }
            : new[] { "/bin/bash", "/bin/sh", "/bin/zsh" };

        foreach (var candidate in candidates)
        {
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return "/bin/sh";
    }

    private static string? TryResolvePosixScriptPath(Func<string, bool> fileExists)
    {
        foreach (var candidate in new[] { "/usr/bin/script", "/bin/script" })
        {
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildPosixPtyArguments(string shellPath, string shellDisplayName)
    {
        var shellArguments = GetPosixSessionShellArguments(shellDisplayName);
        return UsesBsdScriptArguments()
            ? BuildBsdPosixPtyArguments(shellPath, shellArguments)
            : BuildLinuxPosixPtyArguments(shellPath, shellArguments);
    }

    private static IReadOnlyList<string> BuildBsdPosixPtyArguments(string shellPath, IReadOnlyList<string> shellArguments)
    {
        var arguments = new List<string> { "-q", "/dev/null", shellPath };
        arguments.AddRange(shellArguments);
        return arguments;
    }

    private static IReadOnlyList<string> BuildLinuxPosixPtyArguments(string shellPath, IReadOnlyList<string> shellArguments)
    {
        var commandParts = new[] { shellPath }.Concat(shellArguments);
        return new[]
        {
            "-q",
            "-c",
            string.Join(' ', commandParts.Select(QuotePosixArgument)),
            "/dev/null",
        };
    }

    private static IReadOnlyList<string> GetPosixSessionShellArguments(string shellDisplayName)
        => shellDisplayName switch
        {
            "zsh" => new[] { "-f" },
            "bash" => new[] { "--noprofile", "--norc" },
            _ => Array.Empty<string>(),
        };

    private static bool UsesBsdScriptArguments()
        => OperatingSystem.IsMacOS();

    private static string QuotePosixArgument(string argument)
        => $"'{argument.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";

    private static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        var requiresQuoting = argument.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!requiresQuoting)
        {
            return argument;
        }

        var builder = new StringBuilder(argument.Length + 8);
        builder.Append('"');
        var backslashCount = 0;

        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(ch);
        }

        if (backslashCount > 0)
        {
            builder.Append('\\', backslashCount * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static bool IsTruthyEnvironmentFlag(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim() is not ("0" or "false" or "False" or "FALSE" or "no" or "No" or "NO");

    private static string GetShellDisplayName(string shellPath)
    {
        var normalizedPath = shellPath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedPath);
        return Path.GetFileNameWithoutExtension(fileName);
    }
}

using System;
using System.Text.RegularExpressions;
using System.Text;

namespace NotepadSharp.App.Services;

public sealed record ShellSessionOutputChunk(
    string VisibleText,
    string PendingPartialLine,
    string? WorkingDirectory,
    int? ExitCode);

public static class ShellSessionMetadataLogic
{
    private const string WorkingDirectoryMarkerPrefix = "__NOTEPADSHARP_CWD__:";
    private const string StatusMarkerPrefix = "__NOTEPADSHARP_STATUS__:";
    private static readonly Regex ShellNavigationCommandRegex = new(
        @"(?:^|&&|\|\||;)\s*(?<command>cd|pushd|popd|chdir)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool CommandCanChangeWorkingDirectory(string? shellDisplayName, string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var trimmed = command.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        foreach (Match match in ShellNavigationCommandRegex.Matches(trimmed))
        {
            var token = match.Groups["command"].Value;
            if (NormalizeToken(token) switch
                {
                    "cd" => true,
                    "pushd" => true,
                    "popd" => true,
                    "chdir" when string.Equals(shellDisplayName, "cmd", StringComparison.OrdinalIgnoreCase) => true,
                    _ => false,
                })
            {
                return true;
            }
        }

        return false;
    }

    public static string? BuildWorkingDirectoryProbeCommand(string? shellDisplayName, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return string.Equals(shellDisplayName, "cmd", StringComparison.OrdinalIgnoreCase)
            ? $"echo {WorkingDirectoryMarkerPrefix}{token}:%CD%"
            : $"printf '{WorkingDirectoryMarkerPrefix}{token}:%s\\n' \"$PWD\"";
    }

    public static string? BuildStatusProbeCommand(string? shellDisplayName, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return string.Equals(shellDisplayName, "cmd", StringComparison.OrdinalIgnoreCase)
            ? $"echo {StatusMarkerPrefix}{token}:%ERRORLEVEL%:%CD%"
            : $"printf '{StatusMarkerPrefix}{token}:%s:%s\\n' \"$?\" \"$PWD\"";
    }

    public static ShellSessionOutputChunk ProcessOutputChunk(string? pendingPartialLine, string? chunk, string? token)
    {
        var text = (pendingPartialLine ?? string.Empty) + (chunk ?? string.Empty);
        if (text.Length == 0)
        {
            return new ShellSessionOutputChunk(string.Empty, string.Empty, null, null);
        }

        var builder = new StringBuilder(text.Length);
        string? workingDirectory = null;
        int? exitCode = null;
        var lineStart = 0;

        while (lineStart < text.Length)
        {
            var newlineIndex = text.IndexOf('\n', lineStart);
            if (newlineIndex < 0)
            {
                return new ShellSessionOutputChunk(builder.ToString(), text[lineStart..], workingDirectory, exitCode);
            }

            var line = text[lineStart..newlineIndex];
            if (TryParseStatusMarker(line, token, out var parsedWorkingDirectory, out var parsedExitCode))
            {
                if (!string.IsNullOrWhiteSpace(parsedWorkingDirectory))
                {
                    workingDirectory = parsedWorkingDirectory;
                }

                exitCode = parsedExitCode;
            }
            else if (TryParseWorkingDirectoryMarker(line, token, out parsedWorkingDirectory))
            {
                if (!string.IsNullOrWhiteSpace(parsedWorkingDirectory))
                {
                    workingDirectory = parsedWorkingDirectory;
                }
            }
            else
            {
                builder.Append(line);
                builder.Append('\n');
            }

            lineStart = newlineIndex + 1;
        }

        return new ShellSessionOutputChunk(builder.ToString(), string.Empty, workingDirectory, exitCode);
    }

    public static bool IsMarkerOnlyRemainder(string? pendingPartialLine, string? token)
        => TryParseWorkingDirectoryMarker(pendingPartialLine, token, out _)
            || TryParseStatusMarker(pendingPartialLine, token, out _, out _)
            || (!string.IsNullOrWhiteSpace(token)
                && !string.IsNullOrEmpty(pendingPartialLine)
                && ($"{WorkingDirectoryMarkerPrefix}{token}:".StartsWith(pendingPartialLine, StringComparison.Ordinal)
                    || $"{StatusMarkerPrefix}{token}:".StartsWith(pendingPartialLine, StringComparison.Ordinal)));

    private static bool TryParseStatusMarker(string? line, string? token, out string workingDirectory, out int? exitCode)
    {
        workingDirectory = string.Empty;
        exitCode = null;
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var prefix = $"{StatusMarkerPrefix}{token}:";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = line[prefix.Length..];
        var separatorIndex = payload.IndexOf(':');
        if (separatorIndex < 0)
        {
            return false;
        }

        if (int.TryParse(payload[..separatorIndex], out var parsedExitCode))
        {
            exitCode = parsedExitCode;
        }

        workingDirectory = payload[(separatorIndex + 1)..];
        return true;
    }

    private static bool TryParseWorkingDirectoryMarker(string? line, string? token, out string workingDirectory)
    {
        workingDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var prefix = $"{WorkingDirectoryMarkerPrefix}{token}:";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        workingDirectory = line[prefix.Length..];
        return true;
    }

    private static string NormalizeToken(string token)
        => token.Trim().Trim('"', '\'').ToLowerInvariant();
}

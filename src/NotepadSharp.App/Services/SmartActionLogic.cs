using System;
using System.Collections.Generic;
using System.Linq;

namespace NotepadSharp.App.Services;

public enum SmartActionKind
{
    Ask,
    Explain,
    Refactor,
    FixDiagnostics,
    GenerateTests,
    CommitMessage,
}

public static class SmartActionLogic
{
    public const string SidebarSectionName = "Smart Actions";
    public const string LegacySidebarSectionName = "AI Assistant";
    public const string DefaultScopeMode = "Selection only";
    public const string DefaultQuickCommand = "Auto (from prompt)";

    public static IReadOnlyList<string> ScopeModes { get; } = new[]
    {
        DefaultScopeMode,
        "Current file",
        "Workspace summary",
    };

    public static IReadOnlyList<string> QuickCommands { get; } = new[]
    {
        DefaultQuickCommand,
        "/explain",
        "/refactor",
        "/fix",
        "/tests",
        "/commit",
    };

    public static string NormalizeSidebarSectionAlias(string? value)
    {
        if (string.Equals(value?.Trim(), LegacySidebarSectionName, StringComparison.Ordinal))
        {
            return SidebarSectionName;
        }

        return value?.Trim() ?? string.Empty;
    }

    public static string NormalizeScopeMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultScopeMode;
        }

        var candidate = value.Trim();
        return ScopeModes.Any(mode => string.Equals(mode, candidate, StringComparison.Ordinal))
            ? candidate
            : DefaultScopeMode;
    }

    public static string NormalizeQuickCommand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultQuickCommand;
        }

        var candidate = value.Trim();
        return QuickCommands.Any(command => string.Equals(command, candidate, StringComparison.Ordinal))
            ? candidate
            : DefaultQuickCommand;
    }

    public static SmartActionKind ResolveAskKind(string prompt, string quickCommand, out string cleanedPrompt)
    {
        cleanedPrompt = RemoveLeadingSlashCommand(prompt);

        var quickKind = ResolveQuickCommandKind(quickCommand);
        if (quickKind is not null)
        {
            return quickKind.Value;
        }

        var slashKind = ResolveQuickCommandKind(prompt);
        if (slashKind is not null)
        {
            return slashKind.Value;
        }

        return ResolvePromptKind(cleanedPrompt);
    }

    public static SmartActionKind ResolvePromptKind(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return SmartActionKind.Explain;
        }

        if (prompt.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return SmartActionKind.GenerateTests;
        }

        if (prompt.Contains("diagnostic", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("error", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("fix", StringComparison.OrdinalIgnoreCase))
        {
            return SmartActionKind.FixDiagnostics;
        }

        if (prompt.Contains("refactor", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("clean", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("improve", StringComparison.OrdinalIgnoreCase))
        {
            return SmartActionKind.Refactor;
        }

        if (prompt.Contains("commit", StringComparison.OrdinalIgnoreCase))
        {
            return SmartActionKind.CommitMessage;
        }

        return SmartActionKind.Explain;
    }

    public static SmartActionKind? ResolveQuickCommandKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, DefaultQuickCommand, StringComparison.Ordinal))
        {
            return null;
        }

        var token = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        return token switch
        {
            "/explain" => SmartActionKind.Explain,
            "/refactor" => SmartActionKind.Refactor,
            "/fix" or "/diagnostics" => SmartActionKind.FixDiagnostics,
            "/tests" or "/test" => SmartActionKind.GenerateTests,
            "/commit" => SmartActionKind.CommitMessage,
            _ => null,
        };
    }

    public static string RemoveLeadingSlashCommand(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        var trimmed = prompt.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace <= 0)
        {
            return string.Empty;
        }

        return trimmed[(firstSpace + 1)..].Trim();
    }

    public static string GetHistoryLabel(SmartActionKind action)
    {
        return action switch
        {
            SmartActionKind.Explain => "Explain",
            SmartActionKind.Refactor => "Refactor",
            SmartActionKind.FixDiagnostics => "Fix diagnostics",
            SmartActionKind.GenerateTests => "Generate tests",
            SmartActionKind.CommitMessage => "Commit message",
            _ => "Run",
        };
    }
}

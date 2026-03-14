using System;
using System.Collections.Generic;
using System.Linq;

namespace NotepadSharp.App.Services;

public sealed record GitBranchChoice(string Name, bool IsCurrent, bool IsRemote);

public static class GitBranchLogic
{
    public static string? NormalizeBranchName(string? raw)
    {
        var value = raw?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static IReadOnlyList<string> ParseBranchNames(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeBranchName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    public static IReadOnlyList<GitBranchChoice> BuildBranchChoices(IEnumerable<string> branches, string? currentBranch)
    {
        ArgumentNullException.ThrowIfNull(branches);

        var normalizedCurrent = NormalizeBranchName(currentBranch);
        var normalizedBranches = branches
            .Select(NormalizeBranchName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(normalizedCurrent)
            && !normalizedBranches.Contains(normalizedCurrent, StringComparer.OrdinalIgnoreCase))
        {
            normalizedBranches.Add(normalizedCurrent);
        }

        return normalizedBranches
            .OrderBy(name => !string.Equals(name, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new GitBranchChoice(
                name,
                string.Equals(name, normalizedCurrent, StringComparison.OrdinalIgnoreCase),
                IsRemote: false))
            .ToList();
    }

    public static IReadOnlyList<GitBranchChoice> BuildSwitchChoices(
        IEnumerable<string> localBranches,
        IEnumerable<string> remoteBranches,
        string? currentBranch)
    {
        ArgumentNullException.ThrowIfNull(localBranches);
        ArgumentNullException.ThrowIfNull(remoteBranches);

        var locals = BuildBranchChoices(localBranches, currentBranch).ToList();
        var localNames = locals.Select(choice => choice.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remotes = remoteBranches
            .Select(NormalizeBranchName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !name!.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => name!)
            .Where(name =>
            {
                var trackingName = GetRemoteTrackingBranchName(name);
                return !string.IsNullOrWhiteSpace(trackingName)
                    && !localNames.Contains(trackingName);
            })
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new GitBranchChoice(name, IsCurrent: false, IsRemote: true));

        return locals.Concat(remotes).ToList();
    }

    public static string? GetRemoteTrackingBranchName(string? remoteBranch)
    {
        var normalized = NormalizeBranchName(remoteBranch);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var separator = normalized.IndexOf('/');
        if (separator <= 0 || separator >= normalized.Length - 1)
        {
            return null;
        }

        return normalized[(separator + 1)..];
    }

    public static string BuildBranchOptionTitle(GitBranchChoice choice)
        => choice.IsCurrent ? $"{choice.Name} (current)" : choice.Name;

    public static string BuildBranchOptionDescription(GitBranchChoice choice)
    {
        if (choice.IsCurrent)
        {
            return "Current local branch";
        }

        if (choice.IsRemote)
        {
            var trackingName = GetRemoteTrackingBranchName(choice.Name);
            return string.IsNullOrWhiteSpace(trackingName)
                ? "Create tracking branch from remote"
                : $"Create local tracking branch '{trackingName}'";
        }

        return "Switch local branch";
    }

    public static string BuildBranchSelectionId(GitBranchChoice choice)
        => choice.IsRemote ? $"branch-remote:{choice.Name}" : $"branch-local:{choice.Name}";

    public static IReadOnlyList<GitBranchChoice> BuildDeleteChoices(IEnumerable<string> localBranches, string? currentBranch)
    {
        ArgumentNullException.ThrowIfNull(localBranches);

        return BuildBranchChoices(localBranches, currentBranch)
            .Where(choice => !choice.IsCurrent)
            .ToList();
    }

    public static IReadOnlyList<string> BuildRenameArguments(string branchName)
    {
        var normalized = NormalizeBranchName(branchName)
            ?? throw new ArgumentException("Branch name cannot be empty.", nameof(branchName));
        return new[] { "branch", "-m", normalized };
    }

    public static IReadOnlyList<string> BuildDeleteArguments(string branchName, bool force)
    {
        var normalized = NormalizeBranchName(branchName)
            ?? throw new ArgumentException("Branch name cannot be empty.", nameof(branchName));
        return new[] { "branch", force ? "-D" : "-d", normalized };
    }

    public static bool ShouldOfferForceDelete(string? errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText))
        {
            return false;
        }

        return errorText.Contains("not fully merged", StringComparison.OrdinalIgnoreCase)
               || errorText.Contains("is not an ancestor", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseBranchSelection(string? selectionId, out bool isRemote, out string branchName)
    {
        branchName = string.Empty;
        isRemote = false;

        if (string.IsNullOrWhiteSpace(selectionId))
        {
            return false;
        }

        const string localPrefix = "branch-local:";
        if (selectionId.StartsWith(localPrefix, StringComparison.Ordinal))
        {
            branchName = selectionId[localPrefix.Length..];
            return !string.IsNullOrWhiteSpace(branchName);
        }

        const string remotePrefix = "branch-remote:";
        if (selectionId.StartsWith(remotePrefix, StringComparison.Ordinal))
        {
            branchName = selectionId[remotePrefix.Length..];
            isRemote = true;
            return !string.IsNullOrWhiteSpace(branchName);
        }

        return false;
    }

    public static bool TryParseAheadBehindCounts(string? raw, out int ahead, out int behind)
    {
        ahead = 0;
        behind = 0;

        var trimmed = raw?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var parts = trimmed
            .Split((char[])null!, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2
            || !int.TryParse(parts[0], out behind)
            || !int.TryParse(parts[1], out ahead))
        {
            ahead = 0;
            behind = 0;
            return false;
        }

        return true;
    }

    public static string FormatBranchLabel(string? currentBranch, string? detachedCommit = null, int ahead = 0, int behind = 0)
    {
        var normalizedCurrent = NormalizeBranchName(currentBranch);
        if (!string.IsNullOrWhiteSpace(normalizedCurrent)
            && !string.Equals(normalizedCurrent, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            var tracking = FormatTrackingSuffix(ahead, behind);
            return string.IsNullOrWhiteSpace(tracking)
                ? $"Branch: {normalizedCurrent}"
                : $"Branch: {normalizedCurrent} {tracking}";
        }

        var normalizedCommit = NormalizeBranchName(detachedCommit);
        return string.IsNullOrWhiteSpace(normalizedCommit)
            ? "Branch: HEAD (detached)"
            : $"Branch: HEAD ({normalizedCommit})";
    }

    private static string FormatTrackingSuffix(int ahead, int behind)
    {
        var parts = new List<string>(capacity: 2);
        if (ahead > 0)
        {
            parts.Add($"\u2191{ahead}");
        }

        if (behind > 0)
        {
            parts.Add($"\u2193{behind}");
        }

        return string.Join(' ', parts);
    }
}

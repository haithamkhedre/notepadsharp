using System;
using System.Collections.Generic;
using System.IO;

namespace NotepadSharp.App.Services;

public enum GitRepositoryOperationKind
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert,
    Bisect,
}

public sealed record GitRepositoryOperationState(
    GitRepositoryOperationKind Kind,
    string DisplayName,
    string StatusText,
    string? ContinueButtonLabel,
    IReadOnlyList<string>? ContinueArguments,
    string AbortButtonLabel,
    IReadOnlyList<string> AbortArguments);

public static class GitRepositoryOperationLogic
{
    public static GitRepositoryOperationState? DetectState(
        string gitDirectory,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null)
    {
        var normalizedGitDirectory = gitDirectory?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedGitDirectory))
        {
            return null;
        }

        fileExists ??= File.Exists;
        directoryExists ??= Directory.Exists;

        if (directoryExists(Path.Combine(normalizedGitDirectory, "rebase-merge"))
            || directoryExists(Path.Combine(normalizedGitDirectory, "rebase-apply")))
        {
            return BuildState(GitRepositoryOperationKind.Rebase);
        }

        if (fileExists(Path.Combine(normalizedGitDirectory, "CHERRY_PICK_HEAD")))
        {
            return BuildState(GitRepositoryOperationKind.CherryPick);
        }

        if (fileExists(Path.Combine(normalizedGitDirectory, "REVERT_HEAD")))
        {
            return BuildState(GitRepositoryOperationKind.Revert);
        }

        if (fileExists(Path.Combine(normalizedGitDirectory, "MERGE_HEAD")))
        {
            return BuildState(GitRepositoryOperationKind.Merge);
        }

        if (fileExists(Path.Combine(normalizedGitDirectory, "BISECT_LOG")))
        {
            return BuildState(GitRepositoryOperationKind.Bisect);
        }

        return null;
    }

    public static GitRepositoryOperationState BuildState(GitRepositoryOperationKind kind)
        => kind switch
        {
            GitRepositoryOperationKind.Merge => new GitRepositoryOperationState(
                kind,
                "Merge",
                "Merge in progress. Resolve conflicts and commit when ready, or abort the merge.",
                null,
                null,
                "Abort Merge",
                new[] { "merge", "--abort" }),
            GitRepositoryOperationKind.Rebase => new GitRepositoryOperationState(
                kind,
                "Rebase",
                "Rebase in progress. Resolve conflicts and continue externally when ready, or abort the rebase.",
                "Continue Rebase",
                new[] { "-c", "core.editor=true", "rebase", "--continue" },
                "Abort Rebase",
                new[] { "rebase", "--abort" }),
            GitRepositoryOperationKind.CherryPick => new GitRepositoryOperationState(
                kind,
                "Cherry-pick",
                "Cherry-pick in progress. Resolve conflicts and continue externally when ready, or abort the cherry-pick.",
                "Continue Cherry-pick",
                new[] { "-c", "core.editor=true", "cherry-pick", "--continue" },
                "Abort Cherry-pick",
                new[] { "cherry-pick", "--abort" }),
            GitRepositoryOperationKind.Revert => new GitRepositoryOperationState(
                kind,
                "Revert",
                "Revert in progress. Resolve conflicts and continue externally when ready, or abort the revert.",
                "Continue Revert",
                new[] { "-c", "core.editor=true", "revert", "--continue" },
                "Abort Revert",
                new[] { "revert", "--abort" }),
            GitRepositoryOperationKind.Bisect => new GitRepositoryOperationState(
                kind,
                "Bisect",
                "Bisect session in progress. Reset bisect state when you are done investigating.",
                null,
                null,
                "Reset Bisect",
                new[] { "bisect", "reset" }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported git repository operation."),
        };
}

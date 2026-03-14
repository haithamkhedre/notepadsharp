using System;

namespace NotepadSharp.App.Services;

public enum GitDiffCompareSourceKind
{
    Empty,
    WorkingTree,
    GitObject,
}

public readonly record struct GitDiffCompareSource(
    GitDiffCompareSourceKind Kind,
    string Title,
    string? RevisionPrefix = null);

public readonly record struct GitDiffComparePlan(
    GitDiffCompareSource Primary,
    GitDiffCompareSource Secondary)
{
    public bool IsReadOnly => true;
}

public static class GitDiffCompareLogic
{
    public static GitDiffComparePlan BuildPlan(GitChangeSection section, string? status)
    {
        var normalizedStatus = status?.Trim() ?? string.Empty;

        return section switch
        {
            GitChangeSection.Staged => BuildStagedPlan(normalizedStatus),
            GitChangeSection.Conflicts => BuildConflictPlan(),
            _ => BuildUnstagedPlan(normalizedStatus),
        };
    }

    public static string BuildObjectExpression(string revisionPrefix, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        if (string.Equals(revisionPrefix, ":", StringComparison.Ordinal))
        {
            return $":{normalizedPath}";
        }

        if (revisionPrefix.StartsWith(":", StringComparison.Ordinal))
        {
            return $"{revisionPrefix}:{normalizedPath}";
        }

        return $"{revisionPrefix}:{normalizedPath}";
    }

    public static bool ShouldUseEmptySource(GitDiffCompareSource source)
        => source.Kind == GitDiffCompareSourceKind.Empty;

    private static GitDiffComparePlan BuildUnstagedPlan(string status)
    {
        if (status.Contains("?", StringComparison.Ordinal))
        {
            return new GitDiffComparePlan(
                new GitDiffCompareSource(GitDiffCompareSourceKind.Empty, "Base: Empty"),
                new GitDiffCompareSource(GitDiffCompareSourceKind.WorkingTree, "Working Tree"));
        }

        if (status.Contains("D", StringComparison.Ordinal))
        {
            return new GitDiffComparePlan(
                new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Index Snapshot", ":"),
                new GitDiffCompareSource(GitDiffCompareSourceKind.Empty, "Working Tree: Deleted"));
        }

        return new GitDiffComparePlan(
            new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Index Snapshot", ":"),
            new GitDiffCompareSource(GitDiffCompareSourceKind.WorkingTree, "Working Tree"));
    }

    private static GitDiffComparePlan BuildStagedPlan(string status)
    {
        if (status.Contains("A", StringComparison.Ordinal))
        {
            return new GitDiffComparePlan(
                new GitDiffCompareSource(GitDiffCompareSourceKind.Empty, "HEAD: Empty"),
                new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Staged Snapshot", ":"));
        }

        if (status.Contains("D", StringComparison.Ordinal))
        {
            return new GitDiffComparePlan(
                new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "HEAD", "HEAD"),
                new GitDiffCompareSource(GitDiffCompareSourceKind.Empty, "Staged Snapshot: Deleted"));
        }

        return new GitDiffComparePlan(
            new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "HEAD", "HEAD"),
            new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Staged Snapshot", ":"));
    }

    private static GitDiffComparePlan BuildConflictPlan()
        => new(
            new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Ours", ":2"),
            new GitDiffCompareSource(GitDiffCompareSourceKind.GitObject, "Theirs", ":3"));
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadSharp.App.Services;

public sealed record WorkspaceSearchMatch(
    string FilePath,
    string RelativePath,
    int Line,
    int Column,
    int Length,
    string Preview)
{
    public override string ToString()
        => $"{RelativePath}:{Line}:{Column}  {Preview}";
}

public sealed record WorkspaceSearchProgress(
    int ScannedFileCount,
    int MatchCount,
    int MatchedFileCount,
    bool HitMatchLimit,
    string? CurrentFile,
    int SkippedFileCount);

public sealed record WorkspaceSearchResult(
    int ScannedFileCount,
    IReadOnlyList<WorkspaceSearchMatch> Matches,
    int MatchedFileCount,
    bool HitMatchLimit,
    int SkippedFileCount);

public sealed record WorkspaceReplaceProgress(
    int ScannedFileCount,
    int FilesChanged,
    int ReplacementCount,
    string? CurrentFile,
    int SkippedFileCount);

public sealed record WorkspaceReplaceResult(
    int ScannedFileCount,
    int FilesChanged,
    int ReplacementCount,
    IReadOnlyList<string> ChangedFiles,
    int SkippedFileCount);

public sealed class WorkspaceSearchService
{
    private const long DefaultMaxFileSizeBytes = 2_000_000;
    private const int DefaultBinaryProbeBytes = 4096;
    private const int ProgressReportInterval = 24;

    private readonly HashSet<string> _ignoredDirectoryNames;
    private readonly HashSet<string> _searchableExtensions;
    private readonly int _maxParallelism;
    private readonly long _maxFileSizeBytes;
    private readonly int _binaryProbeBytes;

    public WorkspaceSearchService(
        IEnumerable<string> ignoredDirectoryNames,
        IEnumerable<string> searchableExtensions,
        int? maxParallelism = null,
        long maxFileSizeBytes = DefaultMaxFileSizeBytes,
        int binaryProbeBytes = DefaultBinaryProbeBytes)
    {
        _ignoredDirectoryNames = new HashSet<string>(ignoredDirectoryNames, StringComparer.OrdinalIgnoreCase);
        _searchableExtensions = new HashSet<string>(searchableExtensions, StringComparer.OrdinalIgnoreCase);
        _maxParallelism = Math.Max(1, maxParallelism ?? Math.Min(Environment.ProcessorCount, 8));
        _maxFileSizeBytes = Math.Max(1, maxFileSizeBytes);
        _binaryProbeBytes = Math.Max(128, binaryProbeBytes);
    }

    public Task<WorkspaceSearchResult> SearchAsync(
        string rootPath,
        Regex regex,
        int maxMatches,
        Action<WorkspaceSearchProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SearchCore(rootPath, regex, maxMatches, onProgress, cancellationToken), cancellationToken);
    }

    public Task<WorkspaceReplaceResult> ReplaceAsync(
        string rootPath,
        Regex regex,
        string replacement,
        Action<WorkspaceReplaceProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReplaceCore(rootPath, regex, replacement, onProgress, cancellationToken), cancellationToken);
    }

    private WorkspaceSearchResult SearchCore(
        string rootPath,
        Regex regex,
        int maxMatches,
        Action<WorkspaceSearchProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        maxMatches = Math.Max(1, maxMatches);

        var matches = new ConcurrentBag<WorkspaceSearchMatch>();
        var matchedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;
        var skipped = 0;
        var matchedFileCount = 0;
        var matchReservations = 0;
        var hitLimit = 0;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            Parallel.ForEach(
                EnumerateWorkspaceFiles(rootPath, linkedCts.Token),
                new ParallelOptions
                {
                    CancellationToken = linkedCts.Token,
                    MaxDegreeOfParallelism = _maxParallelism,
                },
                file =>
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    var scannedCount = Interlocked.Increment(ref scanned);

                    if (!ShouldProcessFile(file))
                    {
                        var skippedCount = Interlocked.Increment(ref skipped);
                        ReportSearchProgress(
                            onProgress,
                            scannedCount,
                            Math.Min(Volatile.Read(ref matchReservations), maxMatches),
                            Volatile.Read(ref matchedFileCount),
                            skippedCount,
                            Volatile.Read(ref hitLimit) == 1,
                            file);
                        return;
                    }

                    var localMatches = new List<WorkspaceSearchMatch>();
                    foreach (var match in ScanFile(file, rootPath, regex, linkedCts.Token))
                    {
                        var reservation = Interlocked.Increment(ref matchReservations);
                        if (reservation > maxMatches)
                        {
                            Interlocked.Exchange(ref hitLimit, 1);
                            linkedCts.Cancel();
                            break;
                        }

                        localMatches.Add(match);
                    }

                    if (localMatches.Count > 0)
                    {
                        foreach (var match in localMatches)
                        {
                            matches.Add(match);
                        }

                        if (matchedFiles.TryAdd(file, 0))
                        {
                            Interlocked.Increment(ref matchedFileCount);
                        }
                    }

                    ReportSearchProgress(
                        onProgress,
                        scannedCount,
                        Math.Min(Volatile.Read(ref matchReservations), maxMatches),
                        Volatile.Read(ref matchedFileCount),
                        Volatile.Read(ref skipped),
                        Volatile.Read(ref hitLimit) == 1,
                        file);
                });
        }
        catch (OperationCanceledException) when (Volatile.Read(ref hitLimit) == 1 && !cancellationToken.IsCancellationRequested)
        {
            // Match limit reached.
        }

        var orderedMatches = matches
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.Length)
            .ToList();

        onProgress?.Invoke(new WorkspaceSearchProgress(
            scanned,
            orderedMatches.Count,
            matchedFileCount,
            Volatile.Read(ref hitLimit) == 1,
            null,
            skipped));

        return new WorkspaceSearchResult(
            scanned,
            orderedMatches,
            matchedFileCount,
            Volatile.Read(ref hitLimit) == 1,
            skipped);
    }

    private WorkspaceReplaceResult ReplaceCore(
        string rootPath,
        Regex regex,
        string replacement,
        Action<WorkspaceReplaceProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var scanned = 0;
        var skipped = 0;
        var filesChanged = 0;
        var replacements = 0;
        var changedFiles = new ConcurrentBag<string>();

        Parallel.ForEach(
            EnumerateWorkspaceFiles(rootPath, cancellationToken),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _maxParallelism,
            },
            file =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var scannedCount = Interlocked.Increment(ref scanned);

                if (!ShouldProcessFile(file))
                {
                    var skippedCount = Interlocked.Increment(ref skipped);
                    ReportReplaceProgress(onProgress, scannedCount, Volatile.Read(ref filesChanged), Volatile.Read(ref replacements), skippedCount, file);
                    return;
                }

                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    ReportReplaceProgress(onProgress, scannedCount, Volatile.Read(ref filesChanged), Volatile.Read(ref replacements), Volatile.Read(ref skipped), file);
                    return;
                }

                var localCount = 0;
                var updated = regex.Replace(text, _ =>
                {
                    localCount++;
                    return replacement;
                });

                if (localCount > 0 && !string.Equals(updated, text, StringComparison.Ordinal))
                {
                    try
                    {
                        File.WriteAllText(file, updated);
                        Interlocked.Increment(ref filesChanged);
                        Interlocked.Add(ref replacements, localCount);
                        changedFiles.Add(file);
                    }
                    catch
                    {
                        // Ignore locked or unwritable files.
                    }
                }

                ReportReplaceProgress(onProgress, scannedCount, Volatile.Read(ref filesChanged), Volatile.Read(ref replacements), Volatile.Read(ref skipped), file);
            });

        var orderedChangedFiles = changedFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        onProgress?.Invoke(new WorkspaceReplaceProgress(scanned, filesChanged, replacements, null, skipped));
        return new WorkspaceReplaceResult(scanned, filesChanged, replacements, orderedChangedFiles, skipped);
    }

    private IEnumerable<WorkspaceSearchMatch> ScanFile(
        string filePath,
        string rootPath,
        Regex regex,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(filePath);
        }
        catch
        {
            yield break;
        }

        var lineNumber = 0;
        foreach (var raw in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            foreach (Match match in regex.Matches(raw))
            {
                if (!match.Success)
                {
                    continue;
                }

                yield return new WorkspaceSearchMatch(
                    filePath,
                    ToRelativePath(rootPath, filePath),
                    lineNumber,
                    match.Index + 1,
                    Math.Max(1, match.Length),
                    raw.Trim());
            }
        }
    }

    private IEnumerable<string> EnumerateWorkspaceFiles(string rootPath, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = stack.Pop();
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (_ignoredDirectoryNames.Contains(name))
                {
                    continue;
                }

                stack.Push(directory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsSearchableFile(file))
                {
                    yield return file;
                }
            }
        }
    }

    private bool IsSearchableFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext))
        {
            return _searchableExtensions.Contains(ext);
        }

        var fileName = Path.GetFileName(filePath);
        return fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals(".env", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldProcessFile(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length > _maxFileSizeBytes)
            {
                return false;
            }

            var probeLength = (int)Math.Min(fileInfo.Length, _binaryProbeBytes);
            if (probeLength <= 0)
            {
                return true;
            }

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[probeLength];
            var bytesRead = stream.Read(buffer, 0, probeLength);
            for (var index = 0; index < bytesRead; index++)
            {
                if (buffer[index] == 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldReportProgress(int scannedFileCount, bool force = false)
        => force || scannedFileCount == 1 || scannedFileCount % ProgressReportInterval == 0;

    private void ReportSearchProgress(
        Action<WorkspaceSearchProgress>? onProgress,
        int scannedCount,
        int matchCount,
        int matchedFileCount,
        int skippedFileCount,
        bool hitLimit,
        string currentFile)
    {
        if (!ShouldReportProgress(scannedCount, hitLimit))
        {
            return;
        }

        onProgress?.Invoke(new WorkspaceSearchProgress(
            scannedCount,
            matchCount,
            matchedFileCount,
            hitLimit,
            currentFile,
            skippedFileCount));
    }

    private void ReportReplaceProgress(
        Action<WorkspaceReplaceProgress>? onProgress,
        int scannedCount,
        int filesChanged,
        int replacementCount,
        int skippedFileCount,
        string currentFile)
    {
        if (!ShouldReportProgress(scannedCount))
        {
            return;
        }

        onProgress?.Invoke(new WorkspaceReplaceProgress(
            scannedCount,
            filesChanged,
            replacementCount,
            currentFile,
            skippedFileCount));
    }

    private static string ToRelativePath(string rootPath, string fullPath)
    {
        try
        {
            return Path.GetRelativePath(rootPath, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }
}

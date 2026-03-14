using System;
using System.Collections.Generic;
using System.IO;
using NotepadSharp.Core;

namespace NotepadSharp.App.Services;

public sealed record ExternalDocumentChange(
    TextDocument Document,
    string FilePath,
    DateTimeOffset CurrentWriteTimeUtc);

public static class ExternalDocumentChangeLogic
{
    public static bool DetectMissing(
        TextDocument document,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var filePath = document.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        fileExists ??= File.Exists;
        return !fileExists(filePath);
    }

    public static ExternalDocumentChange? Detect(
        TextDocument document,
        Func<string, bool>? fileExists = null,
        Func<string, DateTimeOffset>? getLastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var filePath = document.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || document.FileLastWriteTimeUtc is null)
        {
            return null;
        }

        fileExists ??= File.Exists;
        getLastWriteTimeUtc ??= GetFileLastWriteTimeUtc;

        if (!fileExists(filePath))
        {
            return null;
        }

        var currentWriteTimeUtc = getLastWriteTimeUtc(filePath);
        if (document.FileLastWriteTimeUtc.Value.UtcDateTime == currentWriteTimeUtc.UtcDateTime)
        {
            return null;
        }

        return new ExternalDocumentChange(document, filePath, currentWriteTimeUtc);
    }

    public static IReadOnlyList<ExternalDocumentChange> DetectAll(
        IEnumerable<TextDocument> documents,
        Func<string, bool>? fileExists = null,
        Func<string, DateTimeOffset>? getLastWriteTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var changes = new List<ExternalDocumentChange>();
        foreach (var document in documents)
        {
            var change = Detect(document, fileExists, getLastWriteTimeUtc);
            if (change is not null)
            {
                changes.Add(change);
            }
        }

        return changes;
    }

    private static DateTimeOffset GetFileLastWriteTimeUtc(string filePath)
        => new(DateTime.SpecifyKind(File.GetLastWriteTimeUtc(filePath), DateTimeKind.Utc));
}

using System;
using System.Linq;
using NotepadSharp.App.Services;
using NotepadSharp.Core;

namespace NotepadSharp.App.Tests;

public class ExternalDocumentChangeLogicRegressionTests
{
    [Fact]
    public void Detect_ReturnsNull_WhenDocumentHasNoTrackedFile()
    {
        var document = TextDocument.CreateNew();

        var change = ExternalDocumentChangeLogic.Detect(
            document,
            _ => true,
            _ => DateTimeOffset.UtcNow);

        Assert.Null(change);
    }

    [Fact]
    public void DetectMissing_ReturnsTrue_WhenTrackedFileNoLongerExists()
    {
        var document = TextDocument.CreateNew();
        document.FilePath = "/repo/missing.cs";

        var missing = ExternalDocumentChangeLogic.DetectMissing(document, _ => false);

        Assert.True(missing);
    }

    [Fact]
    public void Detect_ReturnsNull_WhenDiskTimestampMatchesTrackedTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero);
        var document = TextDocument.CreateNew();
        document.FilePath = "/repo/Program.cs";
        document.SetFileLastWriteTimeUtc(timestamp);

        var change = ExternalDocumentChangeLogic.Detect(
            document,
            _ => true,
            _ => timestamp);

        Assert.Null(change);
    }

    [Fact]
    public void Detect_ReturnsChange_WhenDiskTimestampDiffers()
    {
        var trackedTimestamp = new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero);
        var currentTimestamp = trackedTimestamp.AddMinutes(2);
        var document = TextDocument.CreateNew();
        document.FilePath = "/repo/Program.cs";
        document.SetFileLastWriteTimeUtc(trackedTimestamp);

        var change = ExternalDocumentChangeLogic.Detect(
            document,
            _ => true,
            _ => currentTimestamp);

        Assert.NotNull(change);
        Assert.Same(document, change!.Document);
        Assert.Equal("/repo/Program.cs", change.FilePath);
        Assert.Equal(currentTimestamp, change.CurrentWriteTimeUtc);
    }

    [Fact]
    public void DetectAll_ReturnsOnlyDocumentsWithExternalChanges()
    {
        var trackedTimestamp = new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero);
        var changedTimestamp = trackedTimestamp.AddMinutes(1);

        var unchanged = TextDocument.CreateNew();
        unchanged.FilePath = "/repo/stable.cs";
        unchanged.SetFileLastWriteTimeUtc(trackedTimestamp);

        var changed = TextDocument.CreateNew();
        changed.FilePath = "/repo/changed.cs";
        changed.SetFileLastWriteTimeUtc(trackedTimestamp);

        var unstamped = TextDocument.CreateNew();
        unstamped.FilePath = "/repo/unstamped.cs";

        var changes = ExternalDocumentChangeLogic.DetectAll(
            new[] { unchanged, changed, unstamped },
            _ => true,
            path => path.EndsWith("changed.cs", StringComparison.Ordinal) ? changedTimestamp : trackedTimestamp);

        var change = Assert.Single(changes);
        Assert.Same(changed, change.Document);
    }
}

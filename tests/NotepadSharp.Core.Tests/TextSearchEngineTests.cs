using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NotepadSharp.Core.Tests;

public class TextSearchEngineTests
{
    [Fact]
    public void FindMatchInRange_PlainTextForward_WithWrapAround()
    {
        const string text = "alpha beta alpha";

        var match = TextSearchEngine.FindMatchInRange(
            text,
            "alpha",
            startIndex: text.Length,
            forward: true,
            matchCase: true,
            wholeWord: true,
            useRegex: false,
            wrapAround: true,
            rangeStart: 0,
            rangeEnd: text.Length);

        Assert.NotNull(match);
        Assert.Equal((0, 5), match!.Value);
    }

    [Fact]
    public void FindMatchInRange_RegexBackward_RespectsRange()
    {
        const string text = "one\ntwo\nthree\nfour";

        var match = TextSearchEngine.FindMatchInRange(
            text,
            "t\\w+",
            startIndex: text.Length,
            forward: false,
            matchCase: false,
            wholeWord: false,
            useRegex: true,
            wrapAround: false,
            rangeStart: 0,
            rangeEnd: text.IndexOf("four", StringComparison.Ordinal));

        Assert.NotNull(match);
        Assert.Equal(text.IndexOf("three", StringComparison.Ordinal), match!.Value.index);
    }

    [Fact]
    public void CountMatchesInRange_WholeWord_WorksCaseInsensitive()
    {
        const string text = "apple APPLE pineapple Apple";

        var count = TextSearchEngine.CountMatchesInRange(
            text,
            "apple",
            matchCase: false,
            wholeWord: true,
            useRegex: false,
            rangeStart: 0,
            rangeEnd: text.Length);

        Assert.Equal(3, count);
    }

    [Fact]
    public void TryCreateRegex_InvalidPattern_ReturnsNull()
    {
        var regex = TextSearchEngine.TryCreateRegex("(", matchCase: true, wholeWord: false);
        Assert.Null(regex);
    }

    [Theory]
    [InlineData("alpha_beta", 0, 5, false)]
    [InlineData("alpha beta", 0, 5, true)]
    [InlineData("(alpha)", 1, 5, true)]
    public void IsWholeWordAt_DetectsWordBoundaries(string text, int index, int length, bool expected)
        => Assert.Equal(expected, TextSearchEngine.IsWholeWordAt(text, index, length));
}

public class TextDocumentFileServiceSaveToFileTests
{
    [Fact]
    public async Task SaveToFileAsync_UpdatesFileTimestampAndClearsDirtyFlag()
    {
        var service = new TextDocumentFileService();
        var doc = TextDocument.CreateNew();
        doc.Text = "hello";
        doc.Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        doc.HasBom = false;
        doc.PreferredLineEnding = LineEnding.Lf;

        var path = Path.Combine(Path.GetTempPath(), $"notepadsharp-save-{Guid.NewGuid():N}.txt");

        try
        {
            await service.SaveToFileAsync(doc, path);

            Assert.False(doc.IsDirty);
            Assert.NotNull(doc.FileLastWriteTimeUtc);
            Assert.True(File.Exists(path));
            Assert.Equal("hello\n", await File.ReadAllTextAsync(path));
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup for test temp file.
            }
        }
    }
}

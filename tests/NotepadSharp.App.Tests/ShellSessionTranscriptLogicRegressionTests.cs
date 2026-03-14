using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class ShellSessionTranscriptLogicRegressionTests
{
    [Fact]
    public void NormalizeChunk_PreservesTerminalControlsWhileRemovingNullBytes()
    {
        var normalized = ShellSessionTranscriptLogic.NormalizeChunk("\u001b[31merror\u001b[0m\0\r\n");

        Assert.Equal("\u001b[31merror\u001b[0m\n", normalized);
    }

    [Fact]
    public void AppendChunk_RewritesCurrentLineWithCarriageReturnAndEraseLine()
    {
        var appended = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: string.Empty,
            currentCursor: 0,
            pendingControlSequence: string.Empty,
            currentInAlternateScreen: false,
            savedCursor: null,
            nextChunk: "Downloading 10%\r\u001b[2KDone\n",
            maxChars: 200);

        Assert.Equal("Done\n", appended.Text);
        Assert.Equal(5, appended.Cursor);
        Assert.Equal(string.Empty, appended.PendingControlSequence);
        Assert.False(appended.InAlternateScreen);
    }

    [Fact]
    public void AppendChunk_ClearsTranscriptForClearScreenSequence()
    {
        var appended = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: "alpha\nbeta\n",
            currentCursor: "alpha\nbeta\n".Length,
            pendingControlSequence: string.Empty,
            currentInAlternateScreen: false,
            savedCursor: null,
            nextChunk: "\u001b[2J\u001b[Hgamma\n",
            maxChars: 200);

        Assert.Equal("gamma\n", appended.Text);
        Assert.Equal(6, appended.Cursor);
    }

    [Fact]
    public void AppendChunk_PreservesPendingControlSequenceAcrossChunks()
    {
        var first = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: string.Empty,
            currentCursor: 0,
            pendingControlSequence: string.Empty,
            currentInAlternateScreen: false,
            savedCursor: null,
            nextChunk: "Downloading 10%\r\u001b[2",
            maxChars: 200);
        var second = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: first.Text,
            currentCursor: first.Cursor,
            pendingControlSequence: first.PendingControlSequence,
            currentInAlternateScreen: first.InAlternateScreen,
            savedCursor: first.SavedCursor,
            nextChunk: "KDone\n",
            maxChars: 200);

        Assert.Equal("Downloading 10%", first.Text);
        Assert.Equal(0, first.Cursor);
        Assert.Equal("\u001b[2", first.PendingControlSequence);
        Assert.Equal("Done\n", second.Text);
        Assert.Equal(5, second.Cursor);
        Assert.Equal(string.Empty, second.PendingControlSequence);
    }

    [Fact]
    public void AppendChunk_RendersAlternateScreenOutputInline()
    {
        var entered = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: "before\n",
            currentCursor: "before\n".Length,
            pendingControlSequence: string.Empty,
            currentInAlternateScreen: false,
            savedCursor: null,
            nextChunk: "\u001b[?1049h\u001b[2J\u001b[Hmain menu",
            maxChars: 400);
        var running = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: entered.Text,
            currentCursor: entered.Cursor,
            pendingControlSequence: entered.PendingControlSequence,
            currentInAlternateScreen: entered.InAlternateScreen,
            savedCursor: entered.SavedCursor,
            nextChunk: "\u001b[2;5Hitem",
            maxChars: 400);
        var exited = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: running.Text,
            currentCursor: running.Cursor,
            pendingControlSequence: running.PendingControlSequence,
            currentInAlternateScreen: running.InAlternateScreen,
            savedCursor: running.SavedCursor,
            nextChunk: "\u001b[?1049lafter\n",
            maxChars: 400);

        Assert.Equal("main menu", entered.Text);
        Assert.True(entered.InAlternateScreen);
        Assert.Equal("main menu\n    item", running.Text);
        Assert.True(running.InAlternateScreen);
        Assert.False(exited.InAlternateScreen);
        Assert.Equal("main menu\n    item\nafter\n", exited.Text);
    }

    [Fact]
    public void AppendChunk_TrimsTranscriptWhenOverLimit()
    {
        var transcript = ShellSessionTranscriptLogic.AppendChunk("0123456789ABCDEFGHIJ", "abcdefghij", maxChars: 25);

        Assert.StartsWith("[output truncated]\n", transcript);
        Assert.EndsWith("efghij", transcript);
    }

    [Fact]
    public void AppendChunk_RewritesPreviousLineWithCursorUp()
    {
        var appended = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: "alpha\nbeta\n",
            currentCursor: "alpha\nbeta\n".Length,
            pendingControlSequence: string.Empty,
            currentInAlternateScreen: false,
            savedCursor: null,
            nextChunk: "\u001b[1A\u001b[2Kdone\n",
            maxChars: 200);

        Assert.Equal("alpha\ndone\n", appended.Text);
    }

    [Fact]
    public void AppendChunk_SavesAndRestoresCursorAcrossChunks()
    {
        var first = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: "count: 10\n",
            currentCursor: "count: 10\n".Length,
            pendingControlSequence: string.Empty,
            currentInAlternateScreen: false,
            savedCursor: null,
            nextChunk: "\u001b[1F\u001b[s",
            maxChars: 200);
        var second = ShellSessionTranscriptLogic.AppendChunk(
            currentTranscript: first.Text,
            currentCursor: first.Cursor,
            pendingControlSequence: first.PendingControlSequence,
            currentInAlternateScreen: first.InAlternateScreen,
            savedCursor: first.SavedCursor,
            nextChunk: "\u001b[8Greached\u001b[u\u001b[2Kcount: 11\n",
            maxChars: 200);

        Assert.Equal("count: 11\n", second.Text);
        Assert.NotNull(second.SavedCursor);
    }
}

using NotepadSharp.App.Services;

namespace NotepadSharp.App.Tests;

public class OpenDocumentPathRenameLogicRegressionTests
{
    [Fact]
    public void TryRemapPath_RemapsExactFileRename()
    {
        var remapped = OpenDocumentPathRenameLogic.TryRemapPath(
            "/workspace/src/Program.cs",
            "/workspace/src/Program.cs",
            "/workspace/src/App.cs",
            oldPathIsDirectory: false);

        Assert.Equal("/workspace/src/App.cs", remapped);
    }

    [Fact]
    public void TryRemapPath_RemapsChildPathsForDirectoryRename()
    {
        var remapped = OpenDocumentPathRenameLogic.TryRemapPath(
            "/workspace/src/Features/Editor/Main.cs",
            "/workspace/src/Features",
            "/workspace/src/Modules",
            oldPathIsDirectory: true);

        Assert.Equal("/workspace/src/Modules/Editor/Main.cs", remapped?.Replace('\\', '/'));
    }

    [Fact]
    public void TryRemapPath_ReturnsNull_WhenPathIsOutsideRenamedDirectory()
    {
        var remapped = OpenDocumentPathRenameLogic.TryRemapPath(
            "/workspace/tests/Main.cs",
            "/workspace/src",
            "/workspace/modules",
            oldPathIsDirectory: true);

        Assert.Null(remapped);
    }
}

using System;
using System.IO;
using System.Linq;

namespace NotepadSharp.App.Services;

public static class WorkspaceWatcherLogic
{
    public static bool ShouldIgnorePath(string workspaceRoot, string? changedPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(changedPath))
        {
            return true;
        }

        string fullRoot;
        string fullPath;
        try
        {
            fullRoot = Path.GetFullPath(workspaceRoot);
            fullPath = Path.GetFullPath(changedPath);
        }
        catch
        {
            return true;
        }

        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var relativePath = Path.GetRelativePath(fullRoot, fullPath);
        if (string.IsNullOrWhiteSpace(relativePath)
            || string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment => string.Equals(segment, ".DS_Store", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return segments.Any(segment => segment is ".git" or ".vs" or ".idea" or "node_modules" or "bin" or "obj");
    }
}

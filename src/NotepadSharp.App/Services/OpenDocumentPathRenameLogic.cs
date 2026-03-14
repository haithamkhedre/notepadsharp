using System;
using System.IO;

namespace NotepadSharp.App.Services;

public static class OpenDocumentPathRenameLogic
{
    public static string? TryRemapPath(string? currentPath, string? oldPath, string? newPath, bool oldPathIsDirectory)
    {
        if (string.IsNullOrWhiteSpace(currentPath)
            || string.IsNullOrWhiteSpace(oldPath)
            || string.IsNullOrWhiteSpace(newPath))
        {
            return null;
        }

        string normalizedCurrentPath;
        string normalizedOldPath;
        string normalizedNewPath;
        try
        {
            normalizedCurrentPath = Path.GetFullPath(currentPath);
            normalizedOldPath = Path.GetFullPath(oldPath);
            normalizedNewPath = Path.GetFullPath(newPath);
        }
        catch
        {
            return null;
        }

        if (!oldPathIsDirectory)
        {
            return string.Equals(normalizedCurrentPath, normalizedOldPath, StringComparison.OrdinalIgnoreCase)
                ? normalizedNewPath
                : null;
        }

        var oldPrefix = normalizedOldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!normalizedCurrentPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativePath = normalizedCurrentPath[oldPrefix.Length..];
        return Path.Combine(normalizedNewPath, relativePath);
    }
}

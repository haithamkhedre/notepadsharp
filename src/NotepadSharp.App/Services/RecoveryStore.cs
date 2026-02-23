using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadSharp.App.Services;

public sealed class RecoveryStore
{
    private readonly string _recoveryDir;

    public RecoveryStore(string appName = "NotepadSharp")
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(baseDir, appName, "recovery");
        Directory.CreateDirectory(dir);
        _recoveryDir = dir;
    }

    public IReadOnlyList<string> ListSnapshotFiles()
    {
        try
        {
            if (!Directory.Exists(_recoveryDir))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(_recoveryDir, "*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<RecoverySnapshot?> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RecoverySnapshot>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(RecoverySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var path = GetSnapshotPath(snapshot.DocumentId);
        var temp = path + ".tmp." + Guid.NewGuid().ToString("N");

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = false,
        });

        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);

        if (File.Exists(path))
        {
            File.Move(temp, path, overwrite: true);
        }
        else
        {
            File.Move(temp, path);
        }

        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    public void Delete(Guid documentId)
    {
        try
        {
            var path = GetSnapshotPath(documentId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore.
        }
    }

    public void DeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore.
        }
    }

    private string GetSnapshotPath(Guid documentId)
        => Path.Combine(_recoveryDir, documentId.ToString("N") + ".json");
}

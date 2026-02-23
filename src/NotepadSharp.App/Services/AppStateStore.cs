using System;
using System.IO;
using System.Text.Json;

namespace NotepadSharp.App.Services;

public sealed class AppStateStore
{
    private readonly string _stateFilePath;

    public AppStateStore(string appName = "NotepadSharp")
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(baseDir, appName);
        Directory.CreateDirectory(dir);
        _stateFilePath = Path.Combine(dir, "state.json");
    }

    public AppState Load()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new AppState();
            }

            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public void Save(AppState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        File.WriteAllText(_stateFilePath, json);
    }
}

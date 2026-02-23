using System.Collections.Generic;

namespace NotepadSharp.App.Services;

public sealed class AppState
{
    public List<string> RecentFiles { get; set; } = new();
    public List<string> LastSessionFiles { get; set; } = new();
}

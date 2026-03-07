using System.Collections.Generic;

namespace NotepadSharp.App.Services;

public sealed class AppState
{
    public List<string> RecentFiles { get; set; } = new();
    public List<string> LastSessionFiles { get; set; } = new();
    public string Theme { get; set; } = "Dark+";
    public bool ShowMiniMap { get; set; } = true;
    public bool SplitViewEnabled { get; set; }
    public bool FoldingEnabled { get; set; } = true;
    public bool ColumnGuideEnabled { get; set; } = true;
    public int ColumnGuideColumn { get; set; } = 100;
}

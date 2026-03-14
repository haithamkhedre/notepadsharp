using System.Collections.Generic;

namespace NotepadSharp.App.Services;

public sealed class AppState
{
    public List<string> RecentFiles { get; set; } = new();
    public List<string> LastSessionFiles { get; set; } = new();
    public string Theme { get; set; } = "Dark+";
    public string LanguageMode { get; set; } = "Auto";
    public string? WorkspaceRoot { get; set; }
    public string SidebarSection { get; set; } = "Explorer";
    public bool SidebarAutoHide { get; set; }
    public bool SidebarExpanded { get; set; } = true;
    public bool ShowMiniMap { get; set; } = true;
    public bool SplitViewEnabled { get; set; }
    public string SplitCompareMode { get; set; } = "Show all";
    public bool FoldingEnabled { get; set; } = true;
    public bool ShowAllCharacters { get; set; }
    public bool ColumnGuideEnabled { get; set; } = true;
    public int ColumnGuideColumn { get; set; } = 100;
    public double SidebarWidth { get; set; } = 340;
    public bool TerminalVisible { get; set; }
    public double TerminalHeight { get; set; } = 180;
    public List<string> TerminalCommandHistory { get; set; } = new();
    public bool ShowTabBar { get; set; } = true;
    public bool AutoHideTabBar { get; set; }
    public double EditorFontSize { get; set; } = 16;
    public string EditorFontFamily { get; set; } = "Consolas";
    public bool AiProviderEnabled { get; set; }
    public string AiProviderEndpoint { get; set; } = AiProviderConfigLogic.DefaultEndpoint;
    public string AiProviderModel { get; set; } = AiProviderConfigLogic.DefaultModel;
    public string AiProviderApiKeyEnvironmentVariable { get; set; } = AiProviderConfigLogic.DefaultApiKeyEnvironmentVariable;
}

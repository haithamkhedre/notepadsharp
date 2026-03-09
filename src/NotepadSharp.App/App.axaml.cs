using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using System;

namespace NotepadSharp.App;

public partial class App : Application
{
    public override void Initialize()
    {
        Name = "Notepad#";
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();

            try
            {
                using var iconStream = AssetLoader.Open(new Uri("avares://NotepadSharp/Assets/notepadsharp-icon-white.png"));
                mainWindow.Icon = new WindowIcon(iconStream);
            }
            catch
            {
                // Fall back to the XAML-defined icon if resource loading fails.
            }

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

namespace NotepadSharp.App.Dialogs;

public sealed class PaletteItem
{
    public PaletteItem(string id, string title, string? description = null)
    {
        Id = id;
        Title = title;
        Description = description ?? string.Empty;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public string SearchText => $"{Title} {Description}";
}

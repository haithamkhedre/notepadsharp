# NotepadSharp

NotepadSharp (Notepad#) is a fast, cross-platform text editor built with **.NET + Avalonia**.

## Goals
- Be a great **Notepad replacement** on Windows, macOS, and Linux
- Open/save large files quickly and safely
- Handle encodings and line endings correctly
- Keep the core reliable, with room for power features later

## Build
Requirements:
- .NET SDK (see `global.json`) — currently **.NET 10**

If you installed .NET into `~/.dotnet`, make sure your shell uses it:
- `export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"`

Commands:
- `dotnet restore`
- `dotnet build -c Release`
- `dotnet test -c Release`

## Run (desktop)
- `dotnet run --project src/NotepadSharp.App/NotepadSharp.App.csproj`

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md).

## Security
See [SECURITY.md](SECURITY.md).

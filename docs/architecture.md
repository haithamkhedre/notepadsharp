# NotepadSharp Architecture

This document defines code ownership boundaries after the `MainWindow` refactor. Follow these boundaries to keep the editor maintainable.

## Solution Layout

- `src/NotepadSharp.Core`
Contains model and pure text/file logic (document state, encoding, line endings, text search engine).
- `src/NotepadSharp.App`
Contains Avalonia UI, view models, dialogs, and editor workflows.
- `tests/NotepadSharp.Core.Tests`
Unit tests for pure Core logic.
- `tests/NotepadSharp.App.Tests`
Unit tests for app-side non-visual logic (view model + git status/tree logic).

## MainWindow Partial Map

- `MainWindow.axaml.cs`
Type definition, shared fields, nested records/colorizer, constructor entrypoint only.
- `MainWindow.Startup.cs`
Startup/bootstrap orchestration: component init, event wiring, persisted-state restore, lifecycle handlers.
- `MainWindow.Shell.cs`
Shell-level interactions: sidebar section switching, drag/drop, document selection reactions, save-all/close-all/reopen-tab shell commands.
- `MainWindow.Documents.cs`
Open/save/save-as/close/reopen/recovery/session/open-recent workflows.
- `MainWindow.FindReplace.cs`
Find/replace UX, go-to-line, caret and find summary updates.
- `MainWindow.Editing.cs`
Editor editing commands, duplicate/delete line, bracket pairing, zoom gestures, clipboard, undo/redo.
- `MainWindow.EditorSurface.cs`
Editor rendering behavior: word wrap, split view, minimap, diff gutter overlay, folding, theme surface application.
- `MainWindow.LanguageFormatting.cs`
Language mode selection, syntax highlighting, encoding/line-ending actions, formatting and replace operations.
- `MainWindow.Search.cs`
Search in files + replace in files tree workflows.
- `MainWindow.WorkspaceGit.cs`
Workspace tree, explorer actions, git panel integration and interactions.
- `MainWindow.CommandPalette.cs`
Command palette, quick open, autocomplete command dispatch.
- `MainWindow.DiagnosticsSettings.cs`
Diagnostics updates and settings panel synchronization.

## Service Boundaries

- `TextDocumentFileService` (`Core`)
Single source of truth for loading/reloading/saving text with BOM + line-ending normalization.
- `TextSearchEngine` (`Core`)
Shared search/regex/whole-word matching primitives used by editor find/replace flows.
- `GitStatusLogic` (`App.Services`)
Parses git porcelain XY statuses and builds staged/unstaged tree models.

## Ownership Rules

- Add logic to the most specific partial first. Do not append features to `MainWindow.axaml.cs` unless they are shared fields/type definitions.
- Put pure algorithmic logic in `Core` whenever it can run without Avalonia.
- Keep UI event handlers thin; move reusable logic to service helpers.
- Prefer deterministic, side-effect-light helpers for anything that should be unit-tested.
- For new editor features, add tests in the matching test project before/with implementation.

## Change Checklist

1. Place code in the correct partial/service boundary.
2. Add or update tests for changed behavior.
3. Keep constructor/bootstrap minimal by extending startup methods, not constructor body.
4. Run:
   - `dotnet build --nologo`
   - `dotnet test --nologo`

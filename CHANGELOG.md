# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added
- Workspace explorer with tree view, file/folder create/rename/delete, and drag/drop support.
- Search-in-files with tree-grouped results, regex/case options, and replace preview/replace-all flows.
- Source Control sidebar with grouped `Staged` vs `Changes`, expand/collapse actions, stage/unstage all, and commit dialog.
- Git-aware editor visuals: inline added/modified/deleted line colors and minimap/ruler diff markers.
- Built-in terminal pane with run/clear shortcuts and workspace-aware current directory.
- Editor typography controls in the toolbar (font family and size) plus persisted settings.
- Added `ConfirmDialog` and `TextInputDialog` for reusable prompts.
- Added app icon asset for the desktop window.

### Changed
- Refined top toolbar and status bar controls for language, theme, typography, and whitespace options.
- Improved sidebar organization and compacted Source Control action layout.
- Expanded persisted app state to include editor typography, layout, and terminal preferences.

### Notes
- Build currently reports Avalonia drag/drop API deprecation warnings; behavior is unchanged and still functional.

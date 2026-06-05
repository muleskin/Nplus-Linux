# n+ (NPlus) — Linux edition

A lightweight text and code editor for **Linux** (and Windows/macOS), built in C# on
[Avalonia](https://avaloniaui.net/) with [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)
and [TextMate](https://github.com/danipen/TextMateSharp) syntax highlighting. Inspired by
Notepad++, with a focus on session persistence, a built-in macro engine, and live file tailing.

This is a cross-platform port of the original Windows-only WinForms/Scintilla build. The UI was
rewritten in Avalonia; the editor control (Scintilla) was replaced with AvaloniaEdit + TextMate;
and the read-only hex view replaces the WinForms HexBox. The portable logic (encoding detection,
line/blank operations, sorting, JSON tools, macros, session/settings persistence) was carried over
faithfully. The app targets **.NET 10** and publishes as a self-contained binary that needs no
installed runtime.

## Install (Linux)

Grab a release tarball (or build one — see below), then:

```bash
tar xzf nplus-linux-x64.tar.gz
cd nplus-linux-x64
./install.sh            # per-user install into ~/.local (no root needed)
nplus                   # launch (or find "n+" in your application menu)
```

`install.sh` copies the binary into `~/.local/lib/nplus`, symlinks `~/.local/bin/nplus`, and
installs the `.desktop` entry + icon so n+ shows up in your desktop's app menu and as an
"Open with…" handler for text files.

## Building from source

Requirements:
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (the only prerequisite — Avalonia,
  AvaloniaEdit and the TextMate grammars come in via NuGet).

A `Makefile` wraps the common tasks (run `make help` for the full list):

```bash
make run                 # build & run the editor (dev)
make publish             # self-contained single-file binary -> dist/linux-x64
make package             # publish + assemble dist/nplus-linux-x64.tar.gz
make install             # package, then install per-user into ~/.local
make publish RID=linux-arm64
```

Equivalent commands without make:

```bash
dotnet run --project src/NPlus            # run
packaging/build-linux.sh                  # distributable build + tarball (linux-x64)
packaging/build-linux.sh linux-arm64
```

This publishes a single-file `dist/linux-x64/nplus` (plus the native Skia/HarfBuzz/Oniguruma
`.so` libraries it loads at runtime) and assembles `dist/nplus-linux-x64.tar.gz`.

### Project layout

| Path | What it is |
| --- | --- |
| `src/NPlus/Core/` | Portable, UI-free logic: encoding detect/convert, line & blank transforms, sort modes, JSON, macro model, settings/session/recent-files/macros persistence. |
| `src/NPlus/Views/` | The Avalonia main window, split into partial classes by feature (shell, menu, files, edit, find, macros, json). |
| `src/NPlus/Controls/` | `HexView` (read-only hex dump control). |
| `src/NPlus/Editor/` | AvaloniaEdit helpers: brace folding strategy, bookmark + mark-all background renderers. |
| `src/NPlus/Search/` | The find / replace / mark / count search engine. |
| `src/NPlus/Dialogs/` | Find/Replace tool window and message-box / prompt helpers. |
| `packaging/` | `.desktop` entry, icon, Linux build + install scripts. |
| *(repo root `*.cs`)* | The original Windows WinForms sources, kept as a reference. |

### Dependencies (NuGet)
- **Avalonia** 12 — cross-platform UI framework
- **Avalonia.AvaloniaEdit** + **AvaloniaEdit.TextMate** — the editor control & syntax highlighting (replaces Scintilla)
- **TextMateSharp.Grammars** — bundled grammars/themes for the supported languages
- **System.Text.Json** (in .NET) — JSON formatting / tree view

Config, session snapshots, recent files and macros live under `~/.config/nplus/`.

## Features

### Editing
- **Tabbed multi-document interface** with per-tab color tags and close buttons
- **Syntax highlighting** for C#, C/C++, Java, JavaScript/TypeScript, Python, SQL, Visual Basic, VBScript, PowerShell, PHP, HTML, XML/XAML, JSON, YAML (plus shell, CSS, Markdown), via TextMate grammars
- **Column / block selection** (Alt+drag; toggle with `Ctrl+Alt+A`)
- **Word wrap**, whitespace/EOL visualization, brace folding
- **Light** and **Dark** themes — syntax colors update automatically

### Session & Files
- **Hot exit / session snapshots** — tabs, unsaved changes, window position and zoom restored on next launch
- **Recent files** menu (last 10)
- **Revert to saved**
- **Read-only Hex view** for binary/executable files
- **Read-only fallback** when a file is locked by another process
- **External file change detection** — prompted to reload, keep, or track renamed files
- **Tab save-status dots** — green = saved, amber = unsaved
- **Encoding support** — ANSI (Windows-1252), UTF-8, UTF-8 BOM, UTF-16 BE BOM, UTF-16 LE BOM, with auto-detect and convert-to options

### Macros
- Record keystrokes, navigation and Find/Replace actions
- Playback once, N times, or to end-of-file (great for log processing)
- **Save, load, and edit macros** step-by-step — saved macros persist between sessions

### Find / Replace / Mark / Find in Files
- Normal, Extended (`\n`, `\t`), and Regex modes (`Ctrl+F`, `Ctrl+H`, `Ctrl+B`)
- **Mark** highlights all matches and can drop a bookmark on every matching line
- **Find in Files** (`Ctrl+Shift+F`) across a directory with file-type filters, sub-folder recursion and hidden-folder inclusion
- **Replace in Files** — bulk find-and-replace across matching files on disk
- Results appear in a dockable bottom panel; double-click any hit to open the file and jump to the line
- Per-session search history dropdowns

### Bookmarks & Line Operations
- Toggle bookmarks (`Ctrl+F2`), navigate with `F2` / `Shift+F2`
- Copy, cut, or delete all bookmarked lines; delete non-bookmarked lines; paste-to-bookmarks
- Duplicate, reverse, or randomize line order
- Sort lines: lexicographic, locale, integer, decimal (comma/dot), by length — ascending/descending, case-insensitive options
- Split (`Ctrl+I`), join (`Ctrl+J`), move lines (`Ctrl+Shift+Up/Down`)
- Remove duplicate / empty lines, insert blank lines
- **Blank operations** — trim leading/trailing/both, EOL to space, tabs ↔ spaces

### JSON Tools
- **Pretty-print / format** dense or single-line JSON
- **Visual JSON tree explorer** in a dockable side panel

### Live File Monitoring (Tail)
- Toggle "Live" mode to auto-reload and auto-scroll on external file changes — tail rolling log files without leaving the editor

### Zoom
- `F11` / `F12` / `Ctrl+0` to zoom in / out / reset; zoom level persists between sessions

## Keyboard Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+N` / `Ctrl+O` / `Ctrl+S` | New / Open / Save |
| `Ctrl+Shift+S` | Save As |
| `Ctrl+F` / `Ctrl+H` / `Ctrl+B` | Find / Replace / Mark |
| `F3` | Find Next |
| `Ctrl+Shift+F` | Find in Files |
| `Ctrl+Alt+A` | Toggle column selection mode |
| `Ctrl+D` | Duplicate line |
| `Ctrl+I` / `Ctrl+J` | Split / Join lines |
| `Ctrl+Shift+L` | Delete current line |
| `Ctrl+Shift+Up/Down` | Move line up / down |
| `Ctrl+F2` | Toggle bookmark |
| `F2` / `Shift+F2` | Next / previous bookmark |
| `Ctrl+Shift+P` | Playback active macro |
| `Alt+Shift+S` | Trim trailing space and save |
| `F11` / `F12` / `Ctrl+0` | Zoom in / out / reset |

## License

MIT License — Copyright (c) 2026 Mule Skinner. See source headers for full text.

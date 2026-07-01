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

## Building from source

The only prerequisite is the **.NET 10 SDK** (Avalonia, AvaloniaEdit and the TextMate
grammars come in via NuGet). If you don't have it, install it with the bundled
`dotnet-install.sh` — no root required, it installs to `~/.dotnet`:

```bash
./dotnet-install.sh --channel 10.0
export PATH="$HOME/.dotnet:$PATH"     # add to ~/.bashrc to persist
dotnet --version                      # expect 10.0.x
```

(Or use your distro package: Fedora `sudo dnf install dotnet-sdk-10.0`, Arch
`sudo pacman -S dotnet-sdk`, Ubuntu `sudo apt install dotnet-sdk-10.0`.)

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

### Running on a minimal / headless Linux box

n+ is a GUI app, so it needs a display server (X11/Wayland) and a few X client libraries.
Full desktops already have these; "lite"/minimal Ubuntu/Debian installs usually don't, and
the window simply won't appear. An optional helper installs what's missing:

```bash
./packaging/install-gui-deps.sh          # X server + window manager + the X11/Skia libraries
./packaging/install-gui-deps.sh --libs   # only the runtime libraries (you already have a desktop)
```

Then start a session with `startx` (or log into your desktop) and run `nplus`. On a truly
headless box, use SSH X-forwarding (`ssh -X`) or a VNC desktop instead.

### Project layout

| Path | What it is |
| --- | --- |
| `src/NPlus/Core/` | Portable, UI-free logic: encoding detect/convert, line & blank transforms, sort modes, JSON, macro model, settings/session/recent-files/macros persistence. |
| `src/NPlus/Views/` | The Avalonia main window, split into partial classes by feature (shell, menu, files, edit, find, macros, json). |
| `src/NPlus/Controls/` | `HexView` (read-only hex dump control). |
| `src/NPlus/Editor/` | AvaloniaEdit helpers: brace folding strategy, bookmark + mark-all background renderers. |
| `src/NPlus/Search/` | The find / replace / mark / count search engine. |
| `src/NPlus/Scripting/` | Lua scripting host (MoonSharp), the `editor` API bridge, and starter-script seeding. |
| `src/NPlus/Scripts/` | Bundled starter `.lua` scripts (embedded; seeded into `~/.config/nplus/scripts/`). |
| `src/NPlus/Ai/` | Optional AI assistant: provider metadata, the multi-provider HTTP client (complete + stream + test), chat message model, and the agent action-protocol runner. |
| `src/NPlus/Dialogs/` | Find/Replace tool window, AI settings dialog, and message-box / prompt helpers. |
| `packaging/` | `.desktop` entry, icon, Linux build + install scripts. |
| *(repo root `*.cs`)* | The original Windows WinForms sources, kept as a reference. |

### Dependencies (NuGet)
- **Avalonia** 12 — cross-platform UI framework
- **Avalonia.AvaloniaEdit** + **AvaloniaEdit.TextMate** — the editor control & syntax highlighting (replaces Scintilla)
- **TextMateSharp.Grammars** — bundled grammars/themes for the supported languages
- **MoonSharp** — managed Lua interpreter powering the built-in scripting engine (pure C#, no native dependency, so the self-contained single-file build is unaffected)
- **System.Text.Json** (in .NET) — JSON formatting / tree view, and AI settings/wire-format (de)serialization
- **System.Net.Http** (in .NET) — the AI assistant's provider client; no extra SDK is pulled in for any backend, each provider is spoken to over plain HTTPS

Config, session snapshots, recent files, macros and Lua scripts live under `~/.config/nplus/`
(scripts in `~/.config/nplus/scripts/`, AI provider settings in `~/.config/nplus/ai.json`).

## Features

### Editing
- **Tabbed multi-document interface** with per-tab color tags and close buttons
- **Syntax highlighting** for C#, C/C++, Java, JavaScript/TypeScript, Python, SQL, Visual Basic, VBScript, PowerShell, PHP, HTML, XML/XAML, JSON, YAML (plus shell, CSS, Markdown), via TextMate grammars
- **Column / block selection** (Alt+drag; toggle with `Ctrl+Alt+A`)
- **Word wrap**, whitespace/EOL visualization, brace folding
- **Multi-line tabs** — wrap tab headers across multiple rows so every open file stays visible; toggle from the **View** menu or the 🗂 toolbar button (persists between sessions)
- **Light** and **Dark** themes — syntax colors update automatically

### Session & Files
- **Hot exit / session snapshots** — tabs, unsaved changes, window position and zoom restored on next launch, with keyboard focus returned to the tab that was active when you last closed
- **Recent files** menu (last 10)
- **Revert to saved**
- **Large-file mode** — text files over ~16 MB load on a background thread (no UI freeze) and open read-only with syntax highlighting, folding, current-line highlight and word-wrap turned off, so multi-hundred-MB files stay responsive to scroll and search. The tab and status bar show a *LARGE — read-only* indicator. (A confirmation prompt still appears past ~384 MB, where a full in-memory copy risks exhausting memory.)
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
- **Find All in All Tabs** — search every open tab at once; hits are listed in the results panel, double-click to jump straight to the tab and line (unsaved edits included)
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

### Lua Scripting
- **Built-in Lua scripting engine** (MoonSharp) under **Tools ▸ Scripting (Lua)** — automate edits the macro recorder can't express
- **Run the active document** as a script (`F5`), **run any `.lua` file**, or pick from your **scripts folder** (`~/.config/nplus/scripts/`, listed live in the menu)
- A small, stable **`editor` API** gives scripts the buffer and caret:
  `editor.text()` / `set_text(s)`, `selection()` / `replace_selection(s)`, `insert(s)`,
  `lines()` / `set_lines(t)`, `line_count()`, `caret_line()`, `file_path()`, `language()`
- `print(...)` output is captured and shown after the run; every script's edits collapse into a **single undo step**
- **Sandboxed** — the string/table/math/`os`(time) libraries are available, but file I/O, `os.execute` and `loadfile`/`dofile` are disabled, so a script can transform the buffer but can't touch the filesystem or shell
- Ships with **starter examples** (reverse lines, upper-case selection, insert date, word count) seeded into the scripts folder on first run

### AI Assistant (optional)
- **Off by default** — nothing reaches the network until you enable it in **AI ▸ Settings**. Leave it off and the editor behaves exactly as before.
- **Pick your backend**: OpenAI (ChatGPT), Azure OpenAI, Google Gemini, Anthropic Claude, Ollama (local), or Perplexity — each with its own key/endpoint/model, stored per-provider so you can switch freely
- **Test connection** button in Settings does a tiny live round-trip and reports success/failure per provider
- **Chat panel** (`Ctrl+Shift+A`, or the 🤖 toolbar button) — a dockable conversation with **token-by-token streaming** responses and a Stop button
- **Selection actions** (AI menu or editor right-click): **Explain**, **Improve**, **Summarize**, **Ask about Selection…**, or **Send Selection to Chat** to frame your own question around the highlighted text
- **Agent mode** (a checkbox in the chat panel) — lets the AI *act on the active tab*, not just talk about it. It works through a small action protocol (read the text/selection, then `set_text` / `replace_selection` / `set_lines` / `insert`) in a bounded read→act→verify loop. **Every edit is previewed and must be confirmed** before it touches the buffer, and each confirmed batch is a single undo step. Provider-agnostic — no provider-specific function-calling API, so it behaves the same across all six backends.
- **Sandboxed by scope** — keys live only in your local `~/.config/nplus/ai.json`; requests go straight to the provider you chose and nowhere else

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
| `F5` | Run current document as a Lua script |
| `Ctrl+Shift+A` | Toggle AI chat panel |
| `Alt+Shift+S` | Trim trailing space and save |
| `F11` / `F12` / `Ctrl+0` | Zoom in / out / reset |

## License

MIT License — Copyright (c) June 2026 Mule Skinner. See source headers for full text.

using System;
using System.Collections;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using NPlus.Core;
using NPlus.Dialogs;

namespace NPlus.Views;

public partial class MainWindow
{
    private MenuItem _fileMenu = null!;
    private MenuItem _encodingMenu = null!;
    private MenuItem _checkStartupItem = null!;
    private MenuItem _foldViewItem = null!;

    private static MenuItem Mi(string header, Action onClick, KeyGesture? gesture = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture != null) item.InputGesture = gesture;
        item.Click += (_, _) => onClick();
        return item;
    }

    private static void AddItems(MenuItem parent, params object[] items)
    {
        var list = (IList)parent.Items;
        foreach (var i in items) list.Add(i);
    }

    private Menu BuildMenu()
    {
        var menu = new Menu();
        var top = (IList)menu.Items;

        // ---- File ----
        _fileMenu = new MenuItem { Header = "_File" };
        AddItems(_fileMenu,
            Mi("New", NewTab, new KeyGesture(Key.N, KeyModifiers.Control)),
            Mi("Open…", OpenFile, new KeyGesture(Key.O, KeyModifiers.Control)),
            Mi("Open by Path…", () => _ = OpenByPathPrompt()),
            Mi("Save", SaveFile, new KeyGesture(Key.S, KeyModifiers.Control)),
            Mi("Save As…", SaveFileAs, new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift)),
            Mi("Save All", SaveAll),
            new Separator(),
            Mi("Exit", Exit));
        top.Add(_fileMenu);

        // ---- Edit ----
        var edit = new MenuItem { Header = "_Edit" };
        AddItems(edit,
            Mi("Find…", () => ShowFind(0), new KeyGesture(Key.F, KeyModifiers.Control)),
            Mi("Replace…", () => ShowFind(1), new KeyGesture(Key.H, KeyModifiers.Control)),
            Mi("Mark…", () => ShowFind(3), new KeyGesture(Key.B, KeyModifiers.Control)),
            Mi("Find in Files…", () => ShowFind(2), new KeyGesture(Key.F, KeyModifiers.Control | KeyModifiers.Shift)),
            Mi("Find Next", FindNext, new KeyGesture(Key.F3)),
            new Separator(),
            BuildLineOpsMenu(),
            Mi("Toggle Column Mode", ToggleColumnMode, new KeyGesture(Key.A, KeyModifiers.Control | KeyModifiers.Alt)),
            BuildBookmarkMenu(),
            BuildBlankOpsMenu());
        top.Add(edit);

        // ---- View ----
        var view = new MenuItem { Header = "_View" };
        _foldViewItem = new MenuItem { Header = "Fold View", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _foldingEnabled };
        _foldViewItem.Click += (_, _) => ToggleFolding();
        AddItems(view,
            Mi("Zoom In", ZoomIn, new KeyGesture(Key.F11)),
            Mi("Zoom Out", ZoomOut, new KeyGesture(Key.F12)),
            Mi("Reset Zoom", ZoomReset, new KeyGesture(Key.D0, KeyModifiers.Control)),
            new Separator(),
            _foldViewItem,
            Mi("Collapse All", CollapseAll),
            Mi("Expand All", ExpandAll));
        top.Add(view);

        // ---- Macro ----
        var macro = new MenuItem { Header = "_Macro" };
        AddItems(macro,
            Mi("Start Recording", StartRecording),
            Mi("Stop Recording", StopRecording),
            Mi("Playback", () => PlaybackMacro(true), new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift)),
            Mi("Save Current Recorded Macro…", SaveMacroDialog),
            Mi("Run a Macro Multiple Times…", RunMacroMultiple),
            new Separator(),
            Mi("Load Saved Macro…", LoadMacroDialog),
            Mi("Edit Macro Steps…", EditMacroDialog),
            new Separator(),
            Mi("Trim Trailing Space and Save", TrimTrailingSpaceAndSave, new KeyGesture(Key.S, KeyModifiers.Alt | KeyModifiers.Shift)),
            Mi("Modify / Delete Macros…", ModifyMacros));
        top.Add(macro);

        // ---- Tools ----
        var tools = new MenuItem { Header = "_Tools" };
        var json = new MenuItem { Header = "JSON" };
        AddItems(json,
            Mi("Format / Pretty Print JSON", FormatJson),
            Mi("View JSON in Visual Tree", ToggleJsonPanel));
        var integration = new MenuItem { Header = "Desktop Integration" };
        AddItems(integration,
            Mi("Install 'Open with n+' desktop entry", InstallDesktopEntry),
            Mi("Remove 'Open with n+' desktop entry", RemoveDesktopEntry));
        AddItems(tools, json, integration);
        top.Add(tools);

        // ---- Encoding ----
        _encodingMenu = new MenuItem { Header = "E_ncoding" };
        foreach (var name in EncodingHelper.Names)
        {
            var captured = name;
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.CheckBox };
            item.Click += (_, _) => SetEncoding(captured);
            ((IList)_encodingMenu.Items).Add(item);
        }
        ((IList)_encodingMenu.Items).Add(new Separator());
        foreach (var name in EncodingHelper.Names)
        {
            var captured = name;
            var item = new MenuItem { Header = $"Convert to {name}" };
            item.Click += (_, _) => ConvertEncoding(captured);
            ((IList)_encodingMenu.Items).Add(item);
        }
        _encodingMenu.SubmenuOpened += (_, _) => UpdateEncodingMenuChecks();
        top.Add(_encodingMenu);

        // ---- Help ----
        var help = new MenuItem { Header = "_Help" };
        _checkStartupItem = new MenuItem { Header = "Check for Updates on Startup", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _checkForUpdatesOnStartup };
        _checkStartupItem.Click += (_, _) => { _checkForUpdatesOnStartup = !_checkForUpdatesOnStartup; _checkStartupItem.IsChecked = _checkForUpdatesOnStartup; };
        AddItems(help,
            Mi("User's Guide", ShowUserGuide),
            Mi("Check for Updates", CheckForUpdates),
            _checkStartupItem);
        top.Add(help);

        RebuildRecentFilesMenu();
        return menu;
    }

    private MenuItem BuildLineOpsMenu()
    {
        var m = new MenuItem { Header = "Line Operations" };
        AddItems(m,
            Mi("Duplicate Current Line", DuplicateCurrentLine, new KeyGesture(Key.D, KeyModifiers.Control)),
            Mi("Remove Duplicate Lines", RemoveDuplicateLines),
            Mi("Remove Consecutive Duplicate Lines", RemoveConsecutiveDuplicateLines),
            Mi("Split Lines", SplitLines, new KeyGesture(Key.I, KeyModifiers.Control)),
            Mi("Join Lines", JoinLines, new KeyGesture(Key.J, KeyModifiers.Control)),
            Mi("Move Line Up", () => MoveLine(-1), new KeyGesture(Key.Up, KeyModifiers.Control | KeyModifiers.Shift)),
            Mi("Move Line Down", () => MoveLine(1), new KeyGesture(Key.Down, KeyModifiers.Control | KeyModifiers.Shift)),
            Mi("Remove Empty Lines", () => RemoveEmptyLines(false)),
            Mi("Remove Empty Lines (whitespace)", () => RemoveEmptyLines(true)),
            Mi("Insert Blank Line Above", () => InsertBlankLine(false)),
            Mi("Insert Blank Line Below", () => InsertBlankLine(true)),
            new Separator(),
            Mi("Reverse Line Order", ReverseLineOrder),
            Mi("Randomize Line Order", RandomizeLineOrder),
            new Separator(),
            BuildSortMenu(),
            new Separator(),
            Mi("Delete Current Line", DeleteCurrentLine, new KeyGesture(Key.L, KeyModifiers.Control | KeyModifiers.Shift)));
        return m;
    }

    private MenuItem BuildSortMenu()
    {
        var m = new MenuItem { Header = "Sort Lines" };
        void Pair(string label, SortMode mode)
        {
            AddItems(m,
                Mi($"{label} — Ascending", () => SortLines(mode, false, false)),
                Mi($"{label} — Descending", () => SortLines(mode, true, false)),
                Mi($"{label} — Ascending (ignore case)", () => SortLines(mode, false, true)),
                Mi($"{label} — Descending (ignore case)", () => SortLines(mode, true, true)),
                new Separator());
        }
        Pair("Lexicographic", SortMode.Lexicographic);
        Pair("Locale", SortMode.Locale);
        AddItems(m,
            Mi("Integer — Ascending", () => SortLines(SortMode.Integer, false, false)),
            Mi("Integer — Descending", () => SortLines(SortMode.Integer, true, false)),
            Mi("Decimal (dot) — Ascending", () => SortLines(SortMode.DecimalDot, false, false)),
            Mi("Decimal (dot) — Descending", () => SortLines(SortMode.DecimalDot, true, false)),
            Mi("Decimal (comma) — Ascending", () => SortLines(SortMode.DecimalComma, false, false)),
            Mi("Decimal (comma) — Descending", () => SortLines(SortMode.DecimalComma, true, false)),
            Mi("Length — Ascending", () => SortLines(SortMode.Length, false, false)),
            Mi("Length — Descending", () => SortLines(SortMode.Length, true, false)));
        return m;
    }

    private MenuItem BuildBookmarkMenu()
    {
        var m = new MenuItem { Header = "Bookmark" };
        AddItems(m,
            Mi("Toggle Bookmark", ToggleBookmark, new KeyGesture(Key.F2, KeyModifiers.Control)),
            Mi("Next Bookmark", () => NavigateBookmark(true), new KeyGesture(Key.F2)),
            Mi("Previous Bookmark", () => NavigateBookmark(false), new KeyGesture(Key.F2, KeyModifiers.Shift)),
            new Separator(),
            Mi("Copy Bookmarked Lines", () => ProcessBookmarks(false, false)),
            Mi("Cut Bookmarked Lines", () => ProcessBookmarks(true, false)),
            Mi("Delete Bookmarked Lines", () => ProcessBookmarks(true, true)),
            Mi("Delete Non-Bookmarked Lines", InverseBookmarkDelete),
            Mi("Paste to (Replace) Bookmarked Lines", PasteToBookmarks),
            new Separator(),
            Mi("Clear All Bookmarks", ClearAllBookmarks));
        return m;
    }

    private MenuItem BuildBlankOpsMenu()
    {
        var m = new MenuItem { Header = "Blank Operations" };
        AddItems(m,
            Mi("Trim Trailing Space", () => ApplyWholeText(TextTransforms.TrimTrailing)),
            Mi("Trim Leading Space", () => ApplyWholeText(TextTransforms.TrimLeading)),
            Mi("Trim Leading and Trailing Space", () => ApplyWholeText(TextTransforms.TrimBoth)),
            Mi("EOL to Space", () => ApplyWholeTextNoNl(TextTransforms.EolToSpace)),
            Mi("Trim Both and EOL to Space", () => ApplyWholeTextNoNl(TextTransforms.TrimBothAndEolToSpace)),
            new Separator(),
            Mi("TAB to Space", () => ApplyWholeTextNoNl(t => TextTransforms.TabToSpace(t, 4))),
            Mi("Space to TAB (All)", () => ApplyWholeTextNoNl(t => TextTransforms.SpaceToTabAll(t, 4))),
            Mi("Space to TAB (Leading)", () => ApplyWholeText((t, nl) => TextTransforms.SpaceToTabLeading(t, 4, nl))));
        return m;
    }

    // ---- Keyboard shortcuts (works regardless of focus) ----
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        switch (e.Key)
        {
            case Key.N when ctrl && !shift && !alt: NewTab(); break;
            case Key.O when ctrl && !shift && !alt: OpenFile(); break;
            case Key.S when ctrl && shift: SaveFileAs(); break;
            case Key.S when ctrl: SaveFile(); break;
            case Key.S when alt && shift: TrimTrailingSpaceAndSave(); break;
            case Key.F when ctrl && shift: ShowFind(2); break;
            case Key.F when ctrl: ShowFind(0); break;
            case Key.H when ctrl: ShowFind(1); break;
            case Key.B when ctrl: ShowFind(3); break;
            case Key.F3: FindNext(); break;
            case Key.A when ctrl && alt: ToggleColumnMode(); break;
            case Key.D when ctrl: DuplicateCurrentLine(); break;
            case Key.I when ctrl: SplitLines(); break;
            case Key.J when ctrl: JoinLines(); break;
            case Key.L when ctrl && shift: DeleteCurrentLine(); break;
            case Key.Up when ctrl && shift: MoveLine(-1); break;
            case Key.Down when ctrl && shift: MoveLine(1); break;
            case Key.F2 when ctrl: ToggleBookmark(); break;
            case Key.F2 when shift: NavigateBookmark(false); break;
            case Key.F2: NavigateBookmark(true); break;
            case Key.P when ctrl && shift: PlaybackMacro(true); break;
            case Key.F11: ZoomIn(); break;
            case Key.F12: ZoomOut(); break;
            case Key.D0 when ctrl: ZoomReset(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---- Shared helpers ----
    private async void ShowMessage(string title, string message) =>
        await MessageBoxes.Show(this, title, message);

    private void UpdateEncodingMenuChecks()
    {
        var doc = ActiveDoc;
        string current = doc != null ? EncodingHelper.GetEncodingName(doc.Encoding) : EncodingHelper.Utf8;
        var items = (IList)_encodingMenu.Items;
        for (int i = 0; i < EncodingHelper.Names.Length && i < items.Count; i++)
            if (items[i] is MenuItem mi)
                mi.IsChecked = (string)mi.Header! == current;
    }

    private void CheckForUpdates() =>
        ShowMessage("n+", "You are running n+ for Linux (Avalonia edition). Automatic update checks are not configured in this build.");
}

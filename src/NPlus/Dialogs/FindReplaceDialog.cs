using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NPlus.Core;
using NPlus.Models;
using NPlus.Search;
using NPlus.Views;

namespace NPlus.Dialogs;

/// <summary>
/// Non-modal Find / Replace / Mark / Find-in-Files tool window.
/// Mirrors the original FindReplaceDialog; uses SearchEngine for in-editor operations.
/// </summary>
public sealed class FindReplaceDialog : Window
{
    private const int MaxHistory = 20;
    private readonly MainWindow _main;
    private readonly List<string> _findHistory = new();
    private readonly List<string> _replaceHistory = new();

    private TabControl _tabs = null!;
    private AutoCompleteBox _find = null!, _replace = null!;
    private AutoCompleteBox _fifFind = null!, _fifReplace = null!, _fifFilter = null!, _fifDir = null!;
    private CheckBox _matchCase = null!, _wholeWord = null!, _wrap = null!, _backward = null!, _bookmarkLine = null!, _purge = null!;
    private CheckBox _fifMatchCase = null!, _fifWholeWord = null!, _fifSub = null!, _fifHidden = null!;
    private RadioButton _normal = null!, _extended = null!, _regex = null!;
    private RadioButton _fifNormal = null!, _fifExtended = null!, _fifRegex = null!;
    private TextBlock _status = null!;

    public FindReplaceDialog(MainWindow main)
    {
        _main = main;
        Title = "Find";
        Width = 600;
        Height = 360;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        BuildUi();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
    }

    private SearchOptions Options() => new()
    {
        MatchCase = _matchCase.IsChecked == true,
        WholeWord = _wholeWord.IsChecked == true,
        Regex = _regex.IsChecked == true,
        Extended = _extended.IsChecked == true,
        Backward = _backward.IsChecked == true,
        Wrap = _wrap.IsChecked == true,
    };

    public void SetMode(int tabIndex)
    {
        if (tabIndex is >= 0 and <= 3) _tabs.SelectedIndex = tabIndex;
        var ed = _main.GetActiveEditor();
        if (ed != null && ed.SelectionLength is > 0 and < 500 && !ed.SelectedText.Contains('\n'))
        {
            _find.Text = ed.SelectedText;
            _fifFind.Text = ed.SelectedText;
        }
        if (tabIndex == 2 && string.IsNullOrEmpty(_fifDir.Text))
        {
            var path = _main.ActiveDoc?.FilePath;
            if (!string.IsNullOrEmpty(path)) _fifDir.Text = Path.GetDirectoryName(path);
        }
    }

    private void BuildUi()
    {
        _tabs = new TabControl();
        _status = new TextBlock { Margin = new Thickness(8, 4), Foreground = Brushes.Gray };

        var dock = new DockPanel { LastChildFill = true };
        var statusBorder = new Border { Child = _status, Height = 26, BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(statusBorder, Dock.Bottom);
        dock.Children.Add(statusBorder);
        dock.Children.Add(_tabs);

        ((System.Collections.IList)_tabs.Items).Add(new TabItem { Header = "Find", Content = BuildFindReplacePanel(false) });
        ((System.Collections.IList)_tabs.Items).Add(new TabItem { Header = "Replace", Content = BuildFindReplacePanel(true) });
        ((System.Collections.IList)_tabs.Items).Add(new TabItem { Header = "Find in Files", Content = BuildFifPanel() });
        ((System.Collections.IList)_tabs.Items).Add(new TabItem { Header = "Mark", Content = BuildMarkPanel() });

        Content = dock;
    }

    private AutoCompleteBox Combo(List<string> history, double width = 260)
    {
        return new AutoCompleteBox
        {
            Width = width,
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
            ItemsSource = history,
            MinimumPrefixLength = 0,
        };
    }

    private void AddHistory(List<string> history, AutoCompleteBox box)
    {
        var text = box.Text;
        if (string.IsNullOrEmpty(text)) return;
        history.Remove(text);
        history.Insert(0, text);
        if (history.Count > MaxHistory) history.RemoveRange(MaxHistory, history.Count - MaxHistory);
        box.ItemsSource = null;
        box.ItemsSource = history;
        box.Text = text;
    }

    private StackPanel ModeBox(out RadioButton normal, out RadioButton extended, out RadioButton regex, string group)
    {
        normal = new RadioButton { Content = "Normal", GroupName = group, IsChecked = true };
        extended = new RadioButton { Content = @"Extended (\n, \r, \t…)", GroupName = group };
        regex = new RadioButton { Content = "Regular expression", GroupName = group };
        return new StackPanel { Spacing = 2, Children = { normal, extended, regex } };
    }

    private Control BuildFindReplacePanel(bool replace)
    {
        _find ??= Combo(_findHistory);
        _replace ??= Combo(_replaceHistory);
        _matchCase ??= new CheckBox { Content = "Match case" };
        _wholeWord ??= new CheckBox { Content = "Match whole word only" };
        _wrap ??= new CheckBox { Content = "Wrap around", IsChecked = true };
        _backward ??= new CheckBox { Content = "Backward direction" };

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        void Place(Control c, int row, int col, int colSpan = 1) { Grid.SetRow(c, row); Grid.SetColumn(c, col); Grid.SetColumnSpan(c, colSpan); grid.Children.Add(c); }

        Place(new TextBlock { Text = "Find what:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) }, 0, 0);
        Place(_find, 0, 1);
        if (replace)
        {
            Place(new TextBlock { Text = "Replace with:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) }, 1, 0);
            Place(_replace, 1, 1);
        }

        var opts = new StackPanel { Spacing = 3, Margin = new Thickness(0, 10, 0, 0), Children = { _backward, _matchCase, _wholeWord, _wrap } };
        Place(opts, 2, 0, 2);

        var mode = ModeBox(out _normal, out _extended, out _regex, "frmode");
        mode.Margin = new Thickness(0, 10, 0, 0);
        Place(new HeaderedContentControl { Header = "Search Mode", Content = mode }, 3, 0, 2);

        var buttons = new StackPanel { Spacing = 6, Margin = new Thickness(16, 0, 0, 0), Width = 150 };
        buttons.Children.Add(Btn("Find Next", () => Execute("FindNext")));
        buttons.Children.Add(Btn("Count", () => Execute("Count")));
        buttons.Children.Add(Btn("Find All in All Tabs", FindAllInTabs));
        if (replace)
        {
            buttons.Children.Add(Btn("Replace", () => Execute("Replace")));
            buttons.Children.Add(Btn("Replace All", () => Execute("ReplaceAll")));
        }
        buttons.Children.Add(Btn("Close", Hide));
        Place(buttons, 0, 2, 1);
        Grid.SetRowSpan(buttons, 5);

        return grid;
    }

    private Control BuildMarkPanel()
    {
        _bookmarkLine ??= new CheckBox { Content = "Bookmark line", IsChecked = true };
        _purge ??= new CheckBox { Content = "Purge for each search" };

        var markFind = Combo(_findHistory);

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        void Place(Control c, int row, int col, int colSpan = 1) { Grid.SetRow(c, row); Grid.SetColumn(c, col); Grid.SetColumnSpan(c, colSpan); grid.Children.Add(c); }

        Place(new TextBlock { Text = "Find what:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) }, 0, 0);
        Place(markFind, 0, 1);
        Place(new StackPanel { Spacing = 3, Margin = new Thickness(0, 10, 0, 0), Children = { _bookmarkLine, _purge } }, 1, 0, 2);

        var buttons = new StackPanel { Spacing = 6, Margin = new Thickness(16, 0, 0, 0), Width = 150 };
        buttons.Children.Add(Btn("Mark All", () => { _find.Text = markFind.Text; Execute("MarkAll"); }));
        buttons.Children.Add(Btn("Clear all marks", () => Execute("ClearMarks")));
        buttons.Children.Add(Btn("Copy Marked Text", CopyMarkedLines));
        buttons.Children.Add(Btn("Close", Hide));
        Place(buttons, 0, 2);
        Grid.SetRowSpan(buttons, 4);
        return grid;
    }

    private Control BuildFifPanel()
    {
        _fifFind = Combo(_findHistory, 240);
        _fifReplace = Combo(_replaceHistory, 240);
        _fifFilter = Combo(new List<string> { "*.*", "*.cs", "*.txt", "*.json", "*.xml", "*.html", "*.js", "*.py", "*.sql" }, 240);
        _fifFilter.Text = "*.*";
        _fifDir = Combo(new List<string>(), 210);
        _fifMatchCase = new CheckBox { Content = "Match case" };
        _fifWholeWord = new CheckBox { Content = "Match whole word only" };
        _fifSub = new CheckBox { Content = "In all sub-folders", IsChecked = true };
        _fifHidden = new CheckBox { Content = "In hidden folders" };

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int i = 0; i < 7; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        void Place(Control c, int row, int col, int colSpan = 1) { Grid.SetRow(c, row); Grid.SetColumn(c, col); Grid.SetColumnSpan(c, colSpan); grid.Children.Add(c); }
        void Label(string t, int row) => Place(new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) }, row, 0);

        Label("Find what:", 0); Place(_fifFind, 0, 1);
        Label("Replace with:", 1); Place(_fifReplace, 1, 1);
        Label("Filters:", 2); Place(_fifFilter, 2, 1);
        Label("Directory:", 3);
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        dirRow.Children.Add(_fifDir);
        var browse = new Button { Content = "…", Width = 32 };
        browse.Click += async (_, _) => await BrowseDir();
        dirRow.Children.Add(browse);
        Place(dirRow, 3, 1);

        Place(new StackPanel { Spacing = 3, Margin = new Thickness(0, 8, 0, 0), Children = { _fifMatchCase, _fifWholeWord, _fifSub, _fifHidden } }, 4, 0, 2);

        var mode = ModeBox(out _fifNormal, out _fifExtended, out _fifRegex, "fifmode");
        Place(new HeaderedContentControl { Header = "Search Mode", Content = mode, Margin = new Thickness(0, 8, 0, 0) }, 5, 0, 2);

        var buttons = new StackPanel { Spacing = 6, Margin = new Thickness(16, 0, 0, 0), Width = 150 };
        buttons.Children.Add(Btn("Find All", () => RunFindInFiles(false)));
        buttons.Children.Add(Btn("Replace in Files", () => RunFindInFiles(true)));
        buttons.Children.Add(Btn("Close", Hide));
        Place(buttons, 0, 2);
        Grid.SetRowSpan(buttons, 4);
        return grid;
    }

    private Button Btn(string text, Action onClick)
    {
        var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Click += (_, _) => onClick();
        return b;
    }

    private async System.Threading.Tasks.Task BrowseDir()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select search directory" });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) _fifDir.Text = path;
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.Foreground = error ? Brushes.IndianRed : Brushes.Gray;
    }

    public void FindNextExternal()
    {
        if (!string.IsNullOrEmpty(_find.Text)) Execute("FindNext");
    }

    private void FindAllInTabs()
    {
        string pattern = _find.Text ?? "";
        if (string.IsNullOrEmpty(pattern)) return;
        AddHistory(_findHistory, _find);
        try
        {
            int n = _main.FindAllInOpenTabs(pattern, Options());
            SetStatus($"{n} hit(s) across all open tabs.");
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}", true); }
    }

    private void Execute(string action)
    {
        var ed = _main.GetActiveEditor();
        var doc = _main.ActiveDoc;
        if (ed == null || doc == null) return;
        var o = Options();

        if (action == "ClearMarks")
        {
            _main.RecordMacroStep(new MacroStep { ActionType = MacroActionType.ClearMarks });
            SearchEngine.ClearMarks(doc, ed);
            SetStatus("All marks cleared.");
            return;
        }

        string pattern = action == "MarkAll" ? _find.Text ?? "" : _find.Text ?? "";
        if (string.IsNullOrEmpty(pattern)) return;
        AddHistory(_findHistory, _find);

        try
        {
            switch (action)
            {
                case "Count":
                    int c = SearchEngine.Count(ed, pattern, o);
                    SetStatus($"Count: {c} match{(c != 1 ? "es" : "")} found.");
                    break;
                case "ReplaceAll":
                    AddHistory(_replaceHistory, _replace);
                    _main.RecordMacroStep(new MacroStep { ActionType = MacroActionType.ReplaceAll, SearchText = pattern, ReplaceText = _replace.Text, IsRegex = o.Regex, Flags = 0 });
                    int n = SearchEngine.ReplaceAll(ed, pattern, _replace.Text ?? "", o);
                    SetStatus($"{n} occurrence{(n != 1 ? "s" : "")} replaced.");
                    break;
                case "MarkAll":
                    _main.RecordMacroStep(new MacroStep { ActionType = MacroActionType.MarkAll, SearchText = pattern, IsBookmark = _bookmarkLine.IsChecked == true, IsPurge = _purge.IsChecked == true });
                    int marks = SearchEngine.MarkAll(ed, doc, pattern, o, _bookmarkLine.IsChecked == true, _purge.IsChecked == true);
                    SetStatus($"Mark: {marks} match{(marks != 1 ? "es" : "")}.");
                    break;
                case "Replace":
                    AddHistory(_replaceHistory, _replace);
                    _main.RecordMacroStep(new MacroStep { ActionType = MacroActionType.FindReplace, SearchText = pattern, ReplaceText = _replace.Text, IsReplace = true, IsRegex = o.Regex, IsBackward = o.Backward, IsWrap = o.Wrap });
                    if (!SearchEngine.ReplaceNext(ed, pattern, _replace.Text ?? "", o)) SetStatus($"Can't find the text \"{pattern}\"", true);
                    else SetStatus("Replaced.");
                    break;
                default: // FindNext
                    _main.RecordMacroStep(new MacroStep { ActionType = MacroActionType.FindReplace, SearchText = pattern, IsReplace = false, IsRegex = o.Regex, IsBackward = o.Backward, IsWrap = o.Wrap });
                    if (!SearchEngine.FindNext(ed, pattern, o)) SetStatus($"Can't find the text \"{pattern}\"", true);
                    else { var l = ed.Document.GetLineByOffset(ed.SelectionStart).LineNumber; SetStatus($"Found at line {l}."); }
                    break;
            }
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}", true); }
    }

    private async void CopyMarkedLines()
    {
        var ed = _main.GetActiveEditor();
        var doc = _main.ActiveDoc;
        if (ed == null || doc == null || Clipboard == null) return;
        var lines = new List<string>();
        foreach (int ln in doc.Bookmarks.OrderBy(x => x))
        {
            if (ln >= 1 && ln <= ed.Document.LineCount)
            {
                var l = ed.Document.GetLineByNumber(ln);
                lines.Add(ed.Document.GetText(l.Offset, l.Length));
            }
        }
        if (lines.Count > 0) { await Clipboard.SetTextAsync(string.Join("\n", lines)); SetStatus($"{lines.Count} bookmarked line(s) copied."); }
        else SetStatus("No bookmarked lines found to copy.", true);
    }

    // ---- Find in Files ----

    private void RunFindInFiles(bool replaceMode)
    {
        string searchText = _fifFind.Text ?? "";
        if (string.IsNullOrEmpty(searchText)) return;
        string directory = _fifDir.Text ?? "";
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) { SetStatus("Please select a valid directory.", true); return; }

        string? replaceText = replaceMode ? _fifReplace.Text : null;
        string filter = string.IsNullOrWhiteSpace(_fifFilter.Text) ? "*.*" : _fifFilter.Text!.Trim();
        bool matchCase = _fifMatchCase.IsChecked == true;
        bool wholeWord = _fifWholeWord.IsChecked == true;
        bool subFolders = _fifSub.IsChecked == true;
        bool hiddenFolders = _fifHidden.IsChecked == true;
        bool useRegex = _fifRegex.IsChecked == true;
        bool useExtended = _fifExtended.IsChecked == true;

        AddHistory(_findHistory, _fifFind);
        if (replaceMode) AddHistory(_replaceHistory, _fifReplace);

        if (useExtended)
        {
            searchText = searchText.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\0", "\0");
            if (replaceText != null) replaceText = replaceText.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\0", "\0");
        }

        SetStatus("Searching…");
        var matchTimeout = TimeSpan.FromSeconds(5);
        var regOpts = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        var strComp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Regex? rx = null;
        if (useRegex)
        {
            try { rx = new Regex(searchText, regOpts, matchTimeout); }
            catch (Exception ex) { SetStatus($"Invalid regex: {ex.Message}", true); return; }
        }
        else if (wholeWord)
        {
            rx = new Regex(@"\b" + Regex.Escape(searchText) + @"\b", regOpts, matchTimeout);
        }

        var results = new List<string>();
        int totalMatches = 0, filesMatched = 0, filesSearched = 0, filesReplaced = 0;
        var searchOption = subFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        try
        {
            var filters = filter.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var allFiles = new List<string>();
            foreach (var f in filters)
            {
                try { allFiles.AddRange(Directory.GetFiles(directory, f.Trim(), searchOption)); }
                catch { }
            }
            var fileSet = new HashSet<string>(allFiles, StringComparer.OrdinalIgnoreCase);

            foreach (string filePath in fileSet)
            {
                if (!hiddenFolders)
                {
                    var dirInfo = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
                    if ((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;
                }
                try
                {
                    string content = File.ReadAllText(filePath);
                    string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    bool fileHasMatch = false;
                    filesSearched++;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        bool lineMatched = rx != null ? rx.IsMatch(lines[i]) : lines[i].IndexOf(searchText, strComp) >= 0;
                        if (lineMatched)
                        {
                            totalMatches++;
                            if (!fileHasMatch) { filesMatched++; fileHasMatch = true; }
                            string trimmed = lines[i].Trim();
                            if (trimmed.Length > 200) trimmed = trimmed.Substring(0, 200) + "…";
                            results.Add($"{filePath}|{i + 1}|{trimmed}");
                        }
                    }
                    if (replaceMode && fileHasMatch)
                    {
                        string newContent = rx != null ? rx.Replace(content, replaceText ?? "")
                            : matchCase ? content.Replace(searchText, replaceText ?? "")
                            : Regex.Replace(content, Regex.Escape(searchText), (replaceText ?? "").Replace("$", "$$"), RegexOptions.IgnoreCase);
                        File.WriteAllText(filePath, newContent);
                        filesReplaced++;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}", true); return; }

        _main.ShowFindInFilesResults(results, searchText, replaceMode);
        SetStatus(replaceMode
            ? $"Replaced in {filesReplaced} file(s). {totalMatches} hit(s) in {filesSearched} searched."
            : $"{totalMatches} hit(s) in {filesMatched} file(s) ({filesSearched} searched).");
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}

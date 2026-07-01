using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using NPlus.Dialogs;
using NPlus.Models;
using NPlus.Search;

namespace NPlus.Views;

public partial class MainWindow
{
    private FindReplaceDialog? _findDialog;

    private void ShowFind(int tabIndex)
    {
        if (_findDialog == null || !_findDialog.IsVisible)
        {
            _findDialog = new FindReplaceDialog(this);
            _findDialog.Show(this);
        }
        _findDialog.SetMode(tabIndex);
        _findDialog.Activate();
    }

    private void FindNext()
    {
        if (_findDialog is { IsVisible: true })
            _findDialog.FindNextExternal();
        else
            ShowFind(0);
    }

    private Border BuildFindResultsPanel()
    {
        _findResultsHeader = new TextBlock { Margin = new Thickness(8, 4), FontWeight = FontWeight.SemiBold };
        _findResultsList = new ListBox();
        _findResultsList.DoubleTapped += (_, _) => OpenSelectedResult();

        var close = new Button { Content = "✕", Width = 24, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => HideFindResults();

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        headerRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(_findResultsHeader, 0);
        Grid.SetColumn(close, 1);
        headerRow.Children.Add(_findResultsHeader);
        headerRow.Children.Add(close);

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(headerRow, Dock.Top);
        dock.Children.Add(headerRow);
        dock.Children.Add(_findResultsList);

        return new Border { Child = dock, BorderThickness = new Thickness(0, 1, 0, 0), IsVisible = false };
    }

    public void ShowFindInFilesResults(List<string> results, string searchText, bool replaceMode)
    {
        _findResultsHeader.Text = $"{(replaceMode ? "Replace in Files" : "Find in Files")}: \"{searchText}\" — {results.Count} hit(s)";
        _findResultsList.ItemsSource = results.Select(r =>
        {
            var parts = r.Split('|', 3);
            string display = parts.Length >= 3 ? $"{Path.GetFileName(parts[0])}({parts[1]}): {parts[2]}" : r;
            return new ResultRow { Raw = r, Display = display };
        }).ToList();
        _findResultsList.ItemTemplate = new FuncDataTemplate<ResultRow>((row, _) =>
            new TextBlock { Text = row?.Display ?? "", Margin = new Thickness(6, 1) });
        _findResultsPanel.IsVisible = true;
        _findResultsRow.Height = new GridLength(220, GridUnitType.Pixel);
    }

    /// <summary>Searches every open text tab for <paramref name="pattern"/> and lists hits in the results panel.</summary>
    public int FindAllInOpenTabs(string pattern, SearchOptions o)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;
        var rx = SearchEngine.BuildRegex(pattern, o); // invalid regex throws to the caller

        var hits = new List<ResultRow>();
        int total = 0, tabsMatched = 0;
        foreach (var item in _tabs.Items)
        {
            if (item is not TabItem tab || !_docs.TryGetValue(tab, out var doc) || doc.Editor?.Document == null)
                continue; // skip hex tabs
            var d = doc.Editor.Document;
            bool any = false;
            for (int ln = 1; ln <= d.LineCount; ln++)
            {
                var line = d.GetLineByNumber(ln);
                string text = d.GetText(line.Offset, line.Length);
                if (!rx.IsMatch(text)) continue;
                total++;
                any = true;
                string trimmed = text.Trim();
                if (trimmed.Length > 200) trimmed = trimmed.Substring(0, 200) + "…";
                hits.Add(new ResultRow { Doc = doc, Line = ln, Display = $"{doc.DisplayTitle}({ln}): {trimmed}" });
            }
            if (any) tabsMatched++;
        }

        _findResultsHeader.Text = $"Find in Open Tabs: \"{pattern}\" — {total} hit(s) in {tabsMatched} tab(s)";
        _findResultsList.ItemsSource = hits;
        _findResultsList.ItemTemplate = new FuncDataTemplate<ResultRow>((row, _) =>
            new TextBlock { Text = row?.Display ?? "", Margin = new Thickness(6, 1) });
        _findResultsPanel.IsVisible = true;
        _findResultsRow.Height = new GridLength(220, GridUnitType.Pixel);
        return total;
    }

    private void HideFindResults()
    {
        _findResultsPanel.IsVisible = false;
        _findResultsRow.Height = new GridLength(0, GridUnitType.Pixel);
    }

    private void OpenSelectedResult()
    {
        if (_findResultsList.SelectedItem is not ResultRow row) return;

        // Open-tabs result: jump straight to the owning tab (keeps unsaved edits).
        if (row.Doc != null)
        {
            if (!_docs.ContainsKey(row.Doc.TabItem)) return; // tab was closed since the search
            _tabs.SelectedItem = row.Doc.TabItem;
            var ted = row.Doc.Editor;
            if (ted?.Document != null && row.Line >= 1 && row.Line <= ted.Document.LineCount)
            {
                var l = ted.Document.GetLineByNumber(row.Line);
                ted.CaretOffset = l.Offset;
                ted.Select(l.Offset, l.Length);
                ted.ScrollToLine(row.Line);
                ted.Focus();
            }
            return;
        }

        var parts = row.Raw.Split('|', 3);
        if (parts.Length < 2) return;
        string path = parts[0];
        if (!int.TryParse(parts[1], out int lineNo)) lineNo = 1;
        if (!File.Exists(path)) return;

        OpenFilesFromPaths(new[] { path });
        var ed = GetActiveEditor();
        if (ed?.Document != null && lineNo >= 1 && lineNo <= ed.Document.LineCount)
        {
            var line = ed.Document.GetLineByNumber(lineNo);
            ed.CaretOffset = line.Offset;
            ed.Select(line.Offset, line.Length);
            ed.ScrollToLine(lineNo);
            ed.Focus();
        }
    }

    private sealed class ResultRow
    {
        public string Raw = "";
        public string Display = "";
        public EditorDocument? Doc;   // set for open-tab results (jump to tab, not a disk file)
        public int Line;
    }
}

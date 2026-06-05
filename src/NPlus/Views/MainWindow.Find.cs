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

    private void HideFindResults()
    {
        _findResultsPanel.IsVisible = false;
        _findResultsRow.Height = new GridLength(0, GridUnitType.Pixel);
    }

    private void OpenSelectedResult()
    {
        if (_findResultsList.SelectedItem is not ResultRow row) return;
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
    }
}

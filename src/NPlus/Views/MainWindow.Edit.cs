using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using NPlus.Core;
using NPlus.Editor;
using NPlus.Models;

namespace NPlus.Views;

public partial class MainWindow
{
    // ---- Newline + line helpers ----

    private static string GetNewline(TextEditor ed)
    {
        var text = ed.Document.Text;
        int nl = text.IndexOf('\n');
        if (nl > 0 && text[nl - 1] == '\r') return "\r\n";
        if (nl >= 0) return "\n";
        return Environment.NewLine;
    }

    private static (int startLine, int endLine, List<string> lines) GetSelectedOrAllLines(TextEditor ed)
    {
        var doc = ed.Document;
        int startLine, endLine;
        if (ed.SelectionLength == 0)
        {
            startLine = 1;
            endLine = doc.LineCount;
        }
        else
        {
            startLine = doc.GetLineByOffset(ed.SelectionStart).LineNumber;
            endLine = doc.GetLineByOffset(ed.SelectionStart + ed.SelectionLength).LineNumber;
        }
        var lines = new List<string>();
        for (int i = startLine; i <= endLine; i++)
        {
            var l = doc.GetLineByNumber(i);
            lines.Add(doc.GetText(l.Offset, l.Length));
        }
        return (startLine, endLine, lines);
    }

    private static void ReplaceLineRange(TextEditor ed, int startLine, int endLine, IList<string> newLines, string nl)
    {
        var doc = ed.Document;
        var sl = doc.GetLineByNumber(startLine);
        var el = doc.GetLineByNumber(endLine);
        int start = sl.Offset;
        int end = el.EndOffset;
        doc.Replace(start, end - start, string.Join(nl, newLines));
    }

    // ---- Whole-document blank op helpers ----

    private void ApplyWholeText(Func<string, string, string> transform)
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        string nl = GetNewline(ed);
        ed.Document.Text = transform(ed.Document.Text, nl);
    }

    private void ApplyWholeTextNoNl(Func<string, string> transform)
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        ed.Document.Text = transform(ed.Document.Text);
    }

    // ---- Line operations ----

    private void DuplicateCurrentLine()
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        var line = ed.Document.GetLineByOffset(ed.CaretOffset);
        string text = ed.Document.GetText(line.Offset, line.Length);
        string nl = GetNewline(ed);
        ed.Document.Insert(line.EndOffset, nl + text);
    }

    private void RemoveDuplicateLines() =>
        ApplyWholeText((t, nl) => TextTransforms.RemoveDuplicateLines(t, nl));

    private void RemoveConsecutiveDuplicateLines() =>
        ApplyWholeText((t, nl) => TextTransforms.RemoveConsecutiveDuplicateLines(t, nl));

    private void RemoveEmptyLines(bool whitespace) =>
        ApplyWholeText((t, nl) => TextTransforms.RemoveEmptyLines(t, whitespace, nl));

    private void SplitLines()
    {
        var ed = GetActiveEditor();
        if (ed == null || ed.SelectionLength == 0) return;
        string sel = ed.SelectedText;
        string nl = GetNewline(ed);
        string replacement = string.Join(nl, sel.Where(c => c != '\r' && c != '\n').Select(c => c.ToString()));
        ed.Document.Replace(ed.SelectionStart, ed.SelectionLength, replacement);
    }

    private void JoinLines()
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        if (ed.SelectionLength > 0)
        {
            string sel = ed.SelectedText.Replace("\r", "").Replace("\n", " ").Replace("  ", " ");
            ed.Document.Replace(ed.SelectionStart, ed.SelectionLength, sel);
        }
        else
        {
            var doc = ed.Document;
            var line = doc.GetLineByOffset(ed.CaretOffset);
            if (line.NextLine == null) return;
            int start = line.Offset;
            int end = line.NextLine.EndOffset;
            string joined = doc.GetText(start, end - start).Replace("\r", "").Replace("\n", " ").Replace("  ", " ");
            doc.Replace(start, end - start, joined);
        }
    }

    private void MoveLine(int dir)
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        var doc = ed.Document;
        int cur = doc.GetLineByOffset(ed.CaretOffset).LineNumber;
        int target = cur + dir;
        if (target < 1 || target > doc.LineCount) return;
        string nl = GetNewline(ed);

        var curLine = doc.GetLineByNumber(cur);
        string curText = doc.GetText(curLine.Offset, curLine.Length);
        using (doc.RunUpdate())
        {
            // Remove current line (with its delimiter).
            doc.Remove(curLine.Offset, curLine.TotalLength);
            var targetLine = doc.GetLineByNumber(Math.Min(target, doc.LineCount));
            int insertOffset = dir < 0 ? targetLine.Offset : targetLine.EndOffset;
            if (dir > 0) doc.Insert(insertOffset, nl + curText);
            else doc.Insert(insertOffset, curText + nl);
            ed.CaretOffset = Math.Min(insertOffset, doc.TextLength);
        }
    }

    private void InsertBlankLine(bool below)
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        var line = ed.Document.GetLineByOffset(ed.CaretOffset);
        string nl = GetNewline(ed);
        if (below)
        {
            ed.Document.Insert(line.EndOffset, nl);
            ed.CaretOffset = Math.Min(line.EndOffset + nl.Length, ed.Document.TextLength);
        }
        else
        {
            ed.Document.Insert(line.Offset, nl);
            ed.CaretOffset = line.Offset;
        }
    }

    private void ReverseLineOrder()
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        var (s, e, lines) = GetSelectedOrAllLines(ed);
        TextTransforms.ReverseLines(lines);
        ReplaceLineRange(ed, s, e, lines, GetNewline(ed));
    }

    private void RandomizeLineOrder()
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        var (s, e, lines) = GetSelectedOrAllLines(ed);
        TextTransforms.RandomizeLines(lines);
        ReplaceLineRange(ed, s, e, lines, GetNewline(ed));
    }

    private void SortLines(SortMode mode, bool descending, bool ignoreCase)
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        var (s, e, lines) = GetSelectedOrAllLines(ed);
        var sorted = TextTransforms.SortLines(lines, mode, descending, ignoreCase);
        ReplaceLineRange(ed, s, e, sorted, GetNewline(ed));
    }

    private void DeleteCurrentLine()
    {
        var ed = GetActiveEditor();
        if (ed == null) return;
        var line = ed.Document.GetLineByOffset(ed.CaretOffset);
        ed.Document.Remove(line.Offset, line.TotalLength);
    }

    // ---- Column mode ----

    private void ToggleColumnMode()
    {
        _columnMode = !_columnMode;
        // AvaloniaEdit performs rectangular selection on Alt+drag; the toggle reflects intent.
        UpdateToggleButtonVisuals();
    }

    // ---- Bookmarks ----

    private void RefreshBookmarkRender(EditorDocument doc) =>
        doc.Editor?.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);

    private void ToggleBookmark()
    {
        var doc = ActiveDoc;
        var ed = doc?.Editor;
        if (doc == null || ed == null) return;
        int line = ed.Document.GetLineByOffset(ed.CaretOffset).LineNumber;
        if (!doc.Bookmarks.Add(line)) doc.Bookmarks.Remove(line);
        RefreshBookmarkRender(doc);
    }

    private void NavigateBookmark(bool next)
    {
        var doc = ActiveDoc;
        var ed = doc?.Editor;
        if (doc == null || ed == null || doc.Bookmarks.Count == 0) return;
        int cur = ed.Document.GetLineByOffset(ed.CaretOffset).LineNumber;
        var sorted = doc.Bookmarks.OrderBy(x => x).ToList();
        int target;
        if (next)
            target = sorted.FirstOrDefault(l => l > cur, sorted[0]);
        else
            target = sorted.LastOrDefault(l => l < cur, sorted[^1]);
        if (target >= 1 && target <= ed.Document.LineCount)
        {
            var l = ed.Document.GetLineByNumber(target);
            ed.CaretOffset = l.Offset;
            ed.ScrollToLine(target);
        }
    }

    private void ClearAllBookmarks()
    {
        var doc = ActiveDoc;
        if (doc == null) return;
        doc.Bookmarks.Clear();
        RefreshBookmarkRender(doc);
    }

    private async void ProcessBookmarks(bool delete, bool quiet)
    {
        var doc = ActiveDoc;
        var ed = doc?.Editor;
        if (doc == null || ed == null) return;
        var collected = new List<string>();
        var d = ed.Document;
        using (d.RunUpdate())
        {
            for (int i = d.LineCount; i >= 1; i--)
            {
                if (!doc.Bookmarks.Contains(i)) continue;
                var line = d.GetLineByNumber(i);
                collected.Insert(0, d.GetText(line.Offset, line.Length));
                if (delete) d.Remove(line.Offset, line.TotalLength);
            }
        }
        if (delete) { doc.Bookmarks.Clear(); RefreshBookmarkRender(doc); }
        if (!quiet && collected.Count > 0 && Clipboard != null)
            await Clipboard.SetTextAsync(string.Join(GetNewline(ed), collected)!);
    }

    private void InverseBookmarkDelete()
    {
        var doc = ActiveDoc;
        var ed = doc?.Editor;
        if (doc == null || ed == null) return;
        var d = ed.Document;
        using (d.RunUpdate())
        {
            for (int i = d.LineCount; i >= 1; i--)
            {
                if (doc.Bookmarks.Contains(i)) continue;
                var line = d.GetLineByNumber(i);
                d.Remove(line.Offset, line.TotalLength);
            }
        }
        doc.Bookmarks.Clear();
        RefreshBookmarkRender(doc);
    }

    private async void PasteToBookmarks()
    {
        var doc = ActiveDoc;
        var ed = doc?.Editor;
        if (doc == null || ed == null || Clipboard == null) return;
        string? clip = await Clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(clip)) return;
        string nl = GetNewline(ed);
        var d = ed.Document;
        using (d.RunUpdate())
        {
            for (int i = d.LineCount; i >= 1; i--)
            {
                if (!doc.Bookmarks.Contains(i)) continue;
                var line = d.GetLineByNumber(i);
                d.Replace(line.Offset, line.Length, clip);
            }
        }
    }

    // ---- Folding ----

    private void InstallFolding(EditorDocument doc, TextEditor editor)
    {
        if (doc.FoldingManager != null) return;
        doc.FoldingManager = FoldingManager.Install(editor.TextArea);
        doc.FoldingStrategy = new BraceFoldingStrategy();
        UpdateFoldings(doc);
    }

    private void UninstallFolding(EditorDocument doc)
    {
        if (doc.FoldingManager != null)
        {
            FoldingManager.Uninstall(doc.FoldingManager);
            doc.FoldingManager = null;
        }
    }

    private void UpdateFoldings(EditorDocument doc)
    {
        if (doc.FoldingManager == null || doc.FoldingStrategy == null || doc.Editor == null) return;
        if (doc.Editor.Document.TextLength > 500_000) return; // skip very large files
        doc.FoldingStrategy.UpdateFoldings(doc.FoldingManager, doc.Editor.Document);
    }

    private void ScheduleFoldingUpdate(EditorDocument doc)
    {
        if (doc.FoldingManager == null) return;
        Dispatcher.UIThread.Post(() => UpdateFoldings(doc), DispatcherPriority.Background);
    }

    private void ToggleFolding()
    {
        _foldingEnabled = !_foldingEnabled;
        _foldViewItem.IsChecked = _foldingEnabled;
        foreach (var doc in _docs.Values)
        {
            if (doc.Editor == null || doc.IsLargeFile) continue; // folding stays off for large files
            if (_foldingEnabled) InstallFolding(doc, doc.Editor);
            else UninstallFolding(doc);
        }
    }

    private void CollapseAll()
    {
        var doc = ActiveDoc;
        if (doc?.FoldingManager == null) return;
        foreach (var f in doc.FoldingManager.AllFoldings) f.IsFolded = true;
    }

    private void ExpandAll()
    {
        var doc = ActiveDoc;
        if (doc?.FoldingManager == null) return;
        foreach (var f in doc.FoldingManager.AllFoldings) f.IsFolded = false;
    }
}

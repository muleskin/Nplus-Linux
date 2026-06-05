using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using NPlus.Models;

namespace NPlus.Search;

public sealed class SearchOptions
{
    public bool MatchCase;
    public bool WholeWord;
    public bool Regex;
    public bool Extended;
    public bool Backward;
    public bool Wrap = true;
}

/// <summary>
/// Find / Replace / Count / Mark search engine over an AvaloniaEdit document,
/// reproducing the Normal / Extended / Regex behavior of the original Scintilla-based dialog.
/// </summary>
public static class SearchEngine
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static Regex BuildRegex(string pattern, SearchOptions o)
    {
        string effective = pattern;
        if (!o.Regex)
        {
            if (o.Extended) effective = Unescape(effective);
            effective = Regex.Escape(effective);
            if (o.WholeWord) effective = $@"\b{effective}\b";
        }
        else if (o.WholeWord)
        {
            effective = $@"\b(?:{effective})\b";
        }
        var opts = o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        return new Regex(effective, opts, Timeout);
    }

    public static string ProcessReplacement(string replacement, SearchOptions o) =>
        (!o.Regex && o.Extended) ? Unescape(replacement) : replacement;

    private static string Unescape(string s)
    {
        try { return Regex.Unescape(s); }
        catch { return s; }
    }

    /// <summary>Finds and selects the next match. Returns true on success.</summary>
    public static bool FindNext(TextEditor ed, string pattern, SearchOptions o)
    {
        if (ed.Document == null || string.IsNullOrEmpty(pattern)) return false;
        var rx = BuildRegex(pattern, o);
        string text = ed.Document.Text;
        int caret = ed.SelectionLength > 0 ? ed.SelectionStart : ed.CaretOffset;

        Match? found = null;
        if (o.Backward)
        {
            int limit = ed.SelectionStart;
            foreach (Match m in rx.Matches(text))
            {
                if (m.Index < limit) found = m; else break;
            }
            if (found == null && o.Wrap)
            {
                Match? last = null;
                foreach (Match m in rx.Matches(text)) last = m;
                found = last;
            }
        }
        else
        {
            int from = ed.SelectionLength > 0 ? ed.SelectionStart + 1 : caret;
            if (from > text.Length) from = text.Length;
            var m = rx.Match(text, from);
            if (m.Success) found = m;
            else if (o.Wrap)
            {
                var w = rx.Match(text, 0);
                if (w.Success) found = w;
            }
        }

        if (found == null || !found.Success) return false;
        ed.Select(found.Index, found.Length);
        ed.CaretOffset = found.Index + found.Length;
        ed.TextArea.Caret.BringCaretToView();
        ed.ScrollToLine(ed.Document.GetLineByOffset(found.Index).LineNumber);
        return true;
    }

    /// <summary>Replaces the current selection if it matches, then advances.</summary>
    public static bool ReplaceNext(TextEditor ed, string pattern, string replacement, SearchOptions o)
    {
        if (ed.Document == null) return false;
        var rx = BuildRegex(pattern, o);
        string repl = ProcessReplacement(replacement, o);
        if (ed.SelectionLength > 0)
        {
            var m = rx.Match(ed.SelectedText);
            if (m.Success && m.Index == 0 && m.Length == ed.SelectedText.Length)
            {
                string actual = o.Regex ? m.Result(repl) : repl;
                int start = ed.SelectionStart;
                ed.Document.Replace(start, ed.SelectionLength, actual);
                ed.CaretOffset = start + actual.Length;
            }
        }
        return FindNext(ed, pattern, o);
    }

    public static int ReplaceAll(TextEditor ed, string pattern, string replacement, SearchOptions o)
    {
        if (ed.Document == null || string.IsNullOrEmpty(pattern)) return 0;
        var rx = BuildRegex(pattern, o);
        string repl = ProcessReplacement(replacement, o);
        string text = ed.Document.Text;
        int count = rx.Matches(text).Count;
        if (count == 0) return 0;
        string result = o.Regex
            ? rx.Replace(text, repl)
            : rx.Replace(text, _ => repl);
        ed.Document.Text = result;
        return count;
    }

    public static int Count(TextEditor ed, string pattern, SearchOptions o)
    {
        if (ed.Document == null || string.IsNullOrEmpty(pattern)) return 0;
        return BuildRegex(pattern, o).Matches(ed.Document.Text).Count;
    }

    public static int MarkAll(TextEditor ed, EditorDocument doc, string pattern, SearchOptions o, bool bookmark, bool purge)
    {
        if (ed.Document == null || string.IsNullOrEmpty(pattern)) return 0;
        if (purge) doc.MarkRenderer?.Clear();
        var rx = BuildRegex(pattern, o);
        var segments = new List<ISegment>();
        if (!purge && doc.MarkRenderer != null) segments.AddRange(doc.MarkRenderer.Segments);
        int count = 0;
        foreach (Match m in rx.Matches(ed.Document.Text))
        {
            if (m.Length == 0) continue;
            segments.Add(new TextSegment { StartOffset = m.Index, Length = m.Length });
            if (bookmark)
            {
                int line = ed.Document.GetLineByOffset(m.Index).LineNumber;
                doc.Bookmarks.Add(line);
            }
            count++;
        }
        doc.MarkRenderer?.SetSegments(segments);
        ed.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        ed.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        return count;
    }

    public static void ClearMarks(EditorDocument doc, TextEditor? ed)
    {
        doc.MarkRenderer?.Clear();
        ed?.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
    }
}

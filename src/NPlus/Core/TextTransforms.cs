using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NPlus.Core;

/// <summary>
/// Pure text transforms ported from the WinForms EditorForm line/blank operations.
/// Whole-document operations take and return a full text string; selection-scoped
/// operations work on a list of lines (without EOLs) supplied by the UI layer.
/// </summary>
public static class TextTransforms
{
    private static readonly string[] EolSplitters = { "\r\n", "\r", "\n" };

    public static string[] SplitLines(string text) =>
        text.Split(EolSplitters, StringSplitOptions.None);

    // ---- Whole-document line operations -------------------------------------

    public static string RemoveDuplicateLines(string text, string nl) =>
        string.Join(nl, SplitLines(text).Distinct());

    public static string RemoveConsecutiveDuplicateLines(string text, string nl)
    {
        var lines = SplitLines(text);
        if (lines.Length == 0) return text;
        var result = new List<string> { lines[0] };
        for (int i = 1; i < lines.Length; i++)
            if (lines[i] != lines[i - 1]) result.Add(lines[i]);
        return string.Join(nl, result);
    }

    public static string RemoveEmptyLines(string text, bool includeWhitespace, string nl)
    {
        var lines = SplitLines(text);
        var filtered = includeWhitespace
            ? lines.Where(l => !string.IsNullOrWhiteSpace(l))
            : lines.Where(l => l.Length > 0);
        return string.Join(nl, filtered);
    }

    // ---- Selection-scoped list operations -----------------------------------

    public static void ReverseLines(List<string> lines) => lines.Reverse();

    public static void RandomizeLines(List<string> lines)
    {
        var rng = new Random();
        for (int i = lines.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (lines[i], lines[j]) = (lines[j], lines[i]);
        }
    }

    public static List<string> SortLines(List<string> lines, SortMode mode, bool descending, bool ignoreCase)
    {
        IOrderedEnumerable<string> sorted;
        switch (mode)
        {
            case SortMode.Lexicographic:
                var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                sorted = descending ? lines.OrderByDescending(l => l, comparer) : lines.OrderBy(l => l, comparer);
                break;
            case SortMode.Locale:
                var localeComp = ignoreCase ? StringComparer.CurrentCultureIgnoreCase : StringComparer.CurrentCulture;
                sorted = descending ? lines.OrderByDescending(l => l, localeComp) : lines.OrderBy(l => l, localeComp);
                break;
            case SortMode.Integer:
                sorted = descending
                    ? lines.OrderByDescending(l => long.TryParse(l.Trim(), out var v) ? v : long.MaxValue)
                    : lines.OrderBy(l => long.TryParse(l.Trim(), out var v) ? v : long.MaxValue);
                break;
            case SortMode.DecimalDot:
                sorted = descending
                    ? lines.OrderByDescending(l => double.TryParse(l.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : double.MaxValue)
                    : lines.OrderBy(l => double.TryParse(l.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : double.MaxValue);
                break;
            case SortMode.DecimalComma:
                var commaFmt = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };
                sorted = descending
                    ? lines.OrderByDescending(l => double.TryParse(l.Trim(), NumberStyles.Any, commaFmt, out var v) ? v : double.MaxValue)
                    : lines.OrderBy(l => double.TryParse(l.Trim(), NumberStyles.Any, commaFmt, out var v) ? v : double.MaxValue);
                break;
            case SortMode.Length:
                sorted = descending
                    ? lines.OrderByDescending(l => l.TrimEnd('\r', '\n').Length)
                    : lines.OrderBy(l => l.TrimEnd('\r', '\n').Length);
                break;
            default:
                sorted = lines.OrderBy(l => l);
                break;
        }
        return sorted.ToList();
    }

    // ---- Blank operations (whole document) ----------------------------------

    public static string TrimTrailing(string text, string nl) => PerLine(text, nl, line => line.TrimEnd());
    public static string TrimLeading(string text, string nl) => PerLine(text, nl, line => line.TrimStart());
    public static string TrimBoth(string text, string nl) => PerLine(text, nl, line => line.Trim());

    public static string EolToSpace(string text) =>
        text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

    public static string TrimBothAndEolToSpace(string text)
    {
        var lines = SplitLines(text);
        for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Trim();
        return string.Join(" ", lines);
    }

    public static string TabToSpace(string text, int tabWidth) =>
        text.Replace("\t", new string(' ', tabWidth > 0 ? tabWidth : 4));

    public static string SpaceToTabAll(string text, int tabWidth)
    {
        var spaces = new string(' ', tabWidth > 0 ? tabWidth : 4);
        return text.Replace(spaces, "\t");
    }

    public static string SpaceToTabLeading(string text, int tabWidth, string nl)
    {
        var spaces = new string(' ', tabWidth > 0 ? tabWidth : 4);
        var lines = SplitLines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            string content = lines[i];
            int leadingEnd = 0;
            while (leadingEnd < content.Length && (content[leadingEnd] == ' ' || content[leadingEnd] == '\t')) leadingEnd++;
            string leading = content.Substring(0, leadingEnd).Replace(spaces, "\t");
            lines[i] = leading + content.Substring(leadingEnd);
        }
        return string.Join(nl, lines);
    }

    /// <summary>Applies a per-line transform across the whole document, preserving line splits.</summary>
    private static string PerLine(string text, string nl, Func<string, string> transform)
    {
        var lines = SplitLines(text);
        for (int i = 0; i < lines.Length; i++) lines[i] = transform(lines[i]);
        return string.Join(nl, lines);
    }
}

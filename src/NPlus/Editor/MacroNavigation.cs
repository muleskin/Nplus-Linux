using System;
using AvaloniaEdit.Document;
using NPlus.Core;

namespace NPlus.Editor;

/// <summary>
/// Pure caret-movement math for macro playback — the offset a navigation command moves the caret to.
/// Kept UI-free (it works on a <see cref="TextDocument"/>) so it can be unit-tested without a window;
/// the selection/clipboard side-effects live in the editor layer.
/// </summary>
public static class MacroNavigation
{
    /// <summary>
    /// Returns the caret offset that <paramref name="cmd"/> moves to from <paramref name="caret"/>.
    /// Only plain (non-"Extend") move commands are expected; callers map Extend variants to their
    /// base move and apply selection separately. <paramref name="pageLines"/> is the page size for
    /// PageUp/PageDown.
    /// </summary>
    public static int ComputeMoveTarget(TextDocument d, MacroKeyCommand cmd, int caret, int pageLines)
    {
        caret = Math.Clamp(caret, 0, d.TextLength);
        switch (cmd)
        {
            case MacroKeyCommand.CharLeft: return Math.Max(0, caret - 1);
            case MacroKeyCommand.CharRight: return Math.Min(d.TextLength, caret + 1);
            case MacroKeyCommand.WordLeft:
            {
                int p = TextUtilities.GetNextCaretPosition(d, caret, LogicalDirection.Backward, CaretPositioningMode.WordBorder);
                return p < 0 ? 0 : p;
            }
            case MacroKeyCommand.WordRight:
            {
                int p = TextUtilities.GetNextCaretPosition(d, caret, LogicalDirection.Forward, CaretPositioningMode.WordBorder);
                return p < 0 ? d.TextLength : p;
            }
            case MacroKeyCommand.LineStart: return d.GetLineByOffset(caret).Offset;
            case MacroKeyCommand.LineEnd: return d.GetLineByOffset(caret).EndOffset;
            case MacroKeyCommand.DocumentStart: return 0;
            case MacroKeyCommand.DocumentEnd: return d.TextLength;
            case MacroKeyCommand.LineUp: return MoveByLines(d, caret, -1);
            case MacroKeyCommand.LineDown: return MoveByLines(d, caret, +1);
            case MacroKeyCommand.PageUp: return MoveByLines(d, caret, -pageLines);
            case MacroKeyCommand.PageDown: return MoveByLines(d, caret, +pageLines);
            default: return caret;
        }
    }

    /// <summary>Moves <paramref name="delta"/> lines from the caret, preserving the column where possible.</summary>
    public static int MoveByLines(TextDocument d, int caret, int delta)
    {
        var line = d.GetLineByOffset(caret);
        int column = caret - line.Offset;
        int targetNumber = Math.Clamp(line.LineNumber + delta, 1, d.LineCount);
        var target = d.GetLineByNumber(targetNumber);
        return Math.Min(target.Offset + column, target.EndOffset);
    }
}

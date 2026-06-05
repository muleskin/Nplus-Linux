using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace NPlus.Editor;

/// <summary>
/// Simple brace-based folding (folds regions between matching { and }).
/// A lightweight replacement for Scintilla's automatic folding.
/// </summary>
public sealed class BraceFoldingStrategy
{
    public char OpeningBrace { get; set; } = '{';
    public char ClosingBrace { get; set; } = '}';

    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var newFoldings = CreateNewFoldings(document, out int firstErrorOffset);
        manager.UpdateFoldings(newFoldings, firstErrorOffset);
    }

    public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
    {
        firstErrorOffset = -1;
        var newFoldings = new List<NewFolding>();
        var startOffsets = new Stack<int>();
        int lastNewLineOffset = 0;
        char openingBrace = OpeningBrace;
        char closingBrace = ClosingBrace;

        for (int i = 0; i < document.TextLength; i++)
        {
            char c = document.GetCharAt(i);
            if (c == openingBrace)
            {
                startOffsets.Push(i);
            }
            else if (c == closingBrace && startOffsets.Count > 0)
            {
                int startOffset = startOffsets.Pop();
                // Don't fold what's on a single line.
                if (startOffset < lastNewLineOffset)
                    newFoldings.Add(new NewFolding(startOffset, i + 1));
            }
            else if (c == '\n' || c == '\r')
            {
                lastNewLineOffset = i + 1;
            }
        }

        newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return newFoldings;
    }
}

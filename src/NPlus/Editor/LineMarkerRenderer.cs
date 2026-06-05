using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace NPlus.Editor;

/// <summary>Draws a marker bar at the left edge of bookmarked lines.</summary>
public sealed class LineMarkerRenderer : IBackgroundRenderer
{
    private readonly HashSet<int> _lines;
    private readonly IBrush _brush = new SolidColorBrush(Color.FromRgb(95, 160, 228));

    public LineMarkerRenderer(HashSet<int> bookmarkedLines) => _lines = bookmarkedLines;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lines.Count == 0 || !textView.VisualLinesValid) return;
        foreach (var vl in textView.VisualLines)
        {
            int lineNumber = vl.FirstDocumentLine.LineNumber;
            if (!_lines.Contains(lineNumber)) continue;
            double top = vl.VisualTop - textView.VerticalOffset;
            double height = vl.Height;
            drawingContext.FillRectangle(_brush, new Rect(0, top, 4, height));
        }
    }
}

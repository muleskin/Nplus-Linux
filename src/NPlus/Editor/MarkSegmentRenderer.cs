using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NPlus.Editor;

/// <summary>Highlights "marked" text ranges (Find → Mark All), like Scintilla's indicator.</summary>
public sealed class MarkSegmentRenderer : IBackgroundRenderer
{
    private readonly List<ISegment> _segments = new();
    private readonly IBrush _brush = new SolidColorBrush(Color.FromArgb(110, 0, 200, 200));

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyList<ISegment> Segments => _segments;

    public void SetSegments(IEnumerable<ISegment> segments)
    {
        _segments.Clear();
        _segments.AddRange(segments);
    }

    public void Clear() => _segments.Clear();

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_segments.Count == 0 || !textView.VisualLinesValid) return;
        var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
        foreach (var seg in _segments)
            builder.AddSegment(textView, seg);
        var geometry = builder.CreateGeometry();
        if (geometry != null)
            drawingContext.DrawGeometry(_brush, null, geometry);
    }
}
